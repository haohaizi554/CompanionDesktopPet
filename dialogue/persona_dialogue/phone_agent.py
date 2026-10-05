"""平板上的对话 agent，步骤和桌面同一条。

人物关系、记事、检索、审计、改写和设置都在这台设备上做。
公网模型只负责起草句子。langgraph 没有安卓轮子，所以这里自己把那些步骤走完。
"""

from __future__ import annotations

import json
import os
import threading
from pathlib import Path
from urllib.request import Request, urlopen

from persona_dialogue.audit import (
    asks_to_repeat,
    audit,
    clean_reply,
    drop_known_sentences,
    fit_reply,
    repeats_earlier_reply,
)
from persona_dialogue.distill import render_prompt
from persona_dialogue.memory import (
    compact_passes,
    compaction_prompt,
    fold_pending,
    heuristic_update,
    live_messages,
    message_text,
    pending_messages,
)
from persona_dialogue.notebook import local_turn, sanitize_facts
from persona_dialogue.persona_setting import PersonaSetting
from persona_dialogue.retrieve import LineIndex
from persona_dialogue.skills import (
    apply_tool_calls,
    confirm_changes,
    normalize_settings,
    output_tokens,
    reply_limit,
    setting_prompt,
    wants_setting,
)

_FALLBACK = "我在呢，你慢慢说。"
_LOCK = threading.Lock()
_REMEMBER_LIMIT = 120
_COMPACT_SYSTEM = "你在整理对话记忆。只输出一个 JSON 对象，不要markdown，不要多余的话。"
_INDEX: LineIndex | None = None
_INDEX_PATH = ""
_TOOLS = [
    {
        "type": "function",
        "function": {
            "name": "set_interval",
            "description": "改她隔多久自己开口。period 只能是 day、evening、late、fullscreen。",
            "parameters": {
                "type": "object",
                "properties": {
                    "period": {"type": "string"},
                    "minimum_minutes": {"type": "integer"},
                    "maximum_minutes": {"type": "integer"},
                },
                "required": ["period", "minimum_minutes", "maximum_minutes"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "set_bubble",
            "description": "改气泡停在屏幕上的秒数，1 到 120。",
            "parameters": {
                "type": "object",
                "properties": {"seconds": {"type": "integer"}},
                "required": ["seconds"],
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "set_speech",
            "description": "改语速 speed、语气 temperature、重复 repetition、采样个数 top_k、采样范围 top_p。没提到的不要填。",
            "parameters": {
                "type": "object",
                "properties": {
                    "speed": {"type": "number"},
                    "temperature": {"type": "number"},
                    "repetition": {"type": "number"},
                    "top_k": {"type": "integer"},
                    "top_p": {"type": "number"},
                },
            },
        },
    },
    {
        "type": "function",
        "function": {
            "name": "set_companion",
            "description": "改大小 scale、置顶 always_on_top、暂停动画 animation_paused、语音开关 voice_enabled。",
            "parameters": {
                "type": "object",
                "properties": {
                    "scale": {"type": "string"},
                    "always_on_top": {"type": "boolean"},
                    "animation_paused": {"type": "boolean"},
                    "voice_enabled": {"type": "boolean"},
                },
            },
        },
    },
]


def reply(
    corpus_path: str,
    soul_path: str,
    state_dir: str,
    text: str,
    base_url: str,
    model: str,
    api_key: str,
) -> str:
    spoken = " ".join((text or "").split()).strip()
    with _LOCK:
        return _reply_locked(corpus_path, soul_path, state_dir, spoken, base_url, model, api_key)


def _reply_locked(corpus_path, soul_path, state_dir, spoken, base_url, model, api_key) -> str:
    try:
        directory = Path(state_dir)
        directory.mkdir(parents=True, exist_ok=True)
        soul = json.loads(Path(soul_path).read_text(encoding="utf-8"))
        index = _lines(corpus_path)
        state = _load(directory / "phone-agent.json")
        settings = normalize_settings(_load(directory / "phone-settings.json") or state.get("settings"))
        persona = PersonaSetting(directory / "persona-setting.json")
        facts = sanitize_facts(state.get("facts"))
        persona.adopt(facts)
        messages = [item for item in state.get("messages") or [] if isinstance(item, dict)]
        summary = str(state.get("memory") or "")
        cover = int(state.get("cover") or 0)
        summary, facts, cover = _compact(base_url, model, api_key, messages, summary, facts, cover)
        messages, summary, facts, cover = _trim(messages, summary, facts, cover)

        if not spoken:
            return _pack(True, "你说呀，我听着。", [])
        if len(spoken) > 200:
            return _finish(directory, messages, summary, facts, cover, settings, [], "一次说短一点，我跟得上。")

        kept = [fact for fact in facts if not fact.startswith("她是对方的")]
        persona.adopt(facts)
        local = local_turn(spoken, kept, relationship=persona.get())
        if local:
            if local.get("relationship"):
                persona.set(str(local["relationship"]))
            facts = sanitize_facts(local.get("facts"))
            messages = [*messages, {"role": "user", "text": spoken}, {"role": "assistant", "text": local["draft"]}]
            return _finish(directory, messages, summary, facts, cover, settings, local.get("actions") or [], local["draft"])

        messages = [*messages, {"role": "user", "text": spoken}]
        earlier = _recent_assistant(messages)
        retrieved = index.search(spoken, limit=4, exclude=earlier[:4])
        limit = reply_limit(settings)
        actions: list = []
        draft, failure = "", ""
        if wants_setting(spoken):
            try:
                message = _chat(
                    base_url,
                    model,
                    api_key,
                    [
                        {"role": "system", "content": setting_prompt(settings)},
                        {"role": "user", "content": spoken},
                    ],
                    tools=_TOOLS,
                    force=True,
                )
                settings, actions, notes = apply_tool_calls(settings, _tool_calls(message))
                draft = confirm_changes(notes) if actions else "这下我没改成，你再说清楚一点。"
            except Exception:
                draft, failure, actions = "", "", []

        setting_draft = bool(draft) and wants_setting(spoken)
        accepted = False
        attempts = 0
        while attempts < 2:
            if not setting_draft or attempts:
                history = live_messages(messages, cover)
                prompt = render_prompt(
                    soul,
                    retrieved,
                    failure,
                    prior=_prior(history),
                    max_chars=limit,
                    facts=_known(facts, persona),
                    memory=_memory_line(summary, history),
                )
                draft = _text(
                    _chat(
                        base_url,
                        model,
                        api_key,
                        [{"role": "system", "content": prompt}, *_turns(history)],
                        max_tokens=output_tokens(limit, 180),
                    )
                )
            raw = clean_reply(draft)
            cleaned = raw if asks_to_repeat(spoken) else drop_known_sentences(raw, _recent_assistant(messages, 6))
            if raw and not cleaned:
                accepted, reason = False, "重复"
            elif not asks_to_repeat(spoken) and repeats_earlier_reply(cleaned, _recent_assistant(messages, 6)):
                accepted, reason = False, "重复"
            else:
                accepted, reason = audit(cleaned, spoken, max_chars=limit)
            if not accepted and reason == "太长":
                fitted = fit_reply(cleaned, limit)
                fitted_ok, fitted_reason = audit(fitted, spoken, max_chars=limit)
                if fitted_ok:
                    cleaned, accepted, reason = fitted, True, ""
                else:
                    reason = fitted_reason
            if accepted:
                draft = cleaned
                break
            draft = cleaned
            failure = reason
            attempts += 1
        if not accepted:
            draft = next((line for line in retrieved if audit(line, spoken, max_chars=limit)[0]), _FALLBACK)
        messages = [*messages, {"role": "assistant", "text": draft}]
        return _finish(directory, messages, summary, facts, cover, settings, actions, draft)
    except Exception as error:
        return _pack(False, f"这句话没接住。{error}", [])


def remember(state_dir: str, text: str) -> str:
    """她自己先说出口的句子写进同一份记忆，不调用模型。"""
    with _LOCK:
        return _remember_locked(state_dir, text)


def _remember_locked(state_dir: str, text: str) -> str:
    cleaned = " ".join((text or "").split()).strip()
    if not cleaned:
        return _pack(True, "", [])
    if len(cleaned) > _REMEMBER_LIMIT:
        cleaned = cleaned[:_REMEMBER_LIMIT].rstrip()
    directory = Path(state_dir)
    directory.mkdir(parents=True, exist_ok=True)
    state = _load(directory / "phone-agent.json")
    messages = [item for item in state.get("messages") or [] if isinstance(item, dict)]
    if messages and message_text(messages[-1]) == cleaned:
        return _pack(True, cleaned, [])
    messages = [*messages, {"role": "assistant", "text": cleaned}]
    summary = str(state.get("memory") or "")
    facts = sanitize_facts(state.get("facts"))
    cover = int(state.get("cover") or 0)
    messages, summary, facts, cover = _trim(messages, summary, facts, cover)
    _save(
        directory / "phone-agent.json",
        {
            "messages": messages,
            "facts": facts,
            "memory": summary,
            "cover": cover,
            "settings": state.get("settings") if isinstance(state.get("settings"), dict) else {},
            "prior": str(state.get("prior") or cleaned),
        },
    )
    return _pack(True, cleaned, [])


def _compact(base_url, model, api_key, messages, summary, facts, cover):
    for _ in range(compact_passes()):
        pending = pending_messages(messages, cover)
        if not pending:
            break
        try:
            model_text = _text(
                _chat(
                    base_url,
                    model,
                    api_key,
                    [
                        {"role": "system", "content": _COMPACT_SYSTEM},
                        {"role": "user", "content": compaction_prompt(summary, facts, pending)},
                    ],
                    temperature=0.7,
                )
            )
        except Exception:
            model_text = None
        summary, facts = fold_pending(summary, facts, pending, model_text)
        cover += len(pending)
    return summary, sanitize_facts(facts), cover


def _trim(messages: list, summary: str, facts: list, cover: int):
    overflow = len(messages) - 200
    if overflow <= 0:
        return messages, summary, sanitize_facts(facts), cover
    index = min(max(int(cover or 0), 0), len(messages))
    if index < overflow:
        summary, facts = heuristic_update(summary, facts, messages[index:overflow])
        index += overflow - index
    drop = min(overflow, index)
    return messages[drop:], summary, sanitize_facts(facts), index - drop


def _known(facts: list[str], persona: PersonaSetting) -> list[str]:
    kept = [fact for fact in sanitize_facts(facts) if not fact.startswith("她是对方的")]
    persona.adopt(sanitize_facts(facts))
    relation = persona.get()
    if relation:
        return [relation, *kept]
    return kept


def _recent_assistant(messages: list, limit: int = 4) -> list[str]:
    spoken: list[str] = []
    for message in reversed(messages):
        if isinstance(message, dict) and message.get("role") == "assistant":
            text = message_text(message)
            if text:
                spoken.append(text)
        if len(spoken) == limit:
            break
    return spoken


def _prior(messages: list) -> str:
    for message in reversed(messages):
        if isinstance(message, dict) and message.get("role") == "assistant":
            return message_text(message)
    return ""


def _turns(messages: list) -> list[dict]:
    turns = []
    for message in messages:
        if not isinstance(message, dict):
            continue
        text = message_text(message)
        if not text:
            continue
        role = "user" if message.get("role") in {"user", "human"} else "assistant"
        turns.append({"role": role, "content": text})
    return turns


def _chat(base_url, model, api_key, messages, max_tokens=180, temperature=0.7, tools=None, force=False) -> dict:
    payload = {
        "model": model,
        "messages": messages,
        "max_tokens": max_tokens,
        "temperature": temperature,
        "chat_template_kwargs": {"enable_thinking": False},
    }
    if tools:
        payload["tools"] = tools
        payload["tool_choice"] = "required" if force else "auto"
    request = Request(
        base_url.rstrip("/") + "/chat/completions",
        data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
        headers={
            "Content-Type": "application/json",
            "x-api-key": api_key,
            "Authorization": "Bearer " + api_key,
        },
        method="POST",
    )
    with urlopen(request, timeout=45) as response:
        body = json.loads(response.read().decode("utf-8"))
    message = body["choices"][0]["message"]
    return message if isinstance(message, dict) else {"content": str(message)}


def _text(message: dict) -> str:
    return str(message.get("content") or message.get("reasoning_content") or "")


def _tool_calls(message: dict) -> list[dict]:
    calls = []
    for call in message.get("tool_calls") or []:
        function = call.get("function") if isinstance(call, dict) else None
        function = function if isinstance(function, dict) else {}
        arguments = function.get("arguments") or {}
        if isinstance(arguments, str):
            try:
                arguments = json.loads(arguments)
            except json.JSONDecodeError:
                arguments = {}
        calls.append({"name": function.get("name") or "", "args": arguments if isinstance(arguments, dict) else {}})
    return calls


def _lines(corpus_path: str) -> LineIndex:
    global _INDEX, _INDEX_PATH
    if _INDEX is None or _INDEX_PATH != corpus_path:
        _INDEX = LineIndex.from_corpus(Path(corpus_path))
        _INDEX_PATH = corpus_path
    return _INDEX


def _finish(directory: Path, messages, summary, facts, cover, settings, actions, text) -> str:
    messages, summary, facts, cover = _trim(messages, summary, facts, cover)
    _save(
        directory / "phone-agent.json",
        {
            "messages": messages,
            "facts": facts,
            "memory": summary,
            "cover": cover,
            "settings": settings,
            "prior": text,
        },
    )
    return _pack(True, text, actions if isinstance(actions, list) else [])


def _load(path: Path) -> dict:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
        return value if isinstance(value, dict) else {}
    except (OSError, json.JSONDecodeError):
        return {}


def _memory_line(summary: str, history: list) -> str:
    """摘要后面带上还没被盖住的原话，避免模型只看见这一句。"""
    lines = [str(summary or "").strip()]
    for message in history[-13:-1]:
        text = message_text(message)
        if not text:
            continue
        who = "对方" if isinstance(message, dict) and message.get("role") in {"user", "human"} else "佳怡"
        lines.append(f"{who}：{text}")
    return "\n".join(line for line in lines if line)


def _save(path: Path, state: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(state, ensure_ascii=False))
        handle.flush()
        os.fsync(handle.fileno())
    temporary.replace(path)


def _pack(ok: bool, text: str, actions: list) -> str:
    return json.dumps({"ok": ok, "text": text, "actions": actions}, ensure_ascii=False)
