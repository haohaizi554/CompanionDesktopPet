"""佳怡的对话图。

顺序是固定的：把滑出近期窗口的旧对话收成摘要、删掉已经被摘要盖住的超额原文、
收下这句话、检索原句、按人物卡起草、审计。
原文至少留两百条。模型只看摘要加上还没被摘要盖住的最近一段。
审计不过就重写一次。还是不过，就退回一句本地的短话，不把模型原文送去朗读。
她自己先说出口的语料句，用 remember_line 写进同一条记忆，不另叫模型。
"""

from __future__ import annotations

from typing import Literal

from langchain_core.messages import AIMessage, HumanMessage, SystemMessage
from langgraph.graph import END, START, StateGraph, add_messages
from typing_extensions import Annotated, TypedDict

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
    expire_overflow,
    fold_pending,
    fold_transcript,
    live_messages,
    pending_messages,
)
from persona_dialogue.notebook import local_turn, sanitize_facts
from persona_dialogue.persona_setting import PersonaSetting
from persona_dialogue.retrieve import LineIndex
from persona_dialogue.skills import (
    SKILL_TOOLS,
    apply_tool_calls,
    confirm_changes,
    normalize_settings,
    output_tokens,
    reply_limit,
    setting_prompt,
    wants_setting,
)

_REMEMBER_LIMIT = 120
_FALLBACK = "我在呢，你慢慢说。"
_COMPACT_SYSTEM = "你在整理对话记忆。只输出一个 JSON 对象，不要markdown，不要多余的话。"


class DialogueState(TypedDict, total=False):
    messages: Annotated[list, add_messages]
    user_text: str
    retrieved: list[str]
    draft: str
    attempts: int
    accepted: bool
    failure: str
    settings: dict
    actions: list
    facts: list
    memory_summary: str
    compacted_count: int


def build_graph(model, soul: dict, index: LineIndex, checkpointer, persona: PersonaSetting | None = None):
    persona = persona or PersonaSetting(None)

    def _known_facts(state: DialogueState) -> list[str]:
        facts = [fact for fact in sanitize_facts(state.get("facts")) if not fact.startswith("她是对方的")]
        persona.adopt(sanitize_facts(state.get("facts")))
        relation = persona.get()
        if relation:
            return [relation, *facts]
        return facts
    def compact(state: DialogueState) -> dict:
        messages = list(state.get("messages") or [])
        cover = int(state.get("compacted_count") or 0)
        summary = state.get("memory_summary") or ""
        facts = sanitize_facts(state.get("facts"))
        changed = False
        for _ in range(compact_passes()):
            pending = pending_messages(messages, cover)
            if not pending:
                break
            try:
                response = _invoke(
                    model,
                    [
                        SystemMessage(content=_COMPACT_SYSTEM),
                        HumanMessage(content=compaction_prompt(summary, facts, pending)),
                    ],
                    tools=False,
                )
                model_text = _message_text(response)
            except Exception:
                model_text = None
            summary, facts = fold_pending(summary, facts, pending, model_text)
            cover += len(pending)
            changed = True
        if not changed:
            return {}
        return {"memory_summary": summary, "facts": facts, "compacted_count": cover}

    def trim(state: DialogueState) -> dict:
        messages = list(state.get("messages") or [])
        expired, cover = expire_overflow(messages, state.get("compacted_count") or 0)
        if not expired:
            return {}
        return {"messages": expired, "compacted_count": cover}

    def normalize(state: DialogueState) -> dict:
        text = (state.get("user_text") or "").strip()
        if not text:
            return {
                "user_text": "",
                "draft": "你说呀，我听着。",
                "accepted": True,
                "failure": "empty",
                "attempts": 99,
                "retrieved": [],
                "actions": [],
            }
        if len(text) > 200:
            return {
                "user_text": text[:200],
                "draft": "一次说短一点，我跟得上。",
                "accepted": True,
                "failure": "long",
                "attempts": 99,
                "retrieved": [],
                "actions": [],
            }
        kept = [fact for fact in sanitize_facts(state.get("facts")) if not fact.startswith("她是对方的")]
        persona.adopt(sanitize_facts(state.get("facts")))
        local = local_turn(text, kept, relationship=persona.get())
        if local:
            if local.get("relationship"):
                persona.set(local["relationship"])
            return {
                "user_text": text,
                "draft": local["draft"],
                "accepted": True,
                "failure": "local",
                "attempts": 99,
                "retrieved": [],
                "actions": local["actions"],
                "facts": local["facts"],
                "messages": [HumanMessage(content=text), AIMessage(content=local["draft"])],
            }
        return {
            "user_text": text,
            "draft": "",
            "accepted": False,
            "failure": "",
            "attempts": 0,
            "retrieved": [],
            "actions": [],
            "messages": [HumanMessage(content=text)],
        }

    def retrieve(state: DialogueState) -> dict:
        spoken: list[str] = []
        for message in reversed(state.get("messages") or []):
            if not isinstance(message, AIMessage):
                continue
            text = _message_text(message).strip()
            if not text:
                continue
            spoken.append(text)
            if len(spoken) == 4:
                break
        return {"retrieved": index.search(state.get("user_text") or "", limit=4, exclude=spoken)}

    def draft(state: DialogueState) -> dict:
        settings = normalize_settings(state.get("settings"))
        text = state.get("user_text") or ""
        if wants_setting(text):
            response = _invoke(
                model,
                [SystemMessage(content=setting_prompt(settings)), HumanMessage(content=text)],
                tools=True,
                force=True,
            )
            calls = getattr(response, "tool_calls", None) or []
            settings, actions, notes = apply_tool_calls(settings, calls)
            if not actions:
                return {
                    "draft": "这下我没改成，你再说清楚一点。",
                    "actions": [],
                    "settings": settings,
                }
            return {
                "draft": confirm_changes(notes),
                "actions": actions,
                "settings": settings,
            }

        history = _live_history(state)
        prompt = render_prompt(
            soul,
            state.get("retrieved") or [],
            state.get("failure") or "",
            prior=_prior_spoken(history),
            max_chars=reply_limit(settings),
            facts=_known_facts(state),
            memory=state.get("memory_summary") or "",
        )
        response = _speak(model, [SystemMessage(content=prompt), *history], reply_limit(settings))
        return {"draft": _message_text(response), "actions": [], "settings": settings}

    def rewrite(state: DialogueState) -> dict:
        history = _live_history(state)
        prompt = render_prompt(
            soul,
            state.get("retrieved") or [],
            state.get("failure") or "",
            prior=_prior_spoken(history),
            max_chars=reply_limit(state.get("settings")),
            facts=_known_facts(state),
            memory=state.get("memory_summary") or "",
        )
        response = _speak(model, [SystemMessage(content=prompt), *history], reply_limit(state.get("settings")))
        return {"draft": _message_text(response)}

    def inspect(state: DialogueState) -> dict:
        user_text = state.get("user_text") or ""
        raw = clean_reply(state.get("draft") or "")
        recent = _recent_spoken(list(state.get("messages") or []))
        cleaned = raw if asks_to_repeat(user_text) else drop_known_sentences(raw, recent)
        limit = reply_limit(state.get("settings"))
        if raw and not cleaned:
            accepted, reason = False, "重复"
        elif not asks_to_repeat(user_text) and repeats_earlier_reply(cleaned, recent):
            accepted, reason = False, "重复"
        else:
            accepted, reason = audit(cleaned, user_text, max_chars=limit)
        if not accepted and reason == "太长":
            fitted = fit_reply(cleaned, limit)
            fitted_ok, fitted_reason = audit(fitted, user_text, max_chars=limit)
            if fitted_ok:
                cleaned, accepted, reason = fitted, True, ""
            else:
                reason = fitted_reason
        if accepted:
            return {
                "draft": cleaned,
                "accepted": True,
                "failure": "",
                "messages": [AIMessage(content=cleaned)],
            }
        return {
            "draft": cleaned,
            "accepted": False,
            "failure": reason,
            "attempts": int(state.get("attempts") or 0) + 1,
        }

    def fallback(state: DialogueState) -> dict:
        retrieved = state.get("retrieved") or []
        limit = reply_limit(state.get("settings"))
        user_text = state.get("user_text") or ""
        text = next((line for line in retrieved if audit(line, user_text, max_chars=limit)[0]), _FALLBACK)
        return {
            "draft": text,
            "accepted": True,
            "failure": "fallback",
            "messages": [AIMessage(content=text)],
        }

    def after_normalize(state: DialogueState) -> Literal["retrieve", "end"]:
        return "end" if state.get("accepted") else "retrieve"

    def after_inspect(state: DialogueState) -> Literal["rewrite", "fallback", "end"]:
        if state.get("accepted"):
            return "end"
        if int(state.get("attempts") or 0) < 2:
            return "rewrite"
        return "fallback"

    builder = StateGraph(DialogueState)
    builder.add_node("compact", compact)
    builder.add_node("trim", trim)
    builder.add_node("normalize", normalize)
    builder.add_node("retrieve", retrieve)
    builder.add_node("draft", draft)
    builder.add_node("rewrite", rewrite)
    builder.add_node("inspect", inspect)
    builder.add_node("fallback", fallback)
    builder.add_edge(START, "compact")
    builder.add_edge("compact", "trim")
    builder.add_edge("trim", "normalize")
    builder.add_conditional_edges(
        "normalize",
        after_normalize,
        {"retrieve": "retrieve", "end": END},
    )
    builder.add_edge("retrieve", "draft")
    builder.add_edge("draft", "inspect")
    builder.add_conditional_edges(
        "inspect",
        after_inspect,
        {"rewrite": "rewrite", "fallback": "fallback", "end": END},
    )
    builder.add_edge("rewrite", "inspect")
    builder.add_edge("fallback", END)
    return builder.compile(checkpointer=checkpointer)


def remember_line(graph, thread_id: str, text: str) -> bool:
    """把她已经说出口的语料句写进对话记忆。不调用模型。"""
    cleaned = " ".join((text or "").split()).strip()
    if not cleaned:
        return False
    if len(cleaned) > _REMEMBER_LIMIT:
        cleaned = cleaned[:_REMEMBER_LIMIT].rstrip()
    config = {"configurable": {"thread_id": thread_id or "jiayi"}}
    snapshot = graph.get_state(config)
    messages = list((snapshot.values or {}).get("messages") or [])
    if messages and _message_text(messages[-1]) == cleaned:
        return False
    graph.update_state(config, {"messages": [AIMessage(content=cleaned)]}, as_node="inspect")
    snapshot = graph.get_state(config)
    values = snapshot.values or {}
    folded = fold_transcript(
        list(values.get("messages") or []),
        values.get("memory_summary") or "",
        values.get("facts"),
        values.get("compacted_count") or 0,
    )
    if folded:
        graph.update_state(config, folded, as_node="trim")
    return True


def _live_history(state: DialogueState) -> list:
    return live_messages(list(state.get("messages") or []), state.get("compacted_count") or 0)


def _prior_spoken(messages: list) -> str:
    recent = _recent_spoken(messages, limit=1)
    return recent[0] if recent else ""


def _recent_spoken(messages: list, limit: int = 6) -> list[str]:
    spoken: list[str] = []
    for message in reversed(messages):
        if not isinstance(message, AIMessage):
            continue
        text = _message_text(message).strip()
        if not text:
            continue
        spoken.append(text)
        if len(spoken) == limit:
            break
    return spoken


def _speak(model, messages, max_chars: int):
    floor = getattr(model, "max_tokens", None)
    tokens = output_tokens(max_chars, floor if isinstance(floor, int) and floor > 0 else 180)
    if hasattr(model, "bind"):
        model = model.bind(max_tokens=tokens)
    return model.invoke(messages)


def _invoke(model, messages, tools: bool, force: bool = False):
    if tools and hasattr(model, "bind_tools"):
        if force:
            model = model.bind_tools(SKILL_TOOLS, tool_choice="required")
        else:
            model = model.bind_tools(SKILL_TOOLS)
    return model.invoke(messages)


def _message_text(response) -> str:
    content = getattr(response, "content", response)
    if isinstance(content, list):
        content = "".join(
            part.get("text", "") if isinstance(part, dict) else str(part) for part in content
        )
    return str(content or "").strip()
