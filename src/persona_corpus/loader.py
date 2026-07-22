from __future__ import annotations

import csv
import hashlib
import math
from pathlib import Path

from .models import CorpusLine, LegacyLine


V2_HEADER = (
    "id",
    "category",
    "category_group",
    "topic_id",
    "semantic_group",
    "output_mode",
    "trigger",
    "required_context",
    "tone",
    "interrupt_cost",
    "cooldown_hours",
    "semantic_cooldown_hours",
    "max_per_day",
    "weight",
    "requires_reply",
    "enabled",
    "text",
    "source_kind",
    "source_reference",
    "rewrite_reason",
)


class CorpusFormatError(ValueError):
    """A corpus row does not satisfy its strict TSV contract."""

    def __init__(self, path: Path, line_number: int, detail: str) -> None:
        self.path = path
        self.line_number = line_number
        self.detail = detail
        super().__init__(f"{path}: line {line_number}: {detail}")


def _rows(path: Path):
    try:
        stream = path.open("r", encoding="utf-8-sig", newline="")
    except (OSError, UnicodeError) as error:
        raise CorpusFormatError(path, 1, str(error)) from error
    with stream:
        reader = csv.reader(stream, delimiter="\t", strict=True)
        try:
            yield from reader
        except (csv.Error, UnicodeError) as error:
            raise CorpusFormatError(path, max(1, reader.line_num), str(error)) from error


def load_legacy(path: Path) -> list[LegacyLine]:
    path = Path(path)
    result: list[LegacyLine] = []
    for line_number, row in enumerate(_rows(path), start=1):
        if len(row) != 2:
            raise CorpusFormatError(
                path, line_number, f"expected 2 columns, found {len(row)}"
            )
        category, text = row
        if not category.strip():
            raise CorpusFormatError(path, line_number, "category must not be empty")
        if not text.strip():
            raise CorpusFormatError(path, line_number, "text must not be empty")
        result.append(LegacyLine(line_number, category, text))
    return result


def _boolean(path: Path, line_number: int, column: str, value: str) -> bool:
    if value == "true":
        return True
    if value == "false":
        return False
    raise CorpusFormatError(
        path, line_number, f"{column} must be 'true' or 'false', found {value!r}"
    )


def _integer(path: Path, line_number: int, column: str, value: str) -> int:
    try:
        return int(value)
    except ValueError as error:
        raise CorpusFormatError(
            path, line_number, f"{column} must be an integer, found {value!r}"
        ) from error


def _number(path: Path, line_number: int, column: str, value: str) -> float:
    try:
        parsed = float(value)
    except ValueError as error:
        raise CorpusFormatError(
            path, line_number, f"{column} must be a number, found {value!r}"
        ) from error
    if not math.isfinite(parsed):
        raise CorpusFormatError(path, line_number, f"{column} must be finite")
    return parsed


def load_v2(path: Path, enabled_only: bool = False) -> list[CorpusLine]:
    path = Path(path)
    rows = iter(_rows(path))
    try:
        header = next(rows)
    except StopIteration as error:
        raise CorpusFormatError(path, 1, "missing v2 header") from error
    if tuple(header) != V2_HEADER:
        raise CorpusFormatError(
            path, 1, "expected exact v2 header: " + ",".join(V2_HEADER)
        )

    result: list[CorpusLine] = []
    for line_number, row in enumerate(rows, start=2):
        if len(row) != len(V2_HEADER):
            raise CorpusFormatError(
                path,
                line_number,
                f"expected {len(V2_HEADER)} columns, found {len(row)}",
            )
        values = dict(zip(V2_HEADER, row, strict=True))
        required = (
            "id",
            "category",
            "category_group",
            "topic_id",
            "semantic_group",
            "output_mode",
            "trigger",
            "tone",
            "text",
            "source_kind",
            "source_reference",
        )
        empty = next((name for name in required if not values[name].strip()), None)
        if empty is not None:
            raise CorpusFormatError(path, line_number, f"{empty} must not be empty")
        corpus_line = CorpusLine(
            id=values["id"],
            category=values["category"],
            category_group=values["category_group"],
            topic_id=values["topic_id"],
            semantic_group=values["semantic_group"],
            output_mode=values["output_mode"],
            trigger=values["trigger"],
            required_context=values["required_context"],
            tone=values["tone"],
            interrupt_cost=_integer(
                path, line_number, "interrupt_cost", values["interrupt_cost"]
            ),
            cooldown_hours=_number(
                path, line_number, "cooldown_hours", values["cooldown_hours"]
            ),
            semantic_cooldown_hours=_number(
                path,
                line_number,
                "semantic_cooldown_hours",
                values["semantic_cooldown_hours"],
            ),
            max_per_day=_integer(
                path, line_number, "max_per_day", values["max_per_day"]
            ),
            weight=_number(path, line_number, "weight", values["weight"]),
            requires_reply=_boolean(
                path, line_number, "requires_reply", values["requires_reply"]
            ),
            enabled=_boolean(path, line_number, "enabled", values["enabled"]),
            text=values["text"],
            source_kind=values["source_kind"],
            source_reference=values["source_reference"],
            rewrite_reason=values["rewrite_reason"],
        )
        if not enabled_only or corpus_line.enabled:
            result.append(corpus_line)
    return result


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()
