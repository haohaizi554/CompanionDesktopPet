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
