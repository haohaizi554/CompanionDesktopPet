from __future__ import annotations

from collections.abc import Sequence

from ..contract import PERSONA_CONTRACT
from ..models import CorpusLine
from .common import IssueSink


def validate_dry_sharp_contract(
    rows: Sequence[CorpusLine], issues: IssueSink
) -> None:
    policy = PERSONA_CONTRACT.dry_sharp
    forbidden_groups = frozenset(policy["forbidden_category_groups"])
    forbidden_triggers = frozenset(policy["forbidden_triggers"])
    forbidden_contexts = frozenset(policy["forbidden_context_tokens"])
    for row_number, row in enumerate(rows, start=2):
        if row.tone != "dry_sharp":
            continue
        context_tokens = frozenset(row.required_context.split(","))
        violations: list[str] = []
        if row.category_group in forbidden_groups:
            violations.append(f"category_group={row.category_group}")
        if row.trigger in forbidden_triggers:
            violations.append(f"trigger={row.trigger}")
        blocked_contexts = sorted(context_tokens & forbidden_contexts)
        if blocked_contexts:
            violations.append(f"required_context={blocked_contexts!r}")
        if violations:
            issues.error(
                "dry_sharp_placement",
                "dry_sharp is forbidden at " + ", ".join(violations),
                row.id,
                row_number,
            )

    enabled = tuple(row for row in rows if row.enabled is True)
    if not enabled:
        return
    count = sum(row.tone == "dry_sharp" for row in enabled)
    enforcement_minimum = int(policy["inventory_enforcement_minimum_rows"])
    bootstrap_minimum = int(policy["bootstrap_minimum_rows"])
    if len(enabled) >= enforcement_minimum:
        lower, upper = (float(value) for value in policy["inventory_acceptance"])
        ratio = count / len(enabled)
        if not lower <= ratio <= upper:
            issues.error(
                "dry_sharp_inventory_ratio",
                f"dry_sharp inventory ratio {ratio:.6f} must be in [{lower}, {upper}]",
            )
    elif len(enabled) >= 800 and count < bootstrap_minimum:
        issues.error(
            "dry_sharp_inventory_bootstrap",
            f"bootstrap corpus needs at least {bootstrap_minimum} dry_sharp rows; found {count}",
        )


__all__ = ["validate_dry_sharp_contract"]
