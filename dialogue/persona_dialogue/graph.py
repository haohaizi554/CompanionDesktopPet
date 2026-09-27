"""佳怡的对话图。

顺序是固定的：裁剪记忆、收下这句话、检索原句、按人物卡起草、审计。
审计不过就重写一次。还是不过，就退回一句本地的短话，不把模型原文送去朗读。
"""

from __future__ import annotations

from typing import Literal

from langchain_core.messages import AIMessage, HumanMessage, RemoveMessage, SystemMessage
from langgraph.graph import END, START, StateGraph, add_messages
from typing_extensions import Annotated, TypedDict

from persona_dialogue.audit import audit, clean_reply
from persona_dialogue.distill import render_prompt
from persona_dialogue.retrieve import LineIndex

_MEMORY_LIMIT = 16
_FALLBACK = "我在呢，你慢慢说。"


class DialogueState(TypedDict, total=False):
    messages: Annotated[list, add_messages]
    user_text: str
    retrieved: list[str]
    draft: str
    attempts: int
    accepted: bool
    failure: str


def build_graph(model, soul: dict, index: LineIndex, checkpointer):
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
            }
        if len(text) > 200:
            return {
                "user_text": text[:200],
                "draft": "一次说短一点，我跟得上。",
                "accepted": True,
                "failure": "long",
                "attempts": 99,
                "retrieved": [],
            }
        return {
            "user_text": text,
            "draft": "",
            "accepted": False,
            "failure": "",
            "attempts": 0,
            "retrieved": [],
            "messages": [HumanMessage(content=text)],
        }

    def retrieve(state: DialogueState) -> dict:
        return {"retrieved": index.search(state.get("user_text") or "", limit=4)}

    def draft(state: DialogueState) -> dict:
        prompt = render_prompt(soul, state.get("retrieved") or [], state.get("failure") or "")
        history = list(state.get("messages") or [])[-12:]
        response = model.invoke([SystemMessage(content=prompt), *history])
        content = getattr(response, "content", response)
        if isinstance(content, list):
            content = "".join(
                part.get("text", "") if isinstance(part, dict) else str(part) for part in content
            )
        return {"draft": str(content)}

    def inspect(state: DialogueState) -> dict:
        cleaned = clean_reply(state.get("draft") or "")
        accepted, reason = audit(cleaned, state.get("user_text") or "")
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
        text = next((line for line in retrieved if audit(line, state.get("user_text") or "")[0]), _FALLBACK)
        return {
            "draft": text,
            "accepted": True,
            "failure": "fallback",
            "messages": [AIMessage(content=text)],
        }

    def after_normalize(state: DialogueState) -> Literal["retrieve", "end"]:
        return "end" if state.get("accepted") else "retrieve"

    def after_inspect(state: DialogueState) -> Literal["draft", "fallback", "end"]:
        if state.get("accepted"):
            return "end"
        if int(state.get("attempts") or 0) < 2:
            return "draft"
        return "fallback"

    builder = StateGraph(DialogueState)
    builder.add_node("trim", trim)
    builder.add_node("normalize", normalize)
    builder.add_node("retrieve", retrieve)
    builder.add_node("draft", draft)
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
        {"draft": "draft", "fallback": "fallback", "end": END},
    )
    builder.add_edge("fallback", END)
    return builder.compile(checkpointer=checkpointer)
