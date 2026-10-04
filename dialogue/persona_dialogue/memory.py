"""对话记忆。

原文至少留两百条。模型每次只看最近一段，更早的收成摘要。
事实另外记一份，新的可以替换旧的，过时的删掉。
"""

from __future__ import annotations

import json
import re

from langchain_core.messages import HumanMessage, RemoveMessage

from persona_dialogue.distill import PRIVACY_MARKERS
from persona_dialogue.notebook import sanitize_facts

TRANSCRIPT_LIMIT = 200
RECENT_WINDOW = 16
RECENT_CHAR_BUDGET = 1400
COMPACT_BATCH = 8
COMPACT_TRIGGER_CHARS = 500
COMPACT_INPUT_CHARS = 3600
SUMMARY_CHARS = 480
_COMPACT_PASSES = 4

_STALE_EXAM = re.compile(r"考完|不考了|考试结束|考过了")


def message_text(message) -> str:
    content = getattr(message, "content", "")
    if isinstance(content, list):
        content = "".join(
            part.get("text", "") if isinstance(part, dict) else str(part) for part in content
        )
    return " ".join(str(content or "").split()).strip()


def clamp_cover(messages: list, cover: object) -> int:
    try:
        index = int(cover or 0)
    except (TypeError, ValueError):
        index = 0
    return max(0, min(index, len(messages)))


def recent_messages(messages: list) -> list:
    chosen: list = []
    chars = 0
    for message in reversed(messages):
        extra = len(message_text(message))
        if chosen and (len(chosen) >= RECENT_WINDOW or chars + extra > RECENT_CHAR_BUDGET):
            break
        chosen.append(message)
        chars += extra
    chosen.reverse()
    return chosen


def pending_messages(messages: list, cover: object) -> list:
    """还没写进摘要、又已经滑出近期窗口的那一截。"""
    index = clamp_cover(messages, cover)
    recent = recent_messages(messages)
    uncovered = messages[index : len(messages) - len(recent)]
    if not uncovered:
        return []
    chars = sum(len(message_text(message)) for message in uncovered)
    if (
        len(messages) <= TRANSCRIPT_LIMIT
        and len(uncovered) < COMPACT_BATCH
        and chars < COMPACT_TRIGGER_CHARS
    ):
        return []
    batch: list = []
    total = 0
    for message in uncovered:
        extra = len(message_text(message))
        if batch and total + extra > COMPACT_INPUT_CHARS:
            break
        batch.append(message)
        total += extra
    return batch


def live_messages(messages: list, cover: object) -> list:
    """送给模型的原文：摘要还没盖住的部分，太长就只留最近一截。"""
    index = clamp_cover(messages, cover)
    live = messages[index:]
    if not live:
        return []
    chars = sum(len(message_text(message)) for message in live)
    if len(live) <= RECENT_WINDOW + COMPACT_BATCH and chars <= RECENT_CHAR_BUDGET + COMPACT_TRIGGER_CHARS:
        return live
    return recent_messages(messages)


def expire_overflow(messages: list, cover: object) -> tuple[list, int]:
    """只删已经被摘要盖住的最旧消息，把原文控制在两百条。"""
    index = clamp_cover(messages, cover)
    overflow = len(messages) - TRANSCRIPT_LIMIT
    if overflow <= 0 or index <= 0:
        return [], index
    drop = min(overflow, index)
    batch = messages[:drop]
    if any(not getattr(message, "id", None) for message in batch):
        return [], index
    expired = [RemoveMessage(id=message.id) for message in batch]
    return expired, index - drop


def clip_summary(text: str) -> str:
    cleaned = _scrub(text)
    if len(cleaned) <= SUMMARY_CHARS:
        return cleaned
    window = cleaned[-SUMMARY_CHARS:]
    cut = max(window.find(mark) for mark in ("。", "！", "？", "；"))
    if cut >= 0:
        trimmed = window[cut + 1 :].strip()
        if trimmed:
            return trimmed
    return window.strip()


def parse_memory_update(text: str) -> dict | None:
    raw = (text or "").strip()
    if raw.startswith("```"):
        raw = raw.strip("`")
        if raw.lower().startswith("json"):
            raw = raw[4:].strip()
    start = raw.find("{")
    end = raw.rfind("}")
    if start < 0 or end <= start:
        return None
    try:
        payload = json.loads(raw[start : end + 1])
    except json.JSONDecodeError:
        return None
    if not isinstance(payload, dict):
        return None
    return payload


def compaction_prompt(summary: str, facts: list[str], pending: list) -> str:
    lines = [
        "把更早的对话收成记忆。只输出一个 JSON 对象。",
        "summary：中文一段，最多480字。覆盖整段更早的对话，不要只写这一截。留下称呼、偏好、约定、没做完的事、情绪。寒暄和重复丢掉。",
        "remember：仍然有效的短事实，数组。没有就空数组。",
        "forget：被新信息取代的旧事实，数组。写出旧事实里能对上的词，例如考试已经考完就写「下周考试」。没有就空数组。",
        "不要写隐私小名，不要写她和对方是什么关系。",
    ]
    if summary:
        lines.append("已有摘要：")
        lines.append(summary)
    if facts:
        lines.append("已有事实：")
        lines.extend(f"- {fact}" for fact in facts)
    lines.append("这一截对话：")
    for message in pending:
        role = "对方" if isinstance(message, HumanMessage) else "佳怡"
        lines.append(f"{role}：{message_text(message)}")
    return "\n".join(lines)


def apply_memory_update(summary: str, facts: list[str], payload: dict) -> tuple[str, list[str]]:
    fresh = payload.get("summary") if isinstance(payload.get("summary"), str) else ""
    merged = clip_summary(fresh or summary or "")
    remember = payload.get("remember") if isinstance(payload.get("remember"), list) else []
    forget = payload.get("forget") if isinstance(payload.get("forget"), list) else []
    kept = _drop_forgotten(sanitize_facts(facts), forget)
    kept = sanitize_facts([*kept, *[item for item in remember if isinstance(item, str)]])
    return merged, kept


def heuristic_update(summary: str, facts: list[str], pending: list) -> tuple[str, list[str]]:
    bits: list[str] = []
    for message in pending:
        text = message_text(message)
        if not text:
            continue
        if isinstance(message, HumanMessage):
            bits.append("对方说" + text[:48])
        else:
            bits.append("她说" + text[:36])
    folded = (summary or "").strip()
    if bits:
        addition = "。".join(bits)
        folded = f"{folded} {addition}".strip() if folded else addition
    kept = retire_stale(sanitize_facts(facts), pending)
    return clip_summary(folded), kept


def fold_pending(summary: str, facts: list[str], pending: list, model_text: str | None) -> tuple[str, list[str]]:
    parsed = parse_memory_update(model_text or "")
    if parsed is None:
        return heuristic_update(summary, facts, pending)
    merged, kept = apply_memory_update(summary, facts, parsed)
    kept = retire_stale(kept, pending)
    if not merged:
        return heuristic_update(summary, kept, pending)
    return merged, kept


def retire_stale(facts: list[str], pending: list) -> list[str]:
    human = "\n".join(
        message_text(message) for message in pending if isinstance(message, HumanMessage)
    )
    if not _STALE_EXAM.search(human):
        return list(facts)
    return [fact for fact in facts if "考试" not in fact]


def fold_transcript(messages: list, summary: str, facts: list[str] | None, cover: object) -> dict:
    """不调用模型。原文超过两百条时，先把最旧的收进摘要再删。"""
    index = clamp_cover(messages, cover)
    overflow = len(messages) - TRANSCRIPT_LIMIT
    if overflow <= 0:
        return {}
    text = summary or ""
    kept = sanitize_facts(facts)
    if index < overflow:
        pending = messages[index:overflow]
        text, kept = heuristic_update(text, kept, pending)
        index += len(pending)
    expired, new_cover = expire_overflow(messages, index)
    payload = {
        "memory_summary": text,
        "facts": kept,
        "compacted_count": new_cover if expired else index,
    }
    if expired:
        payload["messages"] = expired
    return payload


def compact_passes() -> int:
    return _COMPACT_PASSES


def _drop_forgotten(facts: list[str], forget: list) -> list[str]:
    needles = []
    for item in forget:
        if not isinstance(item, str):
            continue
        needle = " ".join(item.split()).strip()
        if len(needle) < 2 or needle.startswith("她是对方的"):
            continue
        needles.append(needle)
    if not needles:
        return list(facts)
    kept = []
    for fact in facts:
        if fact.startswith("她是对方的"):
            kept.append(fact)
            continue
        if any(needle == fact or needle in fact or fact in needle for needle in needles):
            continue
        kept.append(fact)
    return kept


def _scrub(text: str) -> str:
    cleaned = " ".join((text or "").split()).strip()
    for marker in PRIVACY_MARKERS:
        cleaned = cleaned.replace(marker, "")
    return " ".join(cleaned.split()).strip()
