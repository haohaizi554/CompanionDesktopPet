"""出口检查。过不了的句子不能进气泡，也不能进语音。"""

from __future__ import annotations

import re

from persona_dialogue.distill import PRIVACY_MARKERS

_THINKING = re.compile(r"<think>.*?</think>", re.DOTALL | re.IGNORECASE)
_LEAKS = (
    "通义",
    "千问",
    "阿里云",
    "语言模型",
    "人工智能",
    "作为一个AI",
    "作为一个 AI",
    "我是AI",
    "我是 AI",
    "系统提示",
    "提示词",
    "Here's a thinking",
    "thinking process",
)
_MARKDOWN = ("```", "**", "- ", "# ")


def without_repeated_prior(text: str, prior: str) -> str:
    """Drop sentences she already said in the previous reply."""
    return drop_known_sentences(text, [prior] if prior else [])


def drop_known_sentences(text: str, earlier: list[str]) -> str:
    """Remove sentences that already appeared in a recent reply.

    A copied answer is often two sentences. Comparing the whole previous
    reply to only the first new sentence lets that copy through.
    """
    cleaned = " ".join((text or "").split()).strip()
    known = {
        key
        for line in earlier
        for key in _sentence_keys(line)
        if len(key) >= 4
    }
    if not cleaned or not known:
        return cleaned

    kept: list[str] = []
    removed = False
    for part in _sentences(cleaned):
        key = _sentence_key(part)
        if len(key) >= 4 and key in known:
            removed = True
            continue
        kept.append(part.strip())
    return "".join(kept) if removed else cleaned


def repeats_earlier_reply(text: str, earlier: list[str]) -> bool:
    key = _sentence_key(text)
    if len(key) < 4:
        return False
    return any(_sentence_key(line) == key for line in earlier if line)


def asks_to_repeat(user_text: str) -> bool:
    text = user_text or ""
    return any(
        phrase in text
        for phrase in ("再说一遍", "再讲一遍", "重复一遍", "刚才说的", "你刚说", "没听清", "没听见")
    )


def _sentence_key(text: str) -> str:
    return " ".join((text or "").split()).strip().rstrip("。！？!?…").strip()


def _sentence_keys(text: str) -> list[str]:
    return [_sentence_key(part) for part in _sentences(text)]


def _sentences(text: str) -> list[str]:
    cleaned = " ".join((text or "").split()).strip()
    if not cleaned:
        return []
    parts: list[str] = []
    start = 0
    for index, char in enumerate(cleaned):
        if char in "。！？!?":
            piece = cleaned[start : index + 1].strip()
            if piece:
                parts.append(piece)
            start = index + 1
    tail = cleaned[start:].strip()
    if tail:
        parts.append(tail)
    return parts


def completion_text(message) -> str:
    """取出模型真正说出口的那句。

    三个点、空白，或只有英文思考过程时，不算结果。
    正文在 reasoning / reasoning_content 里时用那边。
    """
    content, reasoning = _completion_fields(message)
    if _has_cjk(content) and not _thinking_trace(content):
        return content
    if _has_cjk(reasoning):
        return reasoning
    if _substantive(content) and not _thinking_trace(content):
        return content
    if _substantive(reasoning):
        return reasoning
    return ""


def _completion_fields(message) -> tuple[str, str]:
    if isinstance(message, dict):
        content = message.get("content")
        reasoning = message.get("reasoning") or message.get("reasoning_content")
    else:
        content = getattr(message, "content", "")
        extra = getattr(message, "additional_kwargs", None) or {}
        reasoning = ""
        if isinstance(extra, dict):
            reasoning = extra.get("reasoning") or extra.get("reasoning_content") or ""
    return _flatten_content(content), _flatten_content(reasoning)


def _flatten_content(value) -> str:
    if value is None:
        return ""
    if isinstance(value, list):
        parts = []
        for part in value:
            if isinstance(part, dict):
                parts.append(str(part.get("text") or part.get("content") or ""))
            else:
                parts.append(str(part))
        value = "".join(parts)
    return " ".join(str(value).split()).strip()


def _has_cjk(text: str) -> bool:
    return any("\u4e00" <= char <= "\u9fff" for char in text or "")


def _substantive(text: str) -> bool:
    return bool((text or "").strip().strip(".。…· "))


def _thinking_trace(text: str) -> bool:
    lowered = (text or "").lower()
    return "here's a thinking" in lowered or "thinking process" in lowered


def clean_reply(text: str) -> str:
    value = _THINKING.sub("", text or "")
    value = value.replace("\r", " ").replace("\n", " ")
    return " ".join(value.split()).strip()


def audit(text: str, user_text: str, max_chars: int = 80) -> tuple[bool, str]:
    if not text:
        return False, "空话"
    if len(text) > max_chars:
        return False, "太长"
    lowered = text.lower()
    if any(leak.lower() in lowered for leak in _LEAKS):
        return False, "出戏"
    if any(marker in text for marker in _MARKDOWN):
        return False, "有排版"
    if any("\u4e00" <= char <= "\u9fff" for char in text) is False:
        return False, "不是中文"
    user = user_text or ""
    for marker in PRIVACY_MARKERS:
        if marker in text and marker not in user:
            return False, "主动报了不该报的称呼"
    return True, ""


def fit_reply(text: str, max_chars: int) -> str:
    """Keep a finished sentence inside the limit.

    A reply that only ran long does not need another model call.
    """
    cleaned = " ".join((text or "").split()).strip()
    if max_chars <= 0 or len(cleaned) <= max_chars:
        return cleaned
    window = cleaned[:max_chars]
    for marks in ("。！？!?…", "，、,"):
        cut = max(window.rfind(mark) for mark in marks)
        if cut >= 3:
            kept = window[: cut + 1].strip()
            if marks.startswith("，"):
                kept = kept.rstrip("，、, ").strip()
                if kept and kept[-1] not in "。！？!?…":
                    kept += "。"
            if len(kept) <= max_chars and kept:
                return kept
    return window.rstrip()
