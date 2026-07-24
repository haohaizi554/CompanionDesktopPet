from __future__ import annotations

from collections import defaultdict
from typing import Sequence

from ..contract import CATEGORY_GROUP_BY_CATEGORY, PERSONA_CONTRACT
from ..models import CorpusLine
from ..normalization import normalize_text
from .common import IssueSink


SEMANTIC_RUNTIME_FIELDS = (
    "category",
    "category_group",
    "output_mode",
    "trigger",
    "required_context",
    "tone",
    "cooldown_hours",
    "semantic_cooldown_hours",
    "max_per_day",
    "interrupt_cost",
    "weight",
    "requires_reply",
    "enabled",
)


def validate_category_groups(rows: Sequence[CorpusLine], issues: IssueSink) -> None:
    for row_number, row in enumerate(rows, start=2):
        if not isinstance(row.category, str):
            continue
        expected = CATEGORY_GROUP_BY_CATEGORY.get(row.category)
        if expected is None:
            issues.error(
                "invalid_category",
                f"unknown category {row.category!r}",
                row.id,
                row_number,
            )
        elif row.category_group != expected:
            issues.error(
                "category_group_mismatch",
                f"category {row.category!r} must use category_group {expected!r}",
                row.id,
                row_number,
            )


def validate_category_group_output_modes(
    rows: Sequence[CorpusLine], issues: IssueSink
) -> None:
    mapping = PERSONA_CONTRACT.scheduler["category_group_output_modes"]
    for row_number, row in enumerate(rows, start=2):
        if not isinstance(row.category_group, str):
            continue
        expected = mapping.get(row.category_group)
        if isinstance(expected, str) and row.output_mode != expected:
            issues.error(
                "category_group_output_mode_mismatch",
                f"category_group {row.category_group!r} must use output_mode {expected!r}",
                row.id,
                row_number,
            )


def validate_semantic_groups(rows: Sequence[CorpusLine], issues: IssueSink) -> None:
    grouped: dict[str, list[tuple[int, CorpusLine]]] = defaultdict(list)
    for row_number, row in enumerate(rows, start=2):
        if isinstance(row.semantic_group, str) and row.semantic_group:
            grouped[row.semantic_group].append((row_number, row))
    for semantic_group, members in sorted(grouped.items()):
        first_number, first = min(members, key=lambda member: str(member[1].id))
        inconsistent = tuple(
            field
            for field in SEMANTIC_RUNTIME_FIELDS
            if any(getattr(row, field) != getattr(first, field) for _, row in members)
        )
        if inconsistent:
            issues.error(
                "semantic_group_inconsistent",
                f"semantic_group {semantic_group!r} differs in {', '.join(inconsistent)}",
                first.id,
                first_number,
            )


def validate_uniqueness(rows: Sequence[CorpusLine], issues: IssueSink) -> None:
    id_rows: dict[object, list[int]] = defaultdict(list)
    exact_rows: dict[str, list[str]] = defaultdict(list)
    normalized_rows: dict[str, list[str]] = defaultdict(list)
    for index, row in enumerate(rows, start=2):
        id_rows[row.id].append(index)
        if isinstance(row.text, str):
            normalized_rows[normalize_text(row.text)].append(str(row.id))
            if row.enabled is True:
                exact_rows[row.text].append(str(row.id))
    for value, positions in id_rows.items():
        if len(positions) > 1:
            issues.error(
                "duplicate_id",
                f"id {value!r} occurs on {len(positions)} rows",
                value,
                min(positions),
            )
    for text, ids in exact_rows.items():
        if len(ids) > 1:
            issues.error(
                "duplicate_text",
                f"enabled text occurs {len(ids)} times: {text!r}",
                min(ids),
            )
    for text, ids in normalized_rows.items():
        if len(ids) > 1:
            issues.error(
                "duplicate_normalized_text",
                f"normalized text occurs {len(ids)} times: {text!r}",
                min(ids),
            )


def validate_schema_contract(rows: Sequence[CorpusLine], issues: IssueSink) -> None:
    validate_category_groups(rows, issues)
    validate_category_group_output_modes(rows, issues)
    validate_semantic_groups(rows, issues)
    validate_uniqueness(rows, issues)


__all__ = [
    "SEMANTIC_RUNTIME_FIELDS",
    "validate_category_groups",
    "validate_category_group_output_modes",
    "validate_schema_contract",
    "validate_semantic_groups",
    "validate_uniqueness",
]
