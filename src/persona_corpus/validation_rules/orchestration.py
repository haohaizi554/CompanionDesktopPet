"""Corpus/file validation orchestration and report rendering."""

from __future__ import annotations

import hashlib
from pathlib import Path
from typing import Sequence

from ..contract import EXPANDED_RUNTIME_ROWS
from ..loader import CorpusFormatError, load_v2
from ..models import CorpusLine
from .allowlist_rules import _apply_allowlist
from .config_rules import validate_config
from .content_rules import _validate_line
from .core import (
    ValidationInputError,
    ValidationReport,
    _Issues,
    load_json_object,
    scheduler_config_sha256,
)
from .editorial_rules import validate_dry_sharp_contract
from .lineage_rules import (
    LineageRegistry,
    build_repository_registry,
    validate_lineage_registry,
    validate_lineage_structure,
)
from .schema_rules import validate_schema_contract
from .simulation_rules import _simulation_issues
from .surface_rules import (
    _cartesian_grid_issues,
    _distribution_issues,
    _surface_inventory_issues,
)


VALIDATION_GROUPS = (
    (1, "exact_header"),
    (2, "row_width"),
    (3, "unique_id"),
    (4, "exact_enabled_text"),
    (5, "normalized_enabled_text"),
    (6, "output_mode"),
    (7, "trigger"),
    (8, "tone"),
    (9, "interrupt_cost"),
    (10, "cooldown_hours"),
    (11, "semantic_cooldown_hours"),
    (12, "max_per_day"),
    (13, "weight"),
    (14, "reply_free"),
    (15, "question_free"),
    (16, "text_field_integrity"),
    (17, "user_direct_context"),
    (18, "required_context"),
    (19, "pii_review"),
    (20, "easter_egg_cooldown"),
    (21, "high_cost_weight"),
    (22, "technical_fake_context"),
    (23, "cartesian_generation"),
    (24, "catchphrase_frequency"),
    (25, "length_distribution"),
    (26, "scheduler_weights"),
    (27, "simulation"),
)

FORMAT_ERROR_CODES = frozenset(
    {"config_format", "config_keys", "allowlist_format", "simulation_format"}
)

def validate_corpus(
    lines: Sequence[CorpusLine],
    scheduler_config: object,
    allowlist: object,
    simulation_result: object | None = None,
    *,
    lineage_registry: LineageRegistry | None = None,
    _corpus_sha256: str | None = None,
    _scheduler_config_sha256: str | None = None,
    _require_allowlist_binding: bool = False,
    _enforce_canonical_size: bool = False,
) -> ValidationReport:
    rows = tuple(lines)
    issues = _Issues()
    config_report = validate_config(scheduler_config)
    issues.errors.extend(config_report.errors)
    issues.warnings.extend(config_report.warnings)
    for row_number, row in enumerate(rows, start=2):
        if not isinstance(row, CorpusLine):
            issues.error("row_type", "validate_corpus accepts CorpusLine objects", row_number=row_number)
            continue
        _validate_line(row, row_number, issues)
    typed_rows = tuple(row for row in rows if isinstance(row, CorpusLine))
    if _enforce_canonical_size:
        enabled_count = sum(row.enabled is True for row in typed_rows)
        if not EXPANDED_RUNTIME_ROWS[0] <= enabled_count <= EXPANDED_RUNTIME_ROWS[1]:
            issues.error(
                "enabled_count",
                "canonical expanded runtime corpus must contain "
                f"{EXPANDED_RUNTIME_ROWS[0]}-{EXPANDED_RUNTIME_ROWS[1]} enabled rows; "
                f"found {enabled_count}",
            )
    validate_schema_contract(typed_rows, issues)
    validate_dry_sharp_contract(typed_rows, issues)
    if lineage_registry is None:
        validate_lineage_structure(typed_rows, issues)
    else:
        validate_lineage_registry(typed_rows, issues, lineage_registry)
    _cartesian_grid_issues(typed_rows, issues)
    _surface_inventory_issues(typed_rows, issues)
    _distribution_issues(typed_rows, issues)
    if _corpus_sha256 is None:
        from ..builder import serialize_v2

        try:
            payload = serialize_v2(typed_rows)
        except ValueError:
            payload = repr(typed_rows).encode("utf-8", errors="backslashreplace")
        _corpus_sha256 = hashlib.sha256(payload).hexdigest()
    if _scheduler_config_sha256 is None:
        try:
            _scheduler_config_sha256 = scheduler_config_sha256(scheduler_config)
        except (TypeError, ValueError):
            _scheduler_config_sha256 = hashlib.sha256(
                repr(scheduler_config).encode("utf-8", errors="backslashreplace")
            ).hexdigest()
    _simulation_issues(
        simulation_result,
        typed_rows,
        scheduler_config,
        issues,
        expected_corpus_sha256=_corpus_sha256,
        expected_scheduler_config_sha256=_scheduler_config_sha256,
    )
    _apply_allowlist(
        typed_rows,
        allowlist,
        issues,
        expected_corpus_sha256=_corpus_sha256,
        require_corpus_binding=_require_allowlist_binding,
    )
    return issues.report()


def validate_file(
    corpus_path: Path,
    config_path: Path,
    allowlist_path: Path,
    simulation_path: Path | None = None,
) -> ValidationReport:
    corpus_path = Path(corpus_path)
    try:
        lines = load_v2(corpus_path)
    except CorpusFormatError as error:
        raise ValidationInputError(str(error)) from error
    config = load_json_object(Path(config_path))
    allowlist = load_json_object(Path(allowlist_path))
    simulation = load_json_object(Path(simulation_path)) if simulation_path is not None else None
    try:
        corpus_sha256 = hashlib.sha256(corpus_path.read_bytes()).hexdigest()
    except OSError as error:
        raise ValidationInputError(f"{corpus_path}: cannot hash corpus: {error}") from error
    return validate_corpus(
        lines,
        config,
        allowlist,
        simulation_result=simulation,
        lineage_registry=build_repository_registry(),
        _corpus_sha256=corpus_sha256,
        _scheduler_config_sha256=scheduler_config_sha256(config),
        _require_allowlist_binding=True,
        _enforce_canonical_size=True,
    )


def format_report(report: ValidationReport) -> str:
    lines = [
        f"Validation: {len(report.errors)} hard errors, {len(report.warnings)} warnings"
    ]
    for severity, entries in (("ERROR", report.errors), ("WARNING", report.warnings)):
        for issue in entries:
            location = ""
            if issue.line_id:
                location += f" [{issue.line_id}]"
            if issue.row_number is not None:
                location += f" [row {issue.row_number}]"
            lines.append(f"{severity} {issue.code}{location}: {issue.message}")
    return "\n".join(lines)
