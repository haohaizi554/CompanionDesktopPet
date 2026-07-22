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


def _runtime_trigger(entry: CatalogEntry) -> str:
    """Translate editorial trigger labels to the public v2 trigger contract."""
    if entry.trigger == "idle":
        return "any"
    if entry.trigger == "time_event":
        daypart = entry.topic_id.split(".", 2)[1]
        return "morning" if daypart == "dawn" else daypart
    if entry.trigger == "date_event":
        return "weekend" if ".weekend." in entry.topic_id else "day_changed"
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


def _looks_like_pii(text: str) -> bool:
    return any(marker in text for marker in PII_MARKERS) or any(
        pattern.search(text) for pattern in PII_PATTERNS
    )


def _pii_type(text: str) -> str:
    if "雷琳玥" in text or "小玥" in text or "姓名" in text or "名字" in text:
        return "person_name"
    if any(marker in text for marker in ("湖南", "长沙", "广东", "住在", "地址")):
        return "location_or_history"
    if any(marker in text for marker in ("工资", "收入", "月薪", "打零工", "换工作")):
        return "income_or_employment"
    return "direct_identifier"


def _review_risk(text: str) -> tuple[str, str] | None:
    if _looks_like_pii(text):
        return (
            "privacy_risk",
            "疑似包含姓名、地点、收入、工作经历或直接标识，角色设定真实性尚未人工确认。",
        )
    if any(marker in text for marker in INTIMACY_MARKERS):
        return ("uncertain_intimacy", "亲密程度可能超出默认关系边界。")
    if any(marker in text for marker in FALSE_CONTEXT_MARKERS):
        return ("future_context_signal", "句子断言了当前无法可靠获得的用户状态。")
    return None


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
            raise ValueError(
                f"source line {source_line} mapping does not match category/text"
            )
    return mapping_by_line


def _catalog_reference(
    entry: CatalogEntry,
    sources_by_category: Mapping[str, Sequence[SourceMapping]],
    category_offsets: dict[str, int],
) -> tuple[str, str]:
    candidates = sources_by_category.get(entry.category, ())
    if not candidates:
        return (
            entry.topic_id,
            f"catalog:{entry.source_reference_hint or entry.topic_id}",
        )
    offset = category_offsets[entry.category]
    mapping = candidates[offset % len(candidates)]
    category_offsets[entry.category] = offset + 1
    return mapping.topic_id, f"legacy:{mapping.source_line};topic:{mapping.topic_id}"


def _catalog_to_corpus(
    entry: CatalogEntry, topic_id: str, source_reference: str
) -> CorpusLine:
    if any(mark in entry.text for mark in QUESTION_MARKS):
        raise ValueError(f"enabled catalog entry contains a question mark: {entry.text!r}")
    if _looks_like_pii(entry.text):
        raise ValueError(f"enabled catalog entry contains PII risk: {entry.text!r}")
    if "\t" in entry.text or "\r" in entry.text or "\n" in entry.text:
        raise ValueError("catalog text must fit one physical TSV field")
    line_id = (
        f"v2_{_slug(entry.semantic_group)}_"
        f"{_stable_digest(entry.category, entry.semantic_group, entry.text)}"
    )
    return CorpusLine(
        id=line_id,
        category=entry.category,
        category_group=CATEGORY_GROUP_OVERRIDES.get(
            entry.category, entry.category_group
        ),
        topic_id=topic_id,
        semantic_group=entry.semantic_group,
        output_mode=(
            "system_observe"
            if entry.category_group == "system_ambient"
            else entry.output_mode
        ),
        trigger=_runtime_trigger(entry),
        required_context="none",
        tone=TONE_ALIASES.get(entry.tone, entry.tone),
        interrupt_cost=entry.interrupt_cost,
        cooldown_hours=entry.cooldown_hours,
        semantic_cooldown_hours=max(
            entry.cooldown_hours, entry.semantic_cooldown_hours
        ),
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
) -> BuildResult:
    """Create a deterministic, curated one-way corpus plus complete dispositions."""
    if pii_policy != "review":
        raise ValueError("pii_policy must be 'review'")
    mapping_by_line = _validate_source_and_mappings(source, mappings)

    mappings_by_category: dict[str, list[SourceMapping]] = defaultdict(list)
    for mapping in mappings:
        mappings_by_category[mapping.category].append(mapping)
    for category in mappings_by_category:
        mappings_by_category[category].sort(key=lambda item: item.source_line)

    category_offsets: dict[str, int] = defaultdict(int)
    enabled: list[CorpusLine] = []
    for entry in CONTENT_CATALOG:
        topic_id, source_reference = _catalog_reference(
            entry, mappings_by_category, category_offsets
        )
        enabled.append(_catalog_to_corpus(entry, topic_id, source_reference))

    enabled.sort(
        key=lambda row: (
            hashlib.sha256(f"{seed}\0{row.id}".encode("utf-8")).hexdigest(),
            row.id,
        )
    )
    normalized = [normalize_text(row.text) for row in enabled]
    if len(normalized) != len(set(normalized)):
        raise ValueError("content catalog contains normalized duplicate enabled text")
    if len(enabled) != len({row.id for row in enabled}):
        raise ValueError("content catalog produced duplicate stable IDs")

    suggestions: dict[str, str] = {}
    for row in enabled:
        suggestions.setdefault(row.category, row.text)

    archive: list[ArchiveRow] = []
    review: list[ReviewRow] = []
    pii_review: list[PiiReviewRow] = []
    dispositions: dict[int, list[str]] = defaultdict(list)
    for line in sorted(source, key=lambda item: item.source_line):
        mapping = mapping_by_line[line.source_line]
        reason = _archive_reason(line, mapping)
        suggested = suggestions.get(line.category, "")
        archive.append(
            ArchiveRow(
                source_line=line.source_line,
                category=line.category,
                original_text=line.text,
                archive_reason=reason,
                topic_id=mapping.topic_id,
                suggested_rewrite=suggested,
                can_recover=bool(suggested),
            )
        )
        dispositions[line.source_line].append(f"archive:{reason}")

        risk = _review_risk(line.text)
        if risk is None:
            continue
        risk_type, description = risk
        review_id = f"review_{line.source_line}_{_stable_digest(line.category, line.text)}"
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


def write_build_outputs(result: BuildResult, output: Path) -> dict[str, Path]:
    output = Path(output)
    output.parent.mkdir(parents=True, exist_ok=True)
    archive = output.with_name("persona-corpus-archive.tsv")
    review = output.with_name("persona-corpus-review.tsv")
    pii_review = output.parents[1] / "reports" / "pii-review.tsv"
    if output.parent.name == "optimized":
        pii_review = output.parent.parent.parent / "reports" / "pii-review.tsv"
    pii_review.parent.mkdir(parents=True, exist_ok=True)
    paths = {
        "v2": output,
        "archive": archive,
        "review": review,
        "pii_review": pii_review,
    }
    payloads = {
        "v2": serialize_v2(result.enabled),
        "archive": serialize_archive(result.archive),
        "review": serialize_review(result.review),
        "pii_review": serialize_pii_review(result.pii_review),
    }
    for name, path in paths.items():
        path.write_bytes(payloads[name])
    return paths
