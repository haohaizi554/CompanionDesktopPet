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
            base_url=str(payload["base_url"]).rstrip("/"),
            model=str(payload["model"]),
            api_key=str(payload["api_key"]),
            max_tokens=int(payload.get("max_tokens") or 180),
            temperature=float(payload.get("temperature") or 0.7),
            timeout_seconds=float(payload.get("timeout_seconds") or 45),
        )


def build_model(config: LlmConfig):
    return ChatOpenAI(
        model=config.model,
        base_url=config.base_url,
        api_key=config.api_key,
        temperature=config.temperature,
        max_tokens=config.max_tokens,
        timeout=config.timeout_seconds,
        max_retries=1,
        default_headers={"x-api-key": config.api_key},
        extra_body={"chat_template_kwargs": {"enable_thinking": False}},
    )
