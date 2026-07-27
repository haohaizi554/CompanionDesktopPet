"""Fail-closed source validation for authored identity Easter Egg entries.

This is intentionally independent from the legacy editorial manifest.  The
versioned persona contract is the only authority that can admit a known
identity marker into an authored source batch.
"""

from __future__ import annotations

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


class _AuthoredIdentityEntry(Protocol):
    batch_id: str
    variant_id: str
    category: str
    category_group: str
    output_mode: str
    relationship_profile: str
    text: str


def marker_hits(text: str) -> tuple[str, ...]:
    """Return ordered, deduplicated contract identity markers in *text*."""

    return tuple(marker for marker in PERSONA_CONTRACT.pii_markers if marker in text)


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
        hits = marker_hits(entry.text)
        enabled_pii = pii_findings(entry.text, ENABLED_CONTENT_POLICY)
        for finding in classify_pii(entry.text):
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
            if "?" in entry.text or "？" in entry.text:
                errors.append(
                    _diagnostic(entry, "identity text must not contain a question mark", hits[0])
                )
            observation = _unsupported_observation_marker(entry.text)
            if observation is not None:
                errors.append(
                    _diagnostic(
                        entry,
                        f"identity text asserts an unsupported observation via {observation!r}",
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
        direct_marker_counts[(entry.batch_id, assigned_marker)] += entry.text.count(
            assigned_marker
        )
        if hits != (assigned_marker,) or entry.text.count(assigned_marker) != 1:
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


__all__ = ("marker_hits", "validate_authored_identity_entries")
