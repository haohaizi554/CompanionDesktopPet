"""Shared report types, issue collection, and strict JSON/hash helpers."""

from __future__ import annotations

import hashlib
import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Any

from ..normalization import normalize_text


@dataclass(frozen=True, slots=True)
class ValidationIssue:
    code: str
    message: str
    line_id: str = ""
    row_number: int | None = None


@dataclass(frozen=True, slots=True)
class ValidationReport:
    errors: tuple[ValidationIssue, ...]
    warnings: tuple[ValidationIssue, ...]

    @property
    def hard_error_count(self) -> int:
        return len(self.errors)


class ValidationInputError(ValueError):
    """An input file cannot be interpreted under the strict validation contract."""


class _Issues:
    def __init__(self) -> None:
        self.errors: list[ValidationIssue] = []
        self.warnings: list[ValidationIssue] = []

    def error(
        self,
        code: str,
        message: str,
        line_id: object = "",
        row_number: int | None = None,
    ) -> None:
        self.errors.append(
            ValidationIssue(code, message, str(line_id) if line_id is not None else "", row_number)
        )

    def warning(
        self,
        code: str,
        message: str,
        line_id: object = "",
        row_number: int | None = None,
    ) -> None:
        self.warnings.append(
            ValidationIssue(code, message, str(line_id) if line_id is not None else "", row_number)
        )

    @staticmethod
    def _key(issue: ValidationIssue) -> tuple[str, str, int, str]:
        return (
            issue.code,
            issue.line_id,
            issue.row_number if issue.row_number is not None else -1,
            issue.message,
        )

    def report(self) -> ValidationReport:
        return ValidationReport(
            tuple(sorted(self.errors, key=self._key)),
            tuple(sorted(self.warnings, key=self._key)),
        )


def _is_finite_number(value: object) -> bool:
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(float(value))
    )


def _is_integer(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def normalized_text_sha256(text: str) -> str:
    return hashlib.sha256(normalize_text(text).encode("utf-8")).hexdigest()


def scheduler_config_sha256(config: object) -> str:
    """Hash the scheduler's semantic JSON value, independent of formatting."""

    payload = json.dumps(
        config,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def _json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number {value!r}")


def load_json_object(path: Path) -> dict[str, Any]:
    path = Path(path)
    try:
        payload = path.read_text(encoding="utf-8-sig")
        value = json.loads(
            payload,
            object_pairs_hook=_json_pairs,
            parse_constant=_reject_json_constant,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        raise ValidationInputError(f"{path}: invalid JSON: {error}") from error
    if not isinstance(value, dict):
        raise ValidationInputError(f"{path}: JSON root must be an object")
    return value
