from __future__ import annotations

import csv
import hashlib
import re
from dataclasses import dataclass
from pathlib import Path
from types import MappingProxyType
from typing import Mapping, Sequence

from ..contract import PERSONA_CONTRACT, category_group_for
from ..models import CorpusLine
from ..schema import SURFACE_MANIFEST_HEADER
from .common import IssueSink


_LEGACY_PATTERN = re.compile(str(PERSONA_CONTRACT.lineage["legacy_reference_pattern"]))
_CATALOG_PATTERN = re.compile(str(PERSONA_CONTRACT.lineage["catalog_reference_pattern"]))
_LEGACY_SOURCE_KINDS = frozenset(
    {"rewritten_topic", "legacy_surface_variant", "preserved_easter_egg"}
)
_CATALOG_SOURCE_KINDS = frozenset({"curated_standalone", "new_ambient"})


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
    from ..builder import SOURCE_KIND_ALIASES, catalog_line_id, load_source_mappings
    from ..content_catalog import CONTENT_CATALOG
    from ..surface_variants import legacy_surface_line_id, legacy_surface_variant_token

    root = Path(__file__).resolve().parents[3]
    mappings = {
        mapping.source_line: mapping
        for mapping in load_source_mappings(root / "data/intermediate/source-line-map.tsv")
    }
    expected: dict[str, ExpectedLineage] = {}
    for entry in CONTENT_CATALOG:
        reference = f"{entry.source_reference};variant:{entry.variant_id}"
        source_kind = (
            "new_ambient"
            if entry.category_group == "system_ambient"
            else SOURCE_KIND_ALIASES.get(entry.source_kind, entry.source_kind)
        )
        catalog_error = ""
        legacy = re.fullmatch(r"legacy:(\d+);topic:([^;]+)", entry.source_reference)
        if legacy is not None:
            source_line = int(legacy.group(1))
            mapping = mappings.get(source_line)
            if mapping is None:
                catalog_error = f"legacy source line {source_line} does not exist"
            elif mapping.category != entry.category or mapping.topic_id != entry.runtime_topic_id:
                catalog_error = (
                    f"legacy source line {source_line} resolves to "
                    f"{mapping.category!r}/{mapping.topic_id!r}"
                )
        expected[entry.variant_id] = ExpectedLineage(
            variant_id=entry.variant_id,
            category=entry.category,
            category_group=category_group_for(entry.category),
            topic_id=entry.runtime_topic_id,
            source_kind=source_kind,
            source_reference=reference,
            line_id=catalog_line_id(entry),
            catalog_error=catalog_error,
        )
    manifest_path = root / "data/optimized/persona-surface-manifest.tsv"
    try:
        with manifest_path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.reader(stream, delimiter="\t", strict=True)
            header = tuple(next(reader))
            if header != SURFACE_MANIFEST_HEADER:
                raise ValueError("surface manifest header mismatch")
            manifest_rows = [dict(zip(header, values, strict=True)) for values in reader]
    except (OSError, UnicodeError, csv.Error, StopIteration, ValueError) as error:
        raise ValueError(f"{manifest_path}: invalid tracked surface manifest: {error}") from error

    seen_line_ids: set[str] = set()
    for value in manifest_rows:
        try:
            source_line = int(value["source_line"])
        except (KeyError, ValueError) as error:
            raise ValueError("surface manifest contains an invalid source_line") from error
        variant_id = value["variant_id"]
        line_id = value["line_id"]
        mapping = mappings.get(source_line)
        if mapping is None:
            raise ValueError(f"surface manifest references missing source line {source_line}")
        source_digest = hashlib.sha256(mapping.original_text.encode("utf-8")).hexdigest()
        expected_id = legacy_surface_line_id(source_line, mapping.topic_id, mapping.original_text)
        expected_variant = legacy_surface_variant_token(
            source_line,
            mapping.topic_id,
            mapping.original_text,
        )
        expected_reference = (
            f"legacy:{source_line};topic:{mapping.topic_id};variant:{expected_variant}"
        )
        if (
            line_id in seen_line_ids
            or variant_id in expected
            or line_id != expected_id
            or variant_id != expected_variant
            or value["category"] != mapping.category
            or value["category_group"] != category_group_for(mapping.category)
            or value["topic_id"] != mapping.topic_id
            or value["source_reference"] != expected_reference
            or value["text_sha256"] != source_digest
            or value["source_text_sha256"] != source_digest
        ):
            raise ValueError(f"surface manifest entry {line_id!r} is not source-exact")
        seen_line_ids.add(line_id)
        expected[variant_id] = ExpectedLineage(
            variant_id=variant_id,
            category=mapping.category,
            category_group=category_group_for(mapping.category),
            topic_id=mapping.topic_id,
            source_kind="legacy_surface_variant",
            source_reference=expected_reference,
            line_id=line_id,
            text_sha256=source_digest,
            legacy_line=source_line,
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
