from __future__ import annotations

from typing import Protocol


class IssueSink(Protocol):
    def error(
        self,
        code: str,
        message: str,
        line_id: object = "",
        row_number: int | None = None,
    ) -> None: ...
