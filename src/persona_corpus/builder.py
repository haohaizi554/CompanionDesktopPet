from __future__ import annotations

import csv
import hashlib
import re
from collections import defaultdict
from dataclasses import dataclass, fields
from pathlib import Path
from types import MappingProxyType
from typing import Iterable, Mapping, Sequence, TypeVar

from .content_catalog import CONTENT_CATALOG, CatalogEntry
from .extraction import SourceMapping
from .models import CorpusLine, LegacyLine
from .normalization import normalize_text
from .schema import (
    ARCHIVE_HEADER,
    PII_REVIEW_HEADER,
    REVIEW_HEADER,
    V2_HEADER,
    ArchiveRow,
    PiiReviewRow,
    ReviewRow,
)


SOURCE_MAPPING_HEADER = (
    "source_line",
    "category",
    "original_text",
    "prefix_id",
    "topic_id",
    "suffix_id",
    "extraction_confidence",
)

PII_MARKERS = (
    "雷琳玥",
    "小玥",
    "湖南",
    "长沙",
    "广东",
    "姓名",
    "名字",
    "住在",
    "地址",
    "工资",
    "收入",
    "月薪",
    "打零工",
    "换工作",
)
PII_PATTERNS = (
    re.compile(r"(?<!\d)1[3-9]\d{9}(?!\d)"),
    re.compile(r"(?<!\d)\d{17}[\dXx](?!\d)"),
    re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"),
)
FALSE_CONTEXT_MARKERS = (
    "你现在",
    "你今天",
    "你是不是",
    "你有没有",
    "你看起来",
    "看你",
    "你又",
    "你的杯子",
    "你还在",
    "你已经",
    "你刚",
)
INTIMACY_MARKERS = ("小笨蛋", "宝宝", "亲爱的", "只准", "不许离开", "永远陪我")
QUESTION_MARKS = ("?", "？")
LEGACY_REFERENCE = re.compile(r"legacy:(\d+);topic:([^;]+)")

TONE_ALIASES = {
    "dry_warm": "dry",
    "soft_warm": "gentle",
    "gentle_cautious": "gentle",
    "soft_playful": "playful",
    "playful_rare": "playful",
}
CATEGORY_GROUP_OVERRIDES = {
    "Career": "career",
    "Study": "growth",
    "EnglishPractice": "growth",
}
SOURCE_KIND_ALIASES = {
    "topic_rewrite": "rewritten_topic",
    "curated_authored": "curated_standalone",
    "legacy_standalone": "preserved_easter_egg",
}
LEGACY_SOURCE_KINDS = frozenset(("topic_rewrite", "legacy_standalone"))


def _runtime_trigger(entry: CatalogEntry) -> str:
    """Translate editorial trigger labels to the public v2 trigger contract."""
    if entry.trigger == "idle":
        return "any"
    if entry.trigger == "time_event":
        daypart = entry.variant_id.split(".", 2)[1]
        return "morning" if daypart == "dawn" else daypart
    if entry.trigger == "date_event":
        return "weekend" if ".weekend." in entry.variant_id else "day_changed"
    if entry.trigger == "season_event":
        return "day_changed"
    if entry.trigger == "holiday_event":
        return "holiday"
    return entry.trigger


@dataclass(frozen=True, slots=True)
class BuildResult:
    enabled: tuple[CorpusLine, ...]
    archive: tuple[ArchiveRow, ...]
    review: tuple[ReviewRow, ...]
    pii_review: tuple[PiiReviewRow, ...]
    dispositions: Mapping[int, tuple[str, ...]]


def _stable_digest(*parts: object, length: int = 12) -> str:
    identity = "\0".join(str(part) for part in parts)
    return hashlib.sha256(identity.encode("utf-8")).hexdigest()[:length]


def _slug(value: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "_", value.casefold()).strip("_")
    return slug or "line"


def catalog_line_id(entry: CatalogEntry) -> str:
    """Return a stable line ID derived only from immutable catalog identity."""
    return f"v2_{_slug(entry.variant_id)}_{_stable_digest(entry.variant_id)}"


def _looks_like_pii(text: str) -> bool:
    return any(marker in text for marker in PII_MARKERS) or any(
        pattern.search(text) for pattern in PII_PATTERNS
    )


def _pii_type(text: str) -> str:
    if any(marker in text for marker in ("雷琳玥", "小玥", "姓名", "名字")):
        return "person_name"
    if any(marker in text for marker in ("湖南", "长沙", "广东", "住在", "地址")):
        return "location_or_history"
    if any(marker in text for marker in ("工资", "收入", "月薪", "打零工", "换工作")):
        return "income_or_employment"
    return "direct_identifier"


def _review_risks(text: str) -> tuple[tuple[str, str], ...]:
    """Return every independent risk; one source line may require several reviews."""
    risks: list[tuple[str, str]] = []
    if _looks_like_pii(text):
        risks.append(
            (
                "privacy_risk",
                "疑似包含姓名、地点、收入、工作经历或直接标识，角色设定真实性尚未人工确认。",
            )
        )
    if any(marker in text for marker in INTIMACY_MARKERS):
        risks.append(("uncertain_intimacy", "亲密程度可能超出默认关系边界。"))
    if any(marker in text for marker in FALSE_CONTEXT_MARKERS):
        risks.append(("future_context_signal", "句子断言了当前无法可靠获得的用户状态。"))
    return tuple(risks)


def _archive_reason(line: LegacyLine, mapping: SourceMapping) -> str:
    text = line.text
    if line.category.casefold() == "proactivechat" or any(mark in text for mark in QUESTION_MARKS):
        return "requires_user_reply"
    if _looks_like_pii(text):
        return "privacy_risk"
    if any(marker in text for marker in FALSE_CONTEXT_MARKERS):
        return "fake_context"
    if any(marker in text for marker in INTIMACY_MARKERS):
        return "manual_review"
    if len(text) > 64:
        return "too_long"
    if line.category.casefold() == "emotionalsupport" and any(
        marker in text for marker in ("我知道你", "你肯定", "你一定")
    ):
        return "unsafe_emotional_claim"
    if any(text.startswith(marker) for marker in ("别", "赶紧", "必须", "给我")):
        return "overly_commanding"
    if mapping.prefix_id or mapping.suffix_id:
        return "cartesian_duplicate"
    return "low_information"


def _validate_source_and_mappings(
    source: Sequence[LegacyLine], mappings: Sequence[SourceMapping]
) -> dict[int, SourceMapping]:
    source_by_line: dict[int, LegacyLine] = {}
    for line in source:
        if line.source_line in source_by_line:
            raise ValueError(f"duplicate source line {line.source_line}")
        source_by_line[line.source_line] = line

    mapping_by_line: dict[int, SourceMapping] = {}
    for mapping in mappings:
        if mapping.source_line in mapping_by_line:
            raise ValueError(f"duplicate mapping for source line {mapping.source_line}")
        mapping_by_line[mapping.source_line] = mapping

    if set(source_by_line) != set(mapping_by_line):
        missing = sorted(set(source_by_line) - set(mapping_by_line))
        extra = sorted(set(mapping_by_line) - set(source_by_line))
        detail = []
        if missing:
            detail.append(f"missing mappings {missing[:5]}")
        if extra:
            detail.append(f"unknown mappings {extra[:5]}")
        raise ValueError("source mapping coverage mismatch: " + "; ".join(detail))

    for source_line, line in source_by_line.items():
        mapping = mapping_by_line[source_line]
        if mapping.category != line.category or mapping.original_text != line.text:
            raise ValueError(f"source line {source_line} mapping does not match category/text")
    return mapping_by_line


def _catalog_reference(
    entry: CatalogEntry, mapping_by_line: Mapping[int, SourceMapping]
) -> tuple[str, str]:
    reference = entry.source_reference
    legacy = LEGACY_REFERENCE.fullmatch(reference)
    if legacy is not None:
        source_line = int(legacy.group(1))
        source_topic = legacy.group(2)
        mapping = mapping_by_line.get(source_line)
        if mapping is None:
            raise ValueError(
                f"catalog variant {entry.variant_id!r} references unknown source line {source_line}"
            )
        if mapping.category != entry.category or mapping.topic_id != source_topic:
            raise ValueError(
                f"catalog variant {entry.variant_id!r} source category/topic mismatch"
            )
        if entry.runtime_topic_id != source_topic:
            raise ValueError(
                f"catalog variant {entry.variant_id!r} runtime topic does not match lineage"
            )
        if entry.source_kind not in LEGACY_SOURCE_KINDS:
            raise ValueError(
                f"catalog variant {entry.variant_id!r} uses legacy lineage with {entry.source_kind!r}"
            )
        return source_topic, f"{reference};variant:{entry.variant_id}"

    if reference.startswith("catalog:"):
        if entry.source_kind in LEGACY_SOURCE_KINDS:
            raise ValueError(
                f"catalog variant {entry.variant_id!r} requires a verified legacy source"
            )
        return entry.runtime_topic_id, f"{reference};variant:{entry.variant_id}"

    raise ValueError(
        f"catalog variant {entry.variant_id!r} has invalid source_reference {reference!r}"
    )


def _catalog_to_corpus(
    entry: CatalogEntry, topic_id: str, source_reference: str
) -> CorpusLine:
    if any(mark in entry.text for mark in QUESTION_MARKS):
        raise ValueError(f"enabled catalog entry contains a question mark: {entry.text!r}")
    if _looks_like_pii(entry.text):
        raise ValueError(f"enabled catalog entry contains PII risk: {entry.text!r}")
    if "\t" in entry.text or "\r" in entry.text or "\n" in entry.text:
        raise ValueError("catalog text must fit one physical TSV field")
    return CorpusLine(
        id=catalog_line_id(entry),
        category=entry.category,
        category_group=CATEGORY_GROUP_OVERRIDES.get(entry.category, entry.category_group),
        topic_id=topic_id,
        semantic_group=entry.semantic_group,
        output_mode=(
            "system_observe" if entry.category_group == "system_ambient" else entry.output_mode
        ),
        trigger=_runtime_trigger(entry),
        required_context=entry.required_context,
        tone=TONE_ALIASES.get(entry.tone, entry.tone),
        interrupt_cost=entry.interrupt_cost,
        cooldown_hours=entry.cooldown_hours,
        semantic_cooldown_hours=max(entry.cooldown_hours, entry.semantic_cooldown_hours),
        max_per_day=entry.max_per_day,
        weight=entry.weight,
        requires_reply=False,
        enabled=True,
        text=entry.text,
        source_kind=(
            "new_ambient"
            if entry.category_group == "system_ambient"
            else SOURCE_KIND_ALIASES.get(entry.source_kind, entry.source_kind)
        ),
        source_reference=source_reference,
        rewrite_reason=entry.rewrite_reason,
    )


def build_v2(
    source: Sequence[LegacyLine],
    mappings: Sequence[SourceMapping],
    seed: int,
    pii_policy: str = "review",
    *,
    catalog: Sequence[CatalogEntry] | None = None,
) -> BuildResult:
    """Create a deterministic, curated one-way corpus plus complete dispositions."""
    if pii_policy != "review":
        raise ValueError("pii_policy must be 'review'")
    mapping_by_line = _validate_source_and_mappings(source, mappings)
    catalog_entries = tuple(CONTENT_CATALOG if catalog is None else catalog)
    variants = [entry.variant_id for entry in catalog_entries]
    if len(variants) != len(set(variants)):
        raise ValueError("content catalog contains duplicate immutable variant IDs")
    if any(not entry.runtime_topic_id for entry in catalog_entries):
        raise ValueError("content catalog contains an empty runtime topic ID")
    if any(not entry.editorial_role for entry in catalog_entries):
        raise ValueError("content catalog contains an empty editorial role")

    legacy_roles: dict[tuple[str, str], set[str]] = defaultdict(set)
    for entry in catalog_entries:
        legacy = LEGACY_REFERENCE.fullmatch(entry.source_reference)
        if legacy is None:
            continue
        key = (entry.category, entry.runtime_topic_id)
        if entry.editorial_role in legacy_roles[key]:
            raise ValueError(
                f"legacy runtime topic {key!r} reuses editorial role "
                f"{entry.editorial_role!r}"
            )
        legacy_roles[key].add(entry.editorial_role)

    enabled: list[CorpusLine] = []
    for entry in catalog_entries:
        topic_id, source_reference = _catalog_reference(entry, mapping_by_line)
        enabled.append(_catalog_to_corpus(entry, topic_id, source_reference))

    normalized = [normalize_text(row.text) for row in enabled]
    if len(normalized) != len(set(normalized)):
        raise ValueError("content catalog contains normalized duplicate enabled text")
    if len(enabled) != len({row.id for row in enabled}):
        raise ValueError("content catalog produced duplicate stable IDs")

    suggestions_by_source: dict[int, set[str]] = defaultdict(set)
    for row in enabled:
        legacy = re.match(r"legacy:(\d+);", row.source_reference)
        if legacy is not None:
            suggestions_by_source[int(legacy.group(1))].add(row.text)

    enabled.sort(
        key=lambda row: (
            hashlib.sha256(f"{seed}\0{row.id}".encode("utf-8")).hexdigest(),
            row.id,
        )
    )

    archive: list[ArchiveRow] = []
    review: list[ReviewRow] = []
    pii_review: list[PiiReviewRow] = []
    dispositions: dict[int, list[str]] = defaultdict(list)
    for line in sorted(source, key=lambda item: item.source_line):
        mapping = mapping_by_line[line.source_line]
        reason = _archive_reason(line, mapping)
        source_suggestions = sorted(suggestions_by_source.get(line.source_line, ()))
        suggested = source_suggestions[0] if source_suggestions else ""
        archive.append(
            ArchiveRow(
                source_line=line.source_line,
                category=line.category,
                original_text=line.text,
                archive_reason=reason,
                topic_id=mapping.topic_id,
                suggested_rewrite=suggested,
                can_recover=bool(source_suggestions),
            )
        )
        dispositions[line.source_line].append(f"archive:{reason}")

        for risk_type, description in _review_risks(line.text):
            review_id = (
                f"review_{line.source_line}_"
                f"{_stable_digest(line.category, line.text, risk_type)}"
            )
            review.append(
                ReviewRow(
                    review_id=review_id,
                    source_line=line.source_line,
                    category=line.category,
                    original_text=line.text,
                    risk_type=risk_type,
                    risk_description=description,
                    suggested_action="人工确认后改写；默认不得进入运行语料。",
                    suggested_rewrite=suggested,
                    default_enabled=False,
                )
            )
            dispositions[line.source_line].append(f"review:{risk_type}")
            if risk_type == "privacy_risk":
                pii_review.append(
                    PiiReviewRow(
                        review_id=f"pii_{line.source_line}_{_stable_digest(line.text)}",
                        source_line=line.source_line,
                        category=line.category,
                        original_text=line.text,
                        pii_type=_pii_type(line.text),
                        risk_description=description,
                        suggested_action="确认角色设定为虚构且获授权前保持禁用。",
                        suggested_rewrite=suggested,
                        default_enabled=False,
                    )
                )
                dispositions[line.source_line].append("pii_review")

    frozen_dispositions = MappingProxyType(
        {line: tuple(values) for line, values in sorted(dispositions.items())}
    )
    return BuildResult(
        enabled=tuple(enabled),
        archive=tuple(archive),
        review=tuple(review),
        pii_review=tuple(pii_review),
        dispositions=frozen_dispositions,
    )


def load_source_mappings(path: Path) -> list[SourceMapping]:
    path = Path(path)
    result: list[SourceMapping] = []
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.reader(stream, delimiter="\t", strict=True)
        try:
            header = next(reader)
        except StopIteration as error:
            raise ValueError(f"{path}: line 1: missing source mapping header") from error
        if tuple(header) != SOURCE_MAPPING_HEADER:
            raise ValueError(f"{path}: line 1: unexpected source mapping header")
        for row in reader:
            line_number = reader.line_num
            if len(row) != len(SOURCE_MAPPING_HEADER):
                raise ValueError(
                    f"{path}: line {line_number}: expected {len(SOURCE_MAPPING_HEADER)} columns"
                )
            values = dict(zip(SOURCE_MAPPING_HEADER, row, strict=True))
            try:
                result.append(
                    SourceMapping(
                        source_line=int(values["source_line"]),
                        category=values["category"],
                        original_text=values["original_text"],
                        prefix_id=values["prefix_id"],
                        topic_id=values["topic_id"],
                        suffix_id=values["suffix_id"],
                        extraction_confidence=float(values["extraction_confidence"]),
                    )
                )
            except ValueError as error:
                raise ValueError(f"{path}: line {line_number}: {error}") from error
    return result


T = TypeVar("T")


def _serialize(header: tuple[str, ...], rows: Iterable[T]) -> bytes:
    lines = ["\t".join(header)]
    for row in rows:
        values: list[str] = []
        for field in fields(row):
            value = getattr(row, field.name)
            if isinstance(value, bool):
                rendered = "true" if value else "false"
            elif isinstance(value, float):
                rendered = format(value, ".12g")
            else:
                rendered = str(value)
            if "\t" in rendered or "\r" in rendered or "\n" in rendered:
                raise ValueError(f"{field.name} contains a TSV-breaking character")
            values.append(rendered)
        lines.append("\t".join(values))
    return ("\n".join(lines) + "\n").encode("utf-8")


def serialize_v2(rows: Iterable[CorpusLine]) -> bytes:
    return _serialize(V2_HEADER, rows)


def serialize_archive(rows: Iterable[ArchiveRow]) -> bytes:
    return _serialize(ARCHIVE_HEADER, rows)


def serialize_review(rows: Iterable[ReviewRow]) -> bytes:
    return _serialize(REVIEW_HEADER, rows)


def serialize_pii_review(rows: Iterable[PiiReviewRow]) -> bytes:
    return _serialize(PII_REVIEW_HEADER, rows)


def _canonical_output_root(output: Path) -> Path | None:
    if output.parent.name == "optimized" and output.parent.parent.name == "data":
        return output.parent.parent.parent
    return None


def _derived_report_output(output: Path) -> Path:
    root = _canonical_output_root(output)
    if root is None:
        raise ValueError(
            "report_output is required unless output is inside a canonical data/optimized directory"
        )
    return root / "reports" / "pii-review.tsv"


def _is_contained(path: Path, root: Path) -> bool:
    try:
        path.resolve(strict=False).relative_to(root.resolve(strict=False))
    except ValueError:
        return False
    return True


def _validated_output_paths(
    output: Path, report_output: Path | None
) -> dict[str, Path]:
    canonical_root = _canonical_output_root(output)
    if report_output is None:
        pii_review = _derived_report_output(output)
    else:
        pii_review = Path(report_output)
        containment_root = canonical_root or output.parent
        if not _is_contained(pii_review, containment_root):
            raise ValueError(
                "report_output must be contained under the canonical root or output directory"
            )

    paths = {
        "v2": output,
        "archive": output.with_name("persona-corpus-archive.tsv"),
        "review": output.with_name("persona-corpus-review.tsv"),
        "pii_review": pii_review,
    }
    normalized = {path.resolve(strict=False) for path in paths.values()}
    if len(normalized) != len(paths):
        raise ValueError("all build output paths must be distinct")
    return paths


def write_build_outputs(
    result: BuildResult,
    output: Path,
    *,
    report_output: Path | None = None,
) -> dict[str, Path]:
    output = Path(output)
    paths = _validated_output_paths(output, report_output)
    output.parent.mkdir(parents=True, exist_ok=True)
    paths["pii_review"].parent.mkdir(parents=True, exist_ok=True)
    payloads = {
        "v2": serialize_v2(result.enabled),
        "archive": serialize_archive(result.archive),
        "review": serialize_review(result.review),
        "pii_review": serialize_pii_review(result.pii_review),
    }
    for name, path in paths.items():
        path.write_bytes(payloads[name])
    return paths
