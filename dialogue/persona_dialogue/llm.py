"""只负责文本补全。声音不从这里出去。"""

from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from langchain_openai import ChatOpenAI


@dataclass(frozen=True)
class LlmConfig:
    base_url: str
    model: str
    api_key: str
    auth: str = "both"
    max_tokens: int = 180
    temperature: float = 0.7
    timeout_seconds: float = 45.0

    @classmethod
    def load(cls, path: Path) -> "LlmConfig":
        payload = json.loads(path.read_text(encoding="utf-8"))
        missing = [key for key in ("base_url", "model", "api_key") if not str(payload.get(key) or "").strip()]
        if missing:
            raise ValueError("对话配置缺少 " + ", ".join(missing))
        return cls(
            base_url=normalize_base_url(str(payload["base_url"])),
            model=str(payload["model"]).strip(),
            api_key=str(payload["api_key"]).strip(),
            auth=normalize_auth(payload.get("auth")),
            max_tokens=int(payload.get("max_tokens") or 180),
            temperature=float(payload.get("temperature") or 0.7),
            timeout_seconds=float(payload.get("timeout_seconds") or 45),
        )


def normalize_base_url(raw: str) -> str:
    text = raw.strip().rstrip("/")
    if "://" not in text:
        text = "http://" + text
    if text.lower().endswith("/chat/completions"):
        text = text[: -len("/chat/completions")].rstrip("/")
    if not text.lower().endswith("/v1"):
        text += "/v1"
    return text


def normalize_auth(raw: object) -> str:
    value = str(raw or "").strip().lower()
    if value in {"bearer", "x-api-key", "both"}:
        return value
    return "both"


def build_model(config: LlmConfig):
    headers = {"x-api-key": config.api_key} if config.auth in {"x-api-key", "both"} else None
    return ChatOpenAI(
        model=config.model,
        base_url=config.base_url,
        api_key=config.api_key,
        temperature=config.temperature,
        max_tokens=config.max_tokens,
        timeout=config.timeout_seconds,
        max_retries=1,
        default_headers=headers,
        extra_body={"chat_template_kwargs": {"enable_thinking": False}},
    )
