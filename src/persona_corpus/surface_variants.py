from __future__ import annotations

import hashlib
import re
from collections import Counter
from dataclasses import dataclass
from types import MappingProxyType
from typing import Mapping, Sequence

from .contract import PersonaContractError, category_group_for
from .models import CorpusLine
from .normalization import normalize_text
from .schema import ArchiveRow
from .validation import (
    DIRECT_STATE_PATTERNS,
    TECHNICAL_CURRENT_PATTERNS,
    _looks_like_pii,
)


MAX_SURFACE_TEXT_LENGTH = 36
QUESTION_MARKS = ("?", "？")
OVERLY_COMMANDING_PREFIXES = ("别", "赶紧", "必须", "给我")

# These phrases request or strongly invite a response even without a question mark.
# They stay named and audited instead of being hidden in a target row count.
IMPLICIT_QUESTION_MARKERS = ("是不是", "好不好")
REPLY_HOOK_MARKERS = (
    "难受就说",
    "拿来跟我显摆",
    "说给我听",
    "讲给我听",
)

RECOVERABLE_CARTESIAN_REASON = "cartesian_duplicate"
RECOVERABLE_EASTER_REASON = "low_information"
LEGACY_SURFACE_SOURCE_KIND = "legacy_surface_variant"


@dataclass(frozen=True, slots=True)
class LegacySurfaceCandidate:
    id: str
    source_line: int
    category: str
    topic_id: str
    text: str
    normalized_text: str
    source_kind: str
    source_reference: str
    archive_reason: str


@dataclass(frozen=True, slots=True)
class LegacySurfacePreparation:
    candidates: tuple[LegacySurfaceCandidate, ...]
    cartesian_count: int
    easter_egg_count: int
    rejection_counts: Mapping[str, int]
    safety_marker_counts: Mapping[str, int]


def _slug(value: str) -> str:
    return re.sub(r"[^a-z0-9]+", "_", value.casefold()).strip("_") or "topic"


def _identity_digest(source_line: int, topic_id: str, normalized_text: str) -> str:
    identity = f"{source_line}\0{topic_id}\0{normalized_text}"
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()[:12]


def legacy_surface_line_id(source_line: int, topic_id: str, text: str) -> str:
    """Return a stable identity anchored to source, topic and normalized text."""
    normalized = normalize_text(text)
    digest = _identity_digest(source_line, topic_id, normalized)
    return f"v2_surface_{source_line}_{_slug(topic_id)}_{digest}"


def _marker_hits(text: str, category: str) -> tuple[str, ...]:
    hits: list[str] = []
    if any(marker in text for marker in IMPLICIT_QUESTION_MARKERS):
        hits.append("implicit_question")
    if any(marker in text for marker in REPLY_HOOK_MARKERS):
        hits.append("reply_hook")
    if any(marker in text for marker in DIRECT_STATE_PATTERNS):
        hits.append("direct_state")
    try:
        category_group = category_group_for(category)
    except PersonaContractError:
        category_group = ""
    if category_group == "technical" and any(
        marker.casefold() in text.casefold()
        for marker in TECHNICAL_CURRENT_PATTERNS
    ):
        hits.append("technical_current_object")
    return tuple(hits)


def _allowed_archive_source(row: ArchiveRow) -> bool:
    return row.archive_reason == RECOVERABLE_CARTESIAN_REASON or (
        row.archive_reason == RECOVERABLE_EASTER_REASON
        and row.category == "EasterEgg"
    )


def _variant_token(row: ArchiveRow, normalized_text: str) -> str:
    digest = _identity_digest(row.source_line, row.topic_id, normalized_text)
    return f"surface_{row.source_line}_{digest}"


def prepare_legacy_surface_candidates(
    archive_rows: Sequence[ArchiveRow],
    existing_rows: Sequence[CorpusLine],
    *,
    max_text_length: int = MAX_SURFACE_TEXT_LENGTH,
) -> LegacySurfacePreparation:
    """Audit and prepare deterministic safe legacy surface variants.

    The preparation is intentionally separate from scheduling metadata.  A later
    materialization stage can attach one canonical scene policy per topic without
    letting the number of surface variants affect scene selection weight.
    """
    if isinstance(max_text_length, bool) or not isinstance(max_text_length, int):
        raise TypeError("max_text_length must be an integer")
    if max_text_length <= 0:
        raise ValueError("max_text_length must be positive")

    existing_normalized = {normalize_text(row.text) for row in existing_rows}
    seen_normalized = set(existing_normalized)
    rejections: Counter[str] = Counter()
    marker_counts: Counter[str] = Counter()
    candidates: list[LegacySurfaceCandidate] = []

    for row in sorted(archive_rows, key=lambda item: item.source_line):
        if not _allowed_archive_source(row):
            rejections["archive_reason"] += 1
            continue
        text = row.original_text
        if (
            row.archive_reason == RECOVERABLE_CARTESIAN_REASON
            and len(text) > max_text_length
        ):
            rejections["too_long"] += 1
            continue

        hits = _marker_hits(text, row.category)
        marker_counts.update(hits)
        normalized = normalize_text(text)
        if (
            row.category.casefold() == "proactivechat"
            or any(mark in text for mark in QUESTION_MARKS)
        ):
            rejections["question_or_reply"] += 1
            continue
        if _looks_like_pii(text):
            rejections["pii"] += 1
            continue
        if "implicit_question" in hits:
            rejections["implicit_question"] += 1
            continue
        if "reply_hook" in hits:
            rejections["reply_hook"] += 1
            continue
        if "direct_state" in hits or "technical_current_object" in hits:
            rejections["fake_context"] += 1
            continue
        if text.startswith(OVERLY_COMMANDING_PREFIXES):
            rejections["overly_commanding"] += 1
            continue
        if any(character in text for character in ("\t", "\r", "\n", "\u2028", "\u2029")):
            rejections["control_character"] += 1
            continue
        if not normalized:
            rejections["normalized_empty"] += 1
            continue
        if normalized in seen_normalized:
            reason = (
                "existing_text"
                if normalized in existing_normalized
                else "normalized_duplicate"
            )
            rejections[reason] += 1
            continue

        seen_normalized.add(normalized)
        token = _variant_token(row, normalized)
        candidates.append(
            LegacySurfaceCandidate(
                id=legacy_surface_line_id(row.source_line, row.topic_id, text),
                source_line=row.source_line,
                category=row.category,
                topic_id=row.topic_id,
                text=text,
                normalized_text=normalized,
                source_kind=LEGACY_SURFACE_SOURCE_KIND,
                source_reference=(
                    f"legacy:{row.source_line};topic:{row.topic_id};variant:{token}"
                ),
                archive_reason=row.archive_reason,
            )
        )

    cartesian_count = sum(
        row.archive_reason == RECOVERABLE_CARTESIAN_REASON for row in candidates
    )
    easter_egg_count = sum(
        row.archive_reason == RECOVERABLE_EASTER_REASON for row in candidates
    )
    return LegacySurfacePreparation(
        candidates=tuple(candidates),
        cartesian_count=cartesian_count,
        easter_egg_count=easter_egg_count,
        rejection_counts=MappingProxyType(dict(sorted(rejections.items()))),
        safety_marker_counts=MappingProxyType(dict(sorted(marker_counts.items()))),
    )


__all__ = [
    "IMPLICIT_QUESTION_MARKERS",
    "LEGACY_SURFACE_SOURCE_KIND",
    "LegacySurfaceCandidate",
    "LegacySurfacePreparation",
    "MAX_SURFACE_TEXT_LENGTH",
    "REPLY_HOOK_MARKERS",
    "legacy_surface_line_id",
    "prepare_legacy_surface_candidates",
]
