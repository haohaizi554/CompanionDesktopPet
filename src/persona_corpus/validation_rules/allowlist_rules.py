"""Hash-bound validation exception rules."""

from __future__ import annotations

import re
from collections import defaultdict
from typing import Mapping, Sequence

from ..models import CorpusLine
from .core import ValidationIssue, _Issues, normalized_text_sha256


ALLOWLIST_KEYS = frozenset(
    {"rule_code", "line_id", "normalized_text_sha256", "reason"}
)
ALLOWLISTABLE_CODES = frozenset(
    {"fake_context", "user_direct_context", "technical_fake_context", "pii_enabled"}
)

def _apply_allowlist(
    rows: Sequence[CorpusLine],
    allowlist: object,
    issues: _Issues,
    *,
    expected_corpus_sha256: str,
    require_corpus_binding: bool,
) -> None:
    if not isinstance(allowlist, Mapping):
        issues.error("allowlist_format", "allowlist root must be a JSON object")
        return
    keys = set(allowlist)
    legacy_unbound = keys == {"exceptions"} and not require_corpus_binding
    bound = keys == {"$schema", "schema_version", "corpus_sha256", "exceptions"}
    if not legacy_unbound and not bound:
        issues.error(
            "allowlist_format",
            "file allowlist must contain exactly $schema, schema_version, corpus_sha256 and exceptions",
        )
        return
    if bound:
        digest = allowlist.get("corpus_sha256")
        if allowlist.get("$schema") != "./schemas/persona-review-allowlist.schema.json":
            issues.error(
                "allowlist_format",
                "allowlist $schema must reference ./schemas/persona-review-allowlist.schema.json",
            )
            return
        if allowlist.get("schema_version") != 1 or isinstance(
            allowlist.get("schema_version"), bool
        ):
            issues.error("allowlist_format", "allowlist schema_version must be integer 1")
            return
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            issues.error("allowlist_format", "allowlist corpus_sha256 must be lowercase SHA-256")
            return
        if digest != expected_corpus_sha256:
            issues.error(
                "allowlist_corpus_hash_mismatch",
                "allowlist corpus_sha256 does not match the exact corpus under validation",
            )
            return
    entries = allowlist.get("exceptions")
    if not isinstance(entries, list):
        issues.error("allowlist_format", "allowlist exceptions must be an array")
        return

    by_id: dict[str, list[CorpusLine]] = defaultdict(list)
    for row in rows:
        by_id[str(row.id)].append(row)
    seen: set[tuple[str, str]] = set()
    active: dict[tuple[str, str], str] = {}
    invalid_keys: set[tuple[str, str]] = set()
    for position, entry in enumerate(entries, start=1):
        if not isinstance(entry, Mapping) or set(entry) != ALLOWLIST_KEYS:
            issues.error(
                "allowlist_format",
                f"allowlist exception {position} must contain exactly rule_code, line_id, normalized_text_sha256 and reason",
            )
            continue
        rule_code = entry.get("rule_code")
        line_id = entry.get("line_id")
        digest = entry.get("normalized_text_sha256")
        reason = entry.get("reason")
        if (
            not isinstance(rule_code, str)
            or rule_code not in ALLOWLISTABLE_CODES
            or not isinstance(line_id, str)
            or not line_id
            or not isinstance(digest, str)
            or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            or not isinstance(reason, str)
            or not reason.strip()
        ):
            issues.error("allowlist_format", f"allowlist exception {position} has invalid values")
            continue
        key = (rule_code, line_id)
        if key in seen:
            issues.error(
                "allowlist_duplicate",
                f"allowlist tuple ({rule_code!r}, {line_id!r}) occurs more than once",
                line_id,
            )
            invalid_keys.add(key)
            continue
        seen.add(key)
        matches = by_id.get(line_id, [])
        if len(matches) != 1:
            issues.error("allowlist_unknown_line", f"allowlist line_id {line_id!r} does not resolve uniquely", line_id)
            invalid_keys.add(key)
            continue
        actual = normalized_text_sha256(matches[0].text)
        if actual != digest:
            issues.error(
                "allowlist_hash_mismatch",
                f"allowlist normalized-text SHA-256 for {line_id!r} is stale or mismatched",
                line_id,
            )
            invalid_keys.add(key)
            continue
        active[key] = reason.strip()

    original_errors = list(issues.errors)
    retained: list[ValidationIssue] = []
    used: set[tuple[str, str]] = set()
    for issue in original_errors:
        key = (issue.code, issue.line_id)
        reason = active.get(key)
        if reason is not None and key not in invalid_keys:
            used.add(key)
            issues.warning(
                f"allowlisted_{issue.code}",
                f"{issue.message} Exception reason: {reason}",
                issue.line_id,
                issue.row_number,
            )
        else:
            retained.append(issue)
    issues.errors = retained
    for rule_code, line_id in sorted(set(active) - used - invalid_keys):
        issues.error(
            "allowlist_stale",
            f"allowlist entry for ({rule_code!r}, {line_id!r}) no longer matches that exact heuristic finding",
            line_id,
        )
