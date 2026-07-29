"""Per-row content, context, and safety validation rules."""

from __future__ import annotations

import re
from typing import Mapping

from ..contract import (
    ALLOWED_CONTEXT_TOKENS,
    CATEGORY_GROUPS,
    OUTPUT_MODES,
    PERSONA_CONTRACT,
    RELATIONSHIP_PROFILES,
    SOURCE_KINDS,
    TONES,
    TRIGGERS,
)
from ..editorial import is_exact_identity_easter_egg
from ..models import CorpusLine
from ..normalization import normalize_text
from ..privacy import (
    COMMON_CHINESE_GIVEN_NAMES,
    COMMON_CHINESE_SURNAMES,
    CONTEXTUAL_CHINESE_NAME_PATTERN,
    ENABLED_CONTENT_POLICY,
    LABELED_CHINESE_NAME_PATTERN,
    NAME_CONTEXT_MARKERS,
    PII_MARKERS,
    PII_PATTERNS,
    pii_kinds,
)
from ..surface_safety import (
    TECHNICAL_DEICTIC_OBJECT_MARKERS,
    TECHNICAL_USER_ENVIRONMENT_MARKERS,
)
from .config_rules import CONTEXT_TOKEN_PATTERN
from .core import _Issues, _is_finite_number, _is_integer
from .safety_rules import validate_safety_preflight


ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")

DIRECT_STATE_PATTERNS = (
    "你今天",
    "你现在",
    "你看起来",
    "我知道你",
    "你已经",
    "你又",
    "是不是又",
    "你的杯子",
    "你没休息",
    "你没有休息",
    "你很累",
    "你难过",
    "你焦虑",
)
TECHNICAL_CURRENT_PATTERNS = (
    "这个 bug",
    "这个bug",
    "这个空指针",
    "这个事务",
    "这条 SQL",
    "这条SQL",
    "这次死锁",
    "这个请求",
) + TECHNICAL_DEICTIC_OBJECT_MARKERS + TECHNICAL_USER_ENVIRONMENT_MARKERS
STRONG_EMOTION_MARKERS = (
    "永远陪",
    "绝对不会离开",
    "离不开你",
    "只有我懂你",
    "绝望",
)

def _required_context_tokens(value: object) -> tuple[str, ...] | None:
    if not isinstance(value, str) or not value:
        return None
    tokens = tuple(value.split(","))
    if (
        any(not token or token.strip() != token for token in tokens)
        or len(tokens) != len(set(tokens))
        or any(CONTEXT_TOKEN_PATTERN.fullmatch(token) is None for token in tokens)
        or any(token not in ALLOWED_CONTEXT_TOKENS for token in tokens)
        or ("none" in tokens and tokens != ("none",))
    ):
        return None
    return tokens


def _has_identity_marker(text: str) -> bool:
    return "known_identity" in pii_kinds(text, ENABLED_CONTENT_POLICY)


def _looks_like_non_identity_pii(text: str) -> bool:
    return bool(pii_kinds(text, ENABLED_CONTENT_POLICY) - {"known_identity"})


_AUTHORED_SOURCE_REFERENCE_PATTERN = re.compile(
    r"^catalog:authored-v1:(b\d{3});variant:[A-Za-z0-9][A-Za-z0-9._-]*$"
)


def _is_adjudicated_identity(row: CorpusLine) -> bool:
    if is_exact_identity_easter_egg(row):
        return True
    if (
        row.source_kind != "curated_authored"
        or not isinstance(row.source_reference, str)
        or not isinstance(row.text, str)
    ):
        return False
    match = _AUTHORED_SOURCE_REFERENCE_PATTERN.fullmatch(row.source_reference)
    if match is None:
        return False
    policy = PERSONA_CONTRACT.authored_identity
    batch_id = match.group(1)
    if batch_id not in frozenset(policy["easter_egg_batches"]):
        return False
    if (
        row.category != policy["category"]
        or row.category_group != policy["category_group"]
        or row.output_mode != policy["output_mode"]
        or row.relationship_profile not in frozenset(policy["allowed_relationship_profiles"])
    ):
        return False
    hits = tuple(marker for marker in PII_MARKERS if marker in row.text)
    if not hits:
        return False
    assigned = policy["direct_marker_batches"].get(batch_id)
    if assigned is not None:
        return hits == (assigned,) and row.text.count(assigned) == 1
    return all(marker in PII_MARKERS for marker in hits)

def _trigger_context_conflict(trigger: object, tokens: tuple[str, ...] | None) -> bool:
    if not isinstance(trigger, str) or tokens is None:
        return False
    trigger_by_time_token = PERSONA_CONTRACT.temporal["context_token_trigger"]
    if not isinstance(trigger_by_time_token, Mapping):
        raise RuntimeError("persona temporal contract is malformed")
    allowed_time_values = frozenset(
        token.split(":", 1)[1]
        for token, mapped_trigger in trigger_by_time_token.items()
        if isinstance(token, str)
        and token.startswith("time:")
        and mapped_trigger == trigger
    )
    expected = {
        "morning": ("time", allowed_time_values),
        "noon": ("time", allowed_time_values),
        "afternoon": ("time", allowed_time_values),
        "evening": ("time", allowed_time_values),
        "late_night": ("time", allowed_time_values),
        "weekday": ("day", frozenset({"weekday"})),
        "weekend": ("day", frozenset({"weekend"})),
    }.get(trigger)
    if expected is None:
        return False
    dimension, values = expected
    dimension_tokens = [token for token in tokens if token.startswith(f"{dimension}:")]
    allowed_tokens = {f"{dimension}:{value}" for value in values}
    return bool(dimension_tokens) and not any(
        token in allowed_tokens for token in dimension_tokens
    )


def _validate_line(row: CorpusLine, row_number: int, issues: _Issues) -> None:
    line_id = row.id
    required_strings = (
        "id",
        "category",
        "category_group",
        "topic_id",
        "semantic_group",
        "output_mode",
        "trigger",
        "required_context",
        "tone",
        "relationship_profile",
        "text",
        "source_kind",
        "source_reference",
        "rewrite_reason",
    )
    for field in required_strings:
        value = getattr(row, field)
        if not isinstance(value, str) or not value.strip():
            issues.error("required_field", f"{field} must be a non-empty string", line_id, row_number)
    if isinstance(row.id, str) and row.id and ID_PATTERN.fullmatch(row.id) is None:
        issues.error("invalid_id", "id must use only stable ASCII identifier characters", line_id, row_number)
    if not isinstance(row.category_group, str) or row.category_group not in CATEGORY_GROUPS:
        issues.error("invalid_category_group", f"unknown category_group {row.category_group!r}", line_id, row_number)
    if not isinstance(row.output_mode, str) or row.output_mode not in OUTPUT_MODES:
        issues.error("invalid_output_mode", f"unknown output_mode {row.output_mode!r}", line_id, row_number)
    if not isinstance(row.trigger, str) or row.trigger not in TRIGGERS:
        issues.error("invalid_trigger", f"unknown trigger {row.trigger!r}", line_id, row_number)
    if not isinstance(row.tone, str) or row.tone not in TONES:
        issues.error("invalid_tone", f"unknown tone {row.tone!r}", line_id, row_number)
    if (
        not isinstance(row.relationship_profile, str)
        or row.relationship_profile not in RELATIONSHIP_PROFILES
    ):
        issues.error(
            "invalid_relationship_profile",
            f"unknown relationship_profile {row.relationship_profile!r}", line_id, row_number,
        )
    if not isinstance(row.source_kind, str) or row.source_kind not in SOURCE_KINDS:
        issues.error("invalid_source_kind", f"unknown source_kind {row.source_kind!r}", line_id, row_number)
    if not _is_integer(row.interrupt_cost) or not 0 <= row.interrupt_cost <= 5:
        issues.error("invalid_interrupt_cost", "interrupt_cost must be an integer in [0, 5]", line_id, row_number)
    if not _is_finite_number(row.cooldown_hours) or row.cooldown_hours < 1:
        issues.error("invalid_cooldown", "cooldown_hours must be finite and >= 1", line_id, row_number)
    if not _is_finite_number(row.semantic_cooldown_hours) or row.semantic_cooldown_hours < 1:
        issues.error(
            "invalid_semantic_cooldown",
            "semantic_cooldown_hours must be finite and >= 1",
            line_id,
            row_number,
        )
    elif _is_finite_number(row.cooldown_hours) and row.semantic_cooldown_hours < row.cooldown_hours:
        issues.error(
            "semantic_cooldown_shorter",
            "semantic_cooldown_hours must not be shorter than the row cooldown",
            line_id,
            row_number,
        )
    if not _is_integer(row.max_per_day) or row.max_per_day not in {1, 2}:
        issues.error("invalid_max_per_day", "max_per_day must be integer 1 or 2", line_id, row_number)
    if not _is_finite_number(row.weight) or not 0 < row.weight <= 2:
        issues.error("invalid_weight", "weight must be finite and in (0, 2]", line_id, row_number)
    if not isinstance(row.requires_reply, bool) or not isinstance(row.enabled, bool):
        issues.error("invalid_boolean", "requires_reply and enabled must be booleans", line_id, row_number)

    tokens = _required_context_tokens(row.required_context)
    if tokens is None:
        issues.error(
            "invalid_required_context",
            "required_context must be comma-separated controlled tokens; none must stand alone",
            line_id,
            row_number,
        )
    text = row.text if isinstance(row.text, str) else ""
    enabled = row.enabled is True
    if isinstance(row.text, str) and not normalize_text(row.text):
        issues.error(
            "normalized_text_empty",
            "text is empty after NFKC/casefold/punctuation/format normalization",
            line_id,
            row_number,
        )
    if _trigger_context_conflict(row.trigger, tokens):
        issues.error(
            "trigger_context_conflict",
            f"trigger {row.trigger!r} conflicts with required_context {row.required_context!r}",
            line_id,
            row_number,
        )
    if any(character in text for character in ("\t", "\r", "\n")) or any(
        unicoded in text for unicoded in ("\u2028", "\u2029")
    ):
        issues.error("control_character", "text contains a tab or physical line separator", line_id, row_number)
    validate_safety_preflight(
        row,
        row_number,
        issues,
        context_tokens=tokens,
        has_identity_marker=_has_identity_marker,
        looks_like_non_identity_pii=_looks_like_non_identity_pii,
        identity_pii_is_adjudicated=_is_adjudicated_identity,
        direct_state_patterns=DIRECT_STATE_PATTERNS,
        technical_current_patterns=TECHNICAL_CURRENT_PATTERNS,
        unsafe_emotional_markers=STRONG_EMOTION_MARKERS,
    )

    if enabled and row.category_group == "easter_egg" and row.source_kind != "curated_authored":
        rare = any(
            marker in f"{row.semantic_group};{row.source_reference}".lower()
            for marker in ("rare", "privacy", "anniversary")
        )
        minimum = 720 if rare else 168
        if not _is_finite_number(row.cooldown_hours) or row.cooldown_hours < minimum:
            issues.error(
                "easter_egg_cooldown",
                f"EasterEgg cooldown_hours must be >= {minimum}",
                line_id,
                row_number,
            )
        if row.max_per_day != 1 or isinstance(row.max_per_day, bool):
            issues.error(
                "easter_egg_daily_limit", "EasterEgg max_per_day must be 1", line_id, row_number
            )
        if not _is_finite_number(row.weight) or row.weight > 0.10:
            issues.error(
                "easter_egg_row_weight",
                "EasterEgg row weight must not exceed 0.10",
                line_id,
                row_number,
            )

    if _is_integer(row.interrupt_cost) and row.interrupt_cost >= 4 and _is_finite_number(row.weight) and row.weight > 0.5:
        issues.error(
            "high_cost_weight",
            "interrupt_cost 4-5 content must use weight <= 0.5",
            line_id,
            row_number,
        )
    strong_emotion = row.tone == "intimate" or (
        row.category_group != "technical"
        and any(marker in text for marker in STRONG_EMOTION_MARKERS)
    )
    if (
        enabled
        and row.source_kind != "curated_authored"
        and strong_emotion
        and _is_finite_number(row.weight)
        and row.weight > 0.5
    ):
        issues.error(
            "high_emotion_weight",
            "strong emotional content must use weight <= 0.5",
            line_id,
            row_number,
        )
    if (
        enabled
        and isinstance(row.source_kind, str)
        and row.source_kind in {"archived_question", "manual_review"}
    ):
        issues.error(
            "unsafe_source_kind",
            "archived_question and manual_review rows cannot be enabled",
            line_id,
            row_number,
        )

    lineage = row.source_reference.lower() if isinstance(row.source_reference, str) else ""
    if (
        re.search(r"(?:^|;)prefix:[^;]+", lineage)
        and re.search(r"(?:^|;)(?:core|topic):[^;]+", lineage)
        and re.search(r"(?:^|;)suffix:[^;]+", lineage)
    ):
        issues.error(
            "cartesian_signature",
            "runtime row exposes prefix/core/suffix combination lineage",
            line_id,
            row_number,
        )
