from __future__ import annotations

from collections.abc import Callable, Sequence

from ..models import CorpusLine
from ..surface_safety import (
    IMPLICIT_QUESTION_MARKERS,
    MAX_SURFACE_TEXT_LENGTH,
    REPLY_HOOK_MARKERS,
    UNAVAILABLE_STATE_MARKERS,
)
from .common import IssueSink


def validate_safety_preflight(
    row: CorpusLine,
    row_number: int,
    issues: IssueSink,
    *,
    context_tokens: tuple[str, ...] | None,
    has_identity_marker: Callable[[str], bool],
    looks_like_non_identity_pii: Callable[[str], bool],
    identity_pii_is_adjudicated: Callable[[CorpusLine], bool],
    direct_state_patterns: Sequence[str],
    technical_current_patterns: Sequence[str],
    unsafe_emotional_markers: Sequence[str],
) -> None:
    line_id = row.id
    text = row.text if isinstance(row.text, str) else ""
    has_context = context_tokens is not None and context_tokens != ("none",)

    if row.requires_reply is True:
        issues.error("requires_reply", "text must not require a reply", line_id, row_number)
    if "?" in text or "？" in text:
        issues.error("question", "original text contains a question mark", line_id, row_number)
    if row.source_kind == "legacy_surface_variant":
        if row.category_group != "easter_egg" and len(text) > MAX_SURFACE_TEXT_LENGTH:
            issues.error(
                "surface_length",
                f"legacy surface text exceeds {MAX_SURFACE_TEXT_LENGTH} characters",
                line_id,
                row_number,
            )
        if any(marker in text for marker in (*IMPLICIT_QUESTION_MARKERS, *REPLY_HOOK_MARKERS)):
            issues.error(
                "surface_reply_hook",
                "legacy surface text contains an implicit question or reply hook",
                line_id,
                row_number,
            )
        if any(marker in text for marker in UNAVAILABLE_STATE_MARKERS):
            issues.error(
                "surface_fake_context",
                "legacy surface text asserts unavailable body or environment state",
                line_id,
                row_number,
            )

    direct_state = next((pattern for pattern in direct_state_patterns if pattern in text), None)
    if direct_state and not has_context:
        issues.error(
            "fake_context",
            f"text asserts unavailable user context via {direct_state!r}",
            line_id,
            row_number,
        )
        if row.output_mode == "user_direct":
            issues.error(
                "user_direct_context",
                "user_direct state assertion needs a non-none required_context gate",
                line_id,
                row_number,
            )
    if (
        row.category_group == "technical"
        and row.output_mode == "user_direct"
        and (context_tokens is None or "ide_foreground" not in context_tokens)
    ):
        issues.error(
            "user_direct_context",
            "technical user_direct text must be gated by ide_foreground",
            line_id,
            row_number,
        )

    folded = text.casefold()
    technical_pattern = next(
        (pattern for pattern in technical_current_patterns if pattern.casefold() in folded),
        None,
    )
    if row.category_group == "technical" and technical_pattern and not has_context:
        issues.error(
            "technical_fake_context",
            f"technical text uses current-object shorthand {technical_pattern!r} without context",
            line_id,
            row_number,
        )
        if row.source_kind == "legacy_surface_variant":
            issues.error(
                "surface_fake_context",
                "legacy surface text claims an unavailable current technical object or environment",
                line_id,
                row_number,
            )
    identity_marker = has_identity_marker(text)
    if looks_like_non_identity_pii(text) or (
        identity_marker and not identity_pii_is_adjudicated(row)
    ):
        issues.error(
            "pii_enabled",
            "text matches a name, location, income, employment or identifier PII heuristic",
            line_id,
            row_number,
        )
    unsafe_claim = (
        next((marker for marker in unsafe_emotional_markers if marker in text), None)
        if row.category_group != "technical"
        else None
    )
    if unsafe_claim:
        issues.error(
            "unsafe_emotional_claim",
            f"text contains unsafe emotional promise {unsafe_claim!r}",
            line_id,
            row_number,
        )


__all__ = ["validate_safety_preflight"]
