"""佳怡的对话图。

顺序是固定的：裁剪记忆、收下这句话、检索原句、按人物卡起草、审计。
审计不过就重写一次。还是不过，就退回一句本地的短话，不把模型原文送去朗读。
她自己先说出口的语料句，用 remember_line 写进同一条记忆，不另叫模型。
"""

from __future__ import annotations

from typing import Literal

from langchain_core.messages import AIMessage, HumanMessage, RemoveMessage, SystemMessage
from langgraph.graph import END, START, StateGraph, add_messages
from typing_extensions import Annotated, TypedDict

from persona_dialogue.audit import audit, clean_reply, fit_reply
from persona_dialogue.distill import render_prompt
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

_MEMORY_LIMIT = 16
_REMEMBER_LIMIT = 120
_FALLBACK = "我在呢，你慢慢说。"


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


def build_graph(model, soul: dict, index: LineIndex, checkpointer, persona: PersonaSetting | None = None):
    persona = persona or PersonaSetting(None)

    def _known_facts(state: DialogueState) -> list[str]:
        facts = [fact for fact in sanitize_facts(state.get("facts")) if not fact.startswith("她是对方的")]
        persona.adopt(sanitize_facts(state.get("facts")))
        relation = persona.get()
        if relation:
            return [relation, *facts]
        return facts
    def trim(state: DialogueState) -> dict:
        messages = state.get("messages") or []
        if len(messages) <= _MEMORY_LIMIT:
            return {}
        expired = [
            RemoveMessage(id=message.id)
            for message in messages[:-_MEMORY_LIMIT]
            if getattr(message, "id", None)
        ]
        return {"messages": expired} if expired else {}

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

        history = list(state.get("messages") or [])[-12:]
        prompt = render_prompt(
            soul,
            state.get("retrieved") or [],
            state.get("failure") or "",
            prior=_prior_spoken(history),
            max_chars=reply_limit(settings),
            facts=_known_facts(state),
        )
        response = _speak(model, [SystemMessage(content=prompt), *history], reply_limit(settings))
        return {"draft": _message_text(response), "actions": [], "settings": settings}

    def rewrite(state: DialogueState) -> dict:
        history = list(state.get("messages") or [])[-12:]
        prompt = render_prompt(
            soul,
            state.get("retrieved") or [],
            state.get("failure") or "",
            prior=_prior_spoken(history),
            max_chars=reply_limit(state.get("settings")),
            facts=_known_facts(state),
        )
        response = _speak(model, [SystemMessage(content=prompt), *history], reply_limit(state.get("settings")))
        return {"draft": _message_text(response)}

    def inspect(state: DialogueState) -> dict:
        cleaned = clean_reply(state.get("draft") or "")
        limit = reply_limit(state.get("settings"))
        user_text = state.get("user_text") or ""
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
    builder.add_node("trim", trim)
    builder.add_node("normalize", normalize)
    builder.add_node("retrieve", retrieve)
    builder.add_node("draft", draft)
    builder.add_node("rewrite", rewrite)
    builder.add_node("inspect", inspect)
    builder.add_node("fallback", fallback)
    builder.add_edge(START, "trim")
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
    messages = list((snapshot.values or {}).get("messages") or [])
    if len(messages) > _MEMORY_LIMIT:
        expired = [
            RemoveMessage(id=message.id)
            for message in messages[:-_MEMORY_LIMIT]
            if getattr(message, "id", None)
        ]
        if expired:
            graph.update_state(config, {"messages": expired}, as_node="trim")
    return True


def _prior_spoken(messages: list) -> str:
    for message in reversed(messages):
        if isinstance(message, HumanMessage):
            continue
        if isinstance(message, AIMessage):
            return _message_text(message)
    return ""


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
