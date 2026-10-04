"""人物关系单独落盘，不跟短记事、也不跟最近两百条聊天一起删。"""

from __future__ import annotations

import json
from pathlib import Path

_ROLES = {
    "她是对方的女朋友",
    "她是对方的老婆",
    "她是对方的伴侣",
    "她是对方的朋友",
}


class PersonaSetting:
    def __init__(self, path: Path | None) -> None:
        self.path = path
        self.relationship = ""
        self._load()

    def get(self) -> str:
        return self.relationship

    def set(self, relationship: str) -> None:
        if relationship not in _ROLES or relationship == self.relationship:
            return
        self.relationship = relationship
        self._write()

    def adopt(self, facts: list[str] | None) -> None:
        """旧存档里的关系搬进这份永久设定，只在还没写过时搬一次。"""
        if self.relationship:
            return
        for fact in reversed(facts or []):
            if fact in _ROLES:
                self.set(fact)
                return

    def _load(self) -> None:
        if self.path is None or not self.path.exists():
            return
        try:
            payload = json.loads(self.path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError):
            return
        relationship = payload.get("relationship") if isinstance(payload, dict) else ""
        if relationship in _ROLES:
            self.relationship = relationship

    def _write(self) -> None:
        if self.path is None:
            return
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".json.tmp")
        temporary.write_text(
            json.dumps({"relationship": self.relationship}, ensure_ascii=False),
            encoding="utf-8",
        )
        temporary.replace(self.path)
