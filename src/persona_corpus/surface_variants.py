from __future__ import annotations

import hashlib
import re
from collections import Counter
from dataclasses import dataclass, replace
from types import MappingProxyType
from typing import Mapping, Sequence

from .contract import PERSONA_CONTRACT, PersonaContractError, category_group_for
from .editorial import EDITORIAL_MANIFEST
from .models import CorpusLine
from .normalization import normalize_text
from .schema import ArchiveRow
from .surface_safety import (
    IMPLICIT_QUESTION_MARKERS,
    MAX_SURFACE_TEXT_LENGTH,
    REPLY_HOOK_MARKERS,
    UNAVAILABLE_STATE_MARKERS,
)
from .validation import (
    DIRECT_STATE_PATTERNS,
    TECHNICAL_CURRENT_PATTERNS,
    _has_identity_marker,
    _looks_like_non_identity_pii,
)


QUESTION_MARKS = ("?", "？")
OVERLY_COMMANDING_PREFIXES = ("别", "赶紧", "必须", "给我")

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


@dataclass(frozen=True, slots=True)
class _ScenePolicy:
    category_group: str
    semantic_group: str
    output_mode: str
    trigger: str
    required_context: str
    tone: str
    interrupt_cost: int
    cooldown_hours: float
    semantic_cooldown_hours: float
    max_per_day: int
    weight: float


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
    if any(marker in text for marker in UNAVAILABLE_STATE_MARKERS):
        hits.append("unavailable_state")
    return tuple(hits)


def _allowed_archive_source(row: ArchiveRow) -> bool:
    return row.archive_reason == RECOVERABLE_CARTESIAN_REASON or (
        row.archive_reason == RECOVERABLE_EASTER_REASON
        and row.category == "EasterEgg"
    )


def _variant_token(row: ArchiveRow, normalized_text: str) -> str:
    return legacy_surface_variant_token(
        row.source_line,
        row.topic_id,
        normalized_text,
        already_normalized=True,
    )


def legacy_surface_variant_token(
    source_line: int,
    topic_id: str,
    text: str,
    *,
    already_normalized: bool = False,
) -> str:
    normalized = text if already_normalized else normalize_text(text)
    digest = _identity_digest(source_line, topic_id, normalized)
    return f"surface_{source_line}_{digest}"


def _is_exact_identity_candidate(
    row: ArchiveRow,
    *,
    line_id: str,
    source_reference: str,
) -> bool:
    item = EDITORIAL_MANIFEST.identity_easter_eggs.get(line_id)
    if item is None:
        return False
    digest = hashlib.sha256(row.original_text.encode("utf-8")).hexdigest()
    marker_hits = {
        marker
        for marker in EDITORIAL_MANIFEST.allowed_identity_markers
        if marker in row.original_text
    }
    return (
        item.source_line == row.source_line
        and item.category == row.category
        and item.category_group == "easter_egg"
        and item.source_reference == source_reference
        and item.text_sha256 == digest
        and marker_hits == set(item.allowed_markers)
        and not any(
            marker in row.original_text
            for marker in EDITORIAL_MANIFEST.forbidden_identity_markers
        )
    )


def _policy_from_row(row: CorpusLine) -> _ScenePolicy:
    return _ScenePolicy(
        category_group=row.category_group,
        semantic_group=row.semantic_group,
        output_mode=row.output_mode,
        trigger=row.trigger,
        required_context=row.required_context,
        tone=row.tone,
        interrupt_cost=row.interrupt_cost,
        cooldown_hours=float(row.cooldown_hours),
        semantic_cooldown_hours=float(row.semantic_cooldown_hours),
        max_per_day=row.max_per_day,
        weight=float(row.weight),
    )


def _default_policy(candidate: LegacySurfaceCandidate) -> _ScenePolicy:
    category_group = category_group_for(candidate.category)
    if category_group in {"technical", "growth", "career"}:
        output_mode = "self_talk"
        tone = "dry"
        interrupt_cost = 1
        cooldown = 120.0
        weight = 1.0
    elif category_group == "daily_care":
        output_mode = "ambient"
        tone = "gentle"
        interrupt_cost = 0
        cooldown = 144.0
        weight = 1.0
    elif category_group == "emotional_reflection":
        output_mode = "self_talk"
        tone = "gentle"
        interrupt_cost = 0
        cooldown = 144.0
        weight = 1.0
    elif category_group == "character_life":
        output_mode = "self_talk"
        tone = "nostalgic" if candidate.category == "WanderingLife" else "playful"
        interrupt_cost = 0
        cooldown = 144.0
        weight = 1.0
    elif category_group == "easter_egg":
        output_mode = "self_talk"
        tone = "playful"
        interrupt_cost = 0
        cooldown = 720.0
        weight = 0.1
    else:
        raise ValueError(
            f"legacy surface candidate cannot synthesize {category_group!r} metadata"
        )
    return _ScenePolicy(
        category_group=category_group,
        semantic_group=(
            f"legacy_surface.{_slug(candidate.category)}.{_slug(candidate.topic_id)}"
        ),
        output_mode=output_mode,
        trigger="any",
        required_context="none",
        tone=tone,
        interrupt_cost=interrupt_cost,
        cooldown_hours=cooldown,
        semantic_cooldown_hours=cooldown,
        max_per_day=1,
        weight=weight,
    )


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
        line_id = legacy_surface_line_id(row.source_line, row.topic_id, text)
        token = _variant_token(row, normalized)
        source_reference = (
            f"legacy:{row.source_line};topic:{row.topic_id};variant:{token}"
        )
        if (
            row.category.casefold() == "proactivechat"
            or any(mark in text for mark in QUESTION_MARKS)
        ):
            rejections["question_or_reply"] += 1
            continue
        if _looks_like_non_identity_pii(text) or (
            _has_identity_marker(text)
            and not _is_exact_identity_candidate(
                row,
                line_id=line_id,
                source_reference=source_reference,
            )
        ):
            rejections["pii"] += 1
            continue
        if "implicit_question" in hits:
            rejections["implicit_question"] += 1
            continue
        if "reply_hook" in hits:
            rejections["reply_hook"] += 1
            continue
        if "unavailable_state" in hits:
            rejections["unavailable_state"] += 1
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
        candidates.append(
            LegacySurfaceCandidate(
                id=line_id,
                source_line=row.source_line,
                category=row.category,
                topic_id=row.topic_id,
                text=text,
                normalized_text=normalized,
                source_kind=LEGACY_SURFACE_SOURCE_KIND,
                source_reference=source_reference,
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


def materialize_legacy_surface_candidates(
    candidates: Sequence[LegacySurfaceCandidate],
    existing_rows: Sequence[CorpusLine],
) -> tuple[CorpusLine, ...]:
    """Attach exactly one canonical scheduling policy to every legacy topic."""
    existing_policies: dict[tuple[str, str], _ScenePolicy] = {}
    for row in existing_rows:
        key = (row.category, row.topic_id)
        policy = _policy_from_row(row)
        previous = existing_policies.setdefault(key, policy)
        if previous != policy:
            raise ValueError(
                f"existing topic {key!r} has inconsistent scheduling metadata"
            )

    synthesized: dict[tuple[str, str], _ScenePolicy] = {}
    materialized: list[CorpusLine] = []
    for candidate in sorted(candidates, key=lambda row: row.id):
        key = (candidate.category, candidate.topic_id)
        policy = existing_policies.get(key)
        if policy is None:
            policy = synthesized.setdefault(key, _default_policy(candidate))
        rewrite_reason = (
            "preserved safe independent legacy EasterEgg after runtime safety audit"
            if candidate.archive_reason == RECOVERABLE_EASTER_REASON
            else "restored audited legacy surface variant under a semantic scene"
        )
        materialized.append(
            CorpusLine(
                id=candidate.id,
                category=candidate.category,
                category_group=policy.category_group,
                topic_id=candidate.topic_id,
                semantic_group=policy.semantic_group,
                output_mode=policy.output_mode,
                trigger=policy.trigger,
                required_context=policy.required_context,
                tone=policy.tone,
                interrupt_cost=policy.interrupt_cost,
                cooldown_hours=policy.cooldown_hours,
                semantic_cooldown_hours=policy.semantic_cooldown_hours,
                max_per_day=policy.max_per_day,
                weight=policy.weight,
                requires_reply=False,
                enabled=True,
                relationship_profile="neutral",
                text=candidate.text,
                source_kind=LEGACY_SURFACE_SOURCE_KIND,
                source_reference=candidate.source_reference,
                rewrite_reason=rewrite_reason,
            )
        )
    return tuple(materialized)


def _stable_scene_digest(semantic_group: str) -> str:
    namespace = str(PERSONA_CONTRACT.dry_sharp["scene_hash_namespace"])
    identity = f"{namespace}:{semantic_group}"
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()


def apply_dry_sharp_scene_dose(rows: Sequence[CorpusLine]) -> tuple[CorpusLine, ...]:
    """Apply the shared dry-sharp inventory dose to whole semantic scenes."""
    policy = PERSONA_CONTRACT.dry_sharp
    try:
        if policy["scene_assignment_field"] != "semantic_group":
            raise ValueError("unsupported dry_sharp scene assignment field")
        threshold = float(policy["scene_hash_threshold"])
        minimum, maximum = (
            float(value) for value in policy["scene_inventory_acceptance"]
        )
        enforcement_profile = str(policy["scene_inventory_enforcement_profile"])
        enforcement_minimum = int(PERSONA_CONTRACT.inventory[enforcement_profile][0])
        forbidden_groups = frozenset(str(value) for value in policy["forbidden_category_groups"])
        forbidden_triggers = frozenset(str(value) for value in policy["forbidden_triggers"])
        forbidden_context = frozenset(
            str(value) for value in policy["forbidden_context_tokens"]
        )
    except (KeyError, TypeError, ValueError) as error:
        raise ValueError("shared dry_sharp inventory policy is malformed") from error

    if len(rows) < enforcement_minimum:
        return tuple(sorted(rows, key=lambda item: item.id))

    by_scene: dict[str, list[CorpusLine]] = {}
    for row in rows:
        by_scene.setdefault(row.semantic_group, []).append(row)
    for semantic_group, variants in by_scene.items():
        tones = {row.tone for row in variants}
        if len(tones) != 1:
            raise ValueError(
                f"semantic group {semantic_group!r} has inconsistent tone metadata"
            )

    selected: set[str] = {
        semantic_group
        for semantic_group, variants in by_scene.items()
        if variants[0].tone == "dry_sharp"
    }
    for semantic_group, variants in by_scene.items():
        first = variants[0]
        contexts = frozenset(first.required_context.split(","))
        if (
            first.tone in {"dry", "dry_sharp"}
            and first.category_group not in forbidden_groups
            and first.trigger not in forbidden_triggers
            and contexts.isdisjoint(forbidden_context)
        ):
            digest_fraction = int(_stable_scene_digest(semantic_group), 16) / (1 << 256)
            if digest_fraction < threshold:
                selected.add(semantic_group)

    if by_scene:
        actual = len(selected) / len(by_scene)
        if not minimum <= actual <= maximum:
            raise ValueError(
                f"dry_sharp semantic-scene dose {actual:.6f} is outside "
                f"[{minimum:.6f}, {maximum:.6f}]"
            )
    return tuple(
        replace(row, tone="dry_sharp") if row.semantic_group in selected else row
        for row in sorted(rows, key=lambda item: item.id)
    )


__all__ = [
    "IMPLICIT_QUESTION_MARKERS",
    "LEGACY_SURFACE_SOURCE_KIND",
    "LegacySurfaceCandidate",
    "LegacySurfacePreparation",
    "MAX_SURFACE_TEXT_LENGTH",
    "REPLY_HOOK_MARKERS",
    "UNAVAILABLE_STATE_MARKERS",
    "apply_dry_sharp_scene_dose",
    "legacy_surface_line_id",
    "legacy_surface_variant_token",
    "materialize_legacy_surface_candidates",
    "prepare_legacy_surface_candidates",
]
