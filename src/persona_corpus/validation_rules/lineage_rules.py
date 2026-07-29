from __future__ import annotations

import hashlib
import re
from dataclasses import dataclass
from pathlib import Path
from types import MappingProxyType
from typing import Mapping, Sequence

from ..contract import PERSONA_CONTRACT, category_group_for
from ..models import CorpusLine
from .common import IssueSink


_LEGACY_PATTERN = re.compile(str(PERSONA_CONTRACT.lineage["legacy_reference_pattern"]))
_CATALOG_PATTERN = re.compile(str(PERSONA_CONTRACT.lineage["catalog_reference_pattern"]))
_LEGACY_SOURCE_KINDS = frozenset(
    {"rewritten_topic", "legacy_surface_variant", "preserved_easter_egg"}
)
_CATALOG_SOURCE_KINDS = frozenset({"curated_standalone", "curated_authored", "new_ambient"})


@dataclass(frozen=True, slots=True)
class ParsedReference:
    variant_id: str
    topic_id: str | None
    legacy_line: int | None


@dataclass(frozen=True, slots=True)
class ExpectedLineage:
    variant_id: str
    category: str
    category_group: str
    topic_id: str
    source_kind: str
    source_reference: str
    line_id: str = ""
    text_sha256: str = ""
    legacy_line: int | None = None
    catalog_error: str = ""


@dataclass(frozen=True, slots=True)
class LegacySource:
    category: str
    topic_id: str


@dataclass(frozen=True, slots=True)
class LineageRegistry:
    by_variant: Mapping[str, ExpectedLineage]
    by_legacy_line: Mapping[int, LegacySource]


def parse_source_reference(
    row: CorpusLine,
    row_number: int,
    issues: IssueSink,
) -> ParsedReference | None:
    reference = row.source_reference if isinstance(row.source_reference, str) else ""
    legacy = _LEGACY_PATTERN.fullmatch(reference)
    if legacy is not None:
        source_line = int(legacy.group(1))
        topic_id = legacy.group(2)
        variant_id = legacy.group(3)
        minimum = int(PERSONA_CONTRACT.lineage["legacy_source_min_line"])
        maximum = int(PERSONA_CONTRACT.lineage["legacy_source_max_line"])
        if not minimum <= source_line <= maximum:
            issues.error(
                "legacy_line_out_of_range",
                f"legacy source line {source_line} is outside [{minimum}, {maximum}]",
                row.id,
                row_number,
            )
        if row.topic_id != topic_id:
            issues.error(
                "lineage_topic_mismatch",
                f"row topic_id {row.topic_id!r} differs from reference topic {topic_id!r}",
                row.id,
                row_number,
            )
        if not isinstance(row.source_kind, str) or row.source_kind not in _LEGACY_SOURCE_KINDS:
            issues.error(
                "lineage_source_kind_mismatch",
                f"legacy reference cannot use source_kind {row.source_kind!r}",
                row.id,
                row_number,
            )
        return ParsedReference(variant_id, topic_id, source_line)

    catalog = _CATALOG_PATTERN.fullmatch(reference)
    if catalog is not None:
        variant_id = catalog.group(2)
        if not isinstance(row.source_kind, str) or row.source_kind not in _CATALOG_SOURCE_KINDS:
            issues.error(
                "lineage_source_kind_mismatch",
                f"catalog reference cannot use source_kind {row.source_kind!r}",
                row.id,
                row_number,
            )
        return ParsedReference(variant_id, None, None)

    issues.error(
        "invalid_source_reference",
        "source_reference must end in one stable variant and use the legacy or catalog grammar",
        row.id,
        row_number,
    )
    return None


def validate_lineage_structure(rows: Sequence[CorpusLine], issues: IssueSink) -> None:
    for row_number, row in enumerate(rows, start=2):
        parse_source_reference(row, row_number, issues)


def build_repository_registry() -> LineageRegistry:
    from ..authored_catalog import load_authored_catalog
    from ..builder import authored_line_id, load_source_mappings

    root = Path(__file__).resolve().parents[3]
    mappings = {
        mapping.source_line: mapping
        for mapping in load_source_mappings(root / "data/intermediate/source-line-map.tsv")
    }
    catalog = load_authored_catalog(
        root / "data/authored/v1",
        root / "config/persona-authorship-manifest.json",
    )
    expected: dict[str, ExpectedLineage] = {}
    for entry in catalog.entries:
        reference = f"catalog:authored-v1:{entry.batch_id};variant:{entry.variant_id}"
        expected[entry.variant_id] = ExpectedLineage(
            variant_id=entry.variant_id,
            category=entry.category,
            category_group=entry.category_group,
            topic_id=entry.topic_id,
            source_kind="curated_authored",
            source_reference=reference,
            line_id=authored_line_id(entry),
            text_sha256=hashlib.sha256(entry.text.encode("utf-8")).hexdigest(),
        )
    sources = MappingProxyType(
        {
            source_line: LegacySource(mapping.category, mapping.topic_id)
            for source_line, mapping in mappings.items()
        }
    )
    return LineageRegistry(MappingProxyType(expected), sources)


def validate_lineage_registry(
    rows: Sequence[CorpusLine],
    issues: IssueSink,
    registry: LineageRegistry,
) -> None:
    seen: dict[str, int] = {}
    for row_number, row in enumerate(rows, start=2):
        parsed = parse_source_reference(row, row_number, issues)
        if parsed is None:
            continue
        seen[parsed.variant_id] = seen.get(parsed.variant_id, 0) + 1
        expected = registry.by_variant.get(parsed.variant_id)
        if expected is None:
            issues.error(
                "dangling_lineage_variant",
                f"variant {parsed.variant_id!r} is not present in the content catalog",
                row.id,
                row_number,
            )
            continue
        if expected.catalog_error:
            issues.error(
                "dangling_legacy_reference",
                expected.catalog_error,
                row.id,
                row_number,
            )
        comparisons = (
            ("id", row.id, expected.line_id),
            ("category", row.category, expected.category),
            ("category_group", row.category_group, expected.category_group),
            ("topic_id", row.topic_id, expected.topic_id),
            ("source_kind", row.source_kind, expected.source_kind),
            ("source_reference", row.source_reference, expected.source_reference),
        )
        for field, actual, wanted in comparisons:
            if actual != wanted:
                issues.error(
                    f"lineage_{field}_mismatch",
                    f"{field} {actual!r} differs from catalog value {wanted!r}",
                    row.id,
                    row_number,
                )
        if expected.legacy_line is not None and parsed.legacy_line != expected.legacy_line:
            issues.error(
                "lineage_legacy_line_mismatch",
                f"legacy source line {parsed.legacy_line!r} differs from manifest "
                f"{expected.legacy_line!r}",
                row.id,
                row_number,
            )
        if expected.text_sha256:
            actual_digest = hashlib.sha256(row.text.encode("utf-8")).hexdigest()
            if actual_digest != expected.text_sha256:
                issues.error(
                    "lineage_text_hash_mismatch",
                    "runtime text differs from the tracked source-exact surface manifest",
                    row.id,
                    row_number,
                )

    duplicates = sorted(variant for variant, count in seen.items() if count > 1)
    if duplicates:
        issues.error(
            "duplicate_lineage_variant",
            f"lineage variants are reused: {duplicates[:5]!r}",
        )
    missing = sorted(set(registry.by_variant) - set(seen))
    if missing:
        issues.error(
            "unmaterialized_catalog_variant",
            f"{len(missing)} content-catalog variants are absent; examples={missing[:5]!r}",
        )


__all__ = [
    "ExpectedLineage",
    "LegacySource",
    "LineageRegistry",
    "ParsedReference",
    "build_repository_registry",
    "parse_source_reference",
    "validate_lineage_registry",
    "validate_lineage_structure",
]
