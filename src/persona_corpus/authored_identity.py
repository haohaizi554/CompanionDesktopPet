"""Fail-closed source validation for authored identity Easter Egg entries.

This is intentionally independent from the legacy editorial manifest.  The
versioned persona contract is the only authority that can admit a known
identity marker into an authored source batch.
"""

from __future__ import annotations

import re
import unicodedata
from collections import Counter
from collections.abc import Iterable
from typing import Protocol

from .contract import PERSONA_CONTRACT
from .privacy import ENABLED_CONTENT_POLICY, classify_pii, pii_findings
from .surface_safety import (
    IMPLICIT_QUESTION_MARKERS,
    REPLY_HOOK_MARKERS,
    UNAVAILABLE_STATE_MARKERS,
)
from .validation_rules.content_rules import (
    DIRECT_STATE_PATTERNS,
    TECHNICAL_CURRENT_PATTERNS,
)


# These rules intentionally apply only to a line carrying a contract-approved
# marker.  They are source-admission guardrails, not general-purpose Chinese
# prose moderation, and therefore do not turn ordinary technical narration into
# a false positive merely because it contains a generic word such as "小".
_IDENTITY_DEPENDENCY_OR_COERCION_MARKERS = (
    "只有你",
    "只有我",
    "只能陪着你",
    "别走",
    "不许离开",
    "必须陪着你",
    "永远陪",
)
_IDENTITY_SEXUAL_CONTENT_PATTERNS = (
    re.compile(r"(?:上床|做爱|性爱|性行为|色情)"),
    re.compile(
        r"(?:和你|跟你).{0,10}(?:同床|同睡|共寝|共枕|睡在一起).{0,10}"
        r"(?:一夜|过夜|整晚|到天亮|睡)"
    ),
    re.compile(
        r"(?:和你|跟你).{0,10}(?:一张床|床上).{0,10}"
        r"(?:睡|过夜|整晚|到天亮)"
    ),
)
_IDENTITY_BIOGRAPHY_PATTERNS = (
    re.compile(r"(?:今年|我今年|她今年)?\s*\d{1,2}\s*岁"),
    re.compile(r"职高(?:肄|肆)业"),
    re.compile(r"(?:辍学|打零工|月薪|老家|漂泊)"),
)
# A short 小X subject is a commonly used nickname shape, but it is only
# treated as an identity claim when followed by a personal action/predicate.
# This admits generic technical nouns such as 小程序 while rejecting the
# unregistered ordinary-category identity form "小月把…".
_UNREGISTERED_NICKNAME_PATTERN = re.compile(
    r"(?<![\u3400-\u9fff])小[\u3400-\u9fff]"
    r"(?=(?:把|在|会|想|只|能|必须|今年|来自|住在|说|告诉|陪|来了|走了))"
)


class _AuthoredIdentityEntry(Protocol):
    batch_id: str
    variant_id: str
    category: str
    category_group: str
    output_mode: str
    relationship_profile: str
    text: str


def normalize_identity_analysis_text(text: str) -> str:
    """Remove format/zero-width controls for source-safety analysis only.

    The authored TSV payload remains byte-for-byte untouched.  This deliberately
    avoids general NFKC/case-folding or wording rewrites: it closes invisible
    character bypasses without changing normal non-identity corpus semantics.
    """

    return "".join(
        character for character in text if unicodedata.category(character) != "Cf"
    )


def _marker_hits_from_analysis(analysis_text: str) -> tuple[str, ...]:
    return tuple(
        marker for marker in PERSONA_CONTRACT.pii_markers if marker in analysis_text
    )


def marker_hits(text: str) -> tuple[str, ...]:
    """Return ordered, deduplicated contract identity markers in *text*."""

    return _marker_hits_from_analysis(normalize_identity_analysis_text(text))


def _diagnostic(
    entry: _AuthoredIdentityEntry,
    invariant: str,
    marker: str | None = None,
) -> str:
    marker_detail = "" if marker is None else f" marker {marker!r}"
    return (
        f"{entry.batch_id}.tsv: variant {entry.variant_id}{marker_detail}: "
        f"{invariant}"
    )


def _unsupported_observation_marker(text: str) -> str | None:
    for marker in (
        *DIRECT_STATE_PATTERNS,
        *TECHNICAL_CURRENT_PATTERNS,
        *UNAVAILABLE_STATE_MARKERS,
        *IMPLICIT_QUESTION_MARKERS,
        *REPLY_HOOK_MARKERS,
    ):
        if marker in text:
            return marker
    return None


def _identity_safety_violation(text: str) -> tuple[str, str] | None:
    dependency = next(
        (marker for marker in _IDENTITY_DEPENDENCY_OR_COERCION_MARKERS if marker in text),
        None,
    )
    if dependency is not None:
        return ("dependency, exclusivity, or coercion", dependency)
    sexual = next(
        (match for pattern in _IDENTITY_SEXUAL_CONTENT_PATTERNS if (match := pattern.search(text))),
        None,
    )
    if sexual is not None:
        return ("sexual content", sexual.group(0))
    biography = next(
        (
            match
            for pattern in _IDENTITY_BIOGRAPHY_PATTERNS
            if (match := pattern.search(text)) is not None
        ),
        None,
    )
    if biography is not None:
        return ("false real-person biography", biography.group(0))
    return None


def _unregistered_nickname(text: str) -> str | None:
    match = _UNREGISTERED_NICKNAME_PATTERN.search(text)
    return None if match is None else match.group(0)


def validate_authored_identity_entries(entries: Iterable[_AuthoredIdentityEntry]) -> None:
    """Reject unauthorised identity markers and malformed identity batches.

    Every violation is collected so a source author gets batch, variant, marker,
    and failed invariant together.  This validation is deliberately placed
    before manifest construction, so an invalid source can never acquire a
    provenance digest or ledger row.
    """

    policy = PERSONA_CONTRACT.authored_identity
    identity_batches = tuple(policy["easter_egg_batches"])
    identity_batch_set = frozenset(identity_batches)
    direct_marker_batches = dict(policy["direct_marker_batches"])
    allowed_profiles = frozenset(policy["allowed_relationship_profiles"])
    expected_tuple = (
        policy["category"],
        policy["category_group"],
        policy["output_mode"],
    )
    known_markers = frozenset(PERSONA_CONTRACT.pii_markers)
    errors: list[str] = []
    all_entries = tuple(entries)
    by_batch = Counter(entry.batch_id for entry in all_entries)
    identity_entries = tuple(
        entry for entry in all_entries if entry.batch_id in identity_batch_set
    )

    expected_identity_rows = len(identity_batches) * 300
    if len(identity_entries) != expected_identity_rows:
        errors.append(
            "authored identity batches b083-b092: "
            f"expected exactly {expected_identity_rows} rows, found {len(identity_entries)}"
        )
    for batch_id in identity_batches:
        if by_batch[batch_id] != 300:
            errors.append(
                f"{batch_id}.tsv: expected exactly 300 authored identity rows, "
                f"found {by_batch[batch_id]}"
            )

    direct_marker_counts: Counter[tuple[str, str]] = Counter()
    for entry in all_entries:
        analysis_text = normalize_identity_analysis_text(entry.text)
        hits = _marker_hits_from_analysis(analysis_text)
        nickname = _unregistered_nickname(analysis_text)
        enabled_pii = pii_findings(analysis_text, ENABLED_CONTENT_POLICY)
        for finding in classify_pii(analysis_text):
            if finding.kind == "known_identity" and finding.evidence in known_markers:
                continue
            if finding in enabled_pii:
                errors.append(
                    _diagnostic(
                        entry,
                        f"identity authorization never exempts {finding.kind}",
                        hits[0] if hits else finding.evidence,
                    )
                )

        if hits:
            if entry.relationship_profile not in allowed_profiles:
                errors.append(
                    _diagnostic(
                        entry,
                        "relationship_profile is not allowed for identity markers",
                        hits[0],
                    )
                )
            if "?" in analysis_text or "？" in analysis_text:
                errors.append(
                    _diagnostic(entry, "identity text must not contain a question mark", hits[0])
                )
            observation = _unsupported_observation_marker(analysis_text)
            if observation is not None:
                errors.append(
                    _diagnostic(
                        entry,
                        f"identity text asserts an unsupported observation via {observation!r}",
                        hits[0],
                    )
                )
            identity_safety = _identity_safety_violation(analysis_text)
            if identity_safety is not None:
                invariant, evidence = identity_safety
                errors.append(
                    _diagnostic(
                        entry,
                        f"identity text contains {invariant} via {evidence!r}",
                        hits[0],
                    )
                )
            if entry.batch_id not in identity_batch_set and not policy[
                "allow_markers_in_any_category"
            ]:
                errors.append(
                    _diagnostic(
                        entry,
                        "identity marker is outside the authorised Easter Egg batches",
                        hits[0],
                    )
                )

        if (
            nickname is not None
            and nickname not in known_markers
            and entry.category_group != "easter_egg"
        ):
            errors.append(
                _diagnostic(
                    entry,
                    "ordinary-category text contains an unregistered identity/nickname",
                    nickname,
                )
            )

        if entry.batch_id in identity_batch_set and (
            entry.category,
            entry.category_group,
            entry.output_mode,
        ) != expected_tuple:
            errors.append(
                _diagnostic(
                    entry,
                    "identity batch must use EasterEgg/easter_egg/self_talk",
                )
            )

        assigned_marker = direct_marker_batches.get(entry.batch_id)
        if assigned_marker is None:
            continue
        direct_marker_counts[(entry.batch_id, assigned_marker)] += analysis_text.count(
            assigned_marker
        )
        if hits != (assigned_marker,) or analysis_text.count(assigned_marker) != 1:
            errors.append(
                _diagnostic(
                    entry,
                    "direct marker batch requires its assigned marker exactly once and no other direct marker; "
                    "the batch must contain exactly 300 direct hits",
                    assigned_marker,
                )
            )

    for batch_id, marker in direct_marker_batches.items():
        count = direct_marker_counts[(batch_id, marker)]
        if count != 300:
            errors.append(
                f"{batch_id}.tsv: marker {marker!r}: direct marker count must be exactly 300, found {count}"
            )

    if errors:
        raise ValueError("authored identity validation failed:\n- " + "\n- ".join(errors))


__all__ = (
    "marker_hits",
    "normalize_identity_analysis_text",
    "validate_authored_identity_entries",
)
