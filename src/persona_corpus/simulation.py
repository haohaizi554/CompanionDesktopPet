from __future__ import annotations

import hashlib
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path
from typing import Iterable, Mapping, Sequence

from .builder import serialize_v2
from .contract import RELEASE_INVENTORY
from .history import SelectionHistory
from .identity_session import IdentitySessionExposure
from .lexical import contains_seasoning_marker
from .loader import CorpusFormatError, load_legacy
from .models import CorpusLine, LegacyLine
from .normalization import normalize_text
from .schema import ARCHIVE_HEADER, PII_REVIEW_HEADER, REVIEW_HEADER
from .selector import (
    SchedulerConfig,
    SelectorConfigError,
    prepare_corpus,
    select_line,
)
from .simulation_core.constraints import analyze_constraints, run_adversarial_suite
from .simulation_core.metrics import (
    DistributionTolerance,
    derive_distribution_policy,
    derive_dry_sharp_policy,
    derive_lexical_exposure_policy,
)
from .simulation_core.report import (
    SeedMetrics,
    SimulationAttempt,
    SimulationReport,
    analyze_simulation,
    combine_hard_violations,
    render_simulation_report,
)
from .simulation_core.scenarios import (
    ATTEMPT_SLOTS,
    SIMULATION_SCHEMA_VERSION,
    SUBSEED_DERIVATION_SHA256,
    SUBSEED_DERIVATION_SPEC,
    SUBSEED_DERIVATION_VERSION,
    CandidateIndex,
    build_scenario_coverage,
    build_natural_attempt,
    derive_subseed,
    probe_inventory_coverage,
    summarize_scenario_coverage,
)
from .validation import (
    DIRECT_STATE_PATTERNS,
    TECHNICAL_CURRENT_PATTERNS,
    scheduler_config_sha256,
)


CATEGORY_GROUPS = (
    "technical",
    "growth",
    "career",
    "daily_care",
    "emotional_reflection",
    "character_life",
    "easter_egg",
    "system_ambient",
)
OUTPUT_MODES = ("self_talk", "ambient", "user_direct", "system_observe")
LENGTH_BUCKETS = ("<8", "8-16", "17-24", "25-36", ">36")
PREFIX_WIDTHS = (2, 3, 4, 5, 6)
SUFFIX_WIDTHS = (4, 6, 8, 10)
_EPSILON = 1e-9

_TONE_MARKERS = (
    "哈？",
    "我丢",
    "我靠",
    "啊推",
    "小笨蛋",
    "我真的不想多说什么了",
    "别一上来",
    "赶紧",
    "必须",
)


class SimulationError(ValueError):
    """Simulation input or a required deterministic report artifact is invalid."""


@dataclass(frozen=True, slots=True)
class EditorialReportSummary:
    authored_runtime_rows: int
    authored_trace_examples: int
    disabled_examples: int
    relationship_profile_examples: int
    authored_batch_count: int
    manual_review_items: int


def _scheduler_mapping(config: SchedulerConfig) -> dict[str, object]:
    return {
        "schema_version": config.schema_version,
        "category_group_weights": dict(config.category_group_weights),
        "output_mode_targets": dict(config.output_mode_targets),
        "runtime_limits": {
            "minimum_interval_minutes": config.minimum_interval_minutes,
            "max_outputs_per_hour": config.max_outputs_per_hour,
            "late_night_max_outputs_per_hour": config.late_night_max_outputs_per_hour,
            "semantic_group_no_repeat": config.semantic_group_no_repeat,
            "block_adjacent_category_groups": sorted(config.block_adjacent_category_groups),
            "technical_recent_window": config.technical_recent_window,
            "technical_recent_max": config.technical_recent_max,
            "user_direct_recent_window": config.user_direct_recent_window,
            "user_direct_recent_max": config.user_direct_recent_max,
            "easter_egg_recent_window": config.easter_egg_recent_window,
            "easter_egg_recent_max": config.easter_egg_recent_max,
            "long_silence_minutes": config.long_silence_minutes,
            "interrupt_cost_minimum_intervals_minutes": {
                str(cost): minutes
                for cost, minutes in sorted(
                    config.interrupt_cost_minimum_intervals_minutes.items()
                )
            },
        },
        "context_tokens": sorted(config.context_tokens),
        "mvp_triggers": sorted(config.mvp_triggers),
        "future_triggers": sorted(config.future_triggers),
    }


def _coerce_config(
    value: SchedulerConfig | Mapping[str, object],
) -> tuple[SchedulerConfig, Mapping[str, object]]:
    try:
        if isinstance(value, SchedulerConfig):
            mapping = _scheduler_mapping(value)
            return SchedulerConfig.from_mapping(mapping), mapping
        if isinstance(value, Mapping):
            return SchedulerConfig.from_mapping(value), value
    except (SelectorConfigError, TypeError, ValueError) as error:
        raise SimulationError(f"invalid scheduler config: {error}") from error
    raise SimulationError("config must be a SchedulerConfig or mapping")


def _canonical_seeds(seeds: Sequence[int]) -> tuple[int, ...]:
    try:
        values = tuple(seeds)
    except TypeError as error:
        raise SimulationError("seeds must be a finite sequence of integers") from error
    if not values:
        raise SimulationError("seeds must not be empty")
    if any(type(seed) is not int for seed in values):
        raise SimulationError("each seed must be an exact integer")
    if len(values) != len(set(values)):
        raise SimulationError("seeds must be distinct")
    return tuple(sorted(values))


def _ratio(counts: Mapping[str, int], keys: Sequence[str], total: int) -> dict[str, float]:
    if total <= 0:
        return {key: 0.0 for key in keys}
    return {key: counts.get(key, 0) / total for key in keys}


def _length_bucket(length: int) -> str:
    if length < 8:
        return "<8"
    if length <= 16:
        return "8-16"
    if length <= 24:
        return "17-24"
    if length <= 36:
        return "25-36"
    return ">36"


def simulate(
    corpus: Sequence[CorpusLine],
    config: SchedulerConfig | Mapping[str, object],
    days: int,
    seeds: Sequence[int],
    *,
    distribution_tolerance: DistributionTolerance = DistributionTolerance(),
) -> SimulationReport:
    """Run the real selector over a deterministic synthetic local-time event stream."""

    if type(days) is not int or days <= 0:
        raise SimulationError("days must be a positive exact integer")
    canonical_seeds = _canonical_seeds(seeds)
    rows = tuple(corpus)
    if any(not isinstance(row, CorpusLine) for row in rows):
        raise SimulationError("corpus must contain only CorpusLine values")
    scheduler, config_mapping = _coerce_config(config)
    try:
        corpus_payload = serialize_v2(rows)
        corpus_digest = hashlib.sha256(corpus_payload).hexdigest()
        config_digest = scheduler_config_sha256(config_mapping)
    except (TypeError, ValueError) as error:
        raise SimulationError(f"inputs cannot be hashed deterministically: {error}") from error

    prepared_corpus = prepare_corpus(rows)
    inventory_coverage = probe_inventory_coverage(rows, scheduler)
    adversarial_result = run_adversarial_suite(scheduler)

    attempts: list[SimulationAttempt] = []
    for seed in canonical_seeds:
        history = SelectionHistory()
        identity_session = IdentitySessionExposure()
        last_output_at: datetime | None = None
        for day_index in range(days):
            for slot_index in range(len(ATTEMPT_SLOTS)):
                natural_attempt = build_natural_attempt(
                    seed=seed,
                    day_index=day_index,
                    slot_index=slot_index,
                    scheduler_config=scheduler,
                    last_output_at=last_output_at,
                )
                selected = select_line(
                    prepared_corpus,
                    natural_attempt.context,
                    history,
                    natural_attempt.attempted_at,
                    seed=derive_subseed(
                        seed=seed,
                        day_index=day_index,
                        slot_index=slot_index,
                        corpus_sha256=corpus_digest,
                        scheduler_config_sha256=config_digest,
                        scenario=natural_attempt.scenario,
                    ),
                    scheduler_config=scheduler,
                    identity_session=identity_session,
                )
                row = selected.row if selected is not None else None
                if row is not None:
                    last_output_at = natural_attempt.attempted_at
                attempts.append(
                    SimulationAttempt(
                        seed=seed,
                        day_index=day_index,
                        slot_index=slot_index,
                        attempted_at=natural_attempt.attempted_at,
                        context=natural_attempt.context,
                        row=row,
                    )
                )

    scenario_coverage = summarize_scenario_coverage(attempts)
    enabled_rows = tuple(row for row in rows if row.enabled)
    scene_tones = {
        row.semantic_group: row.tone
        for row in enabled_rows
        if row.semantic_group
    }
    return analyze_simulation(
        corpus_sha256=corpus_digest,
        enabled_corpus_count=len(enabled_rows),
        enabled_scene_count=len(scene_tones),
        dry_sharp_scene_count=sum(
            tone == "dry_sharp" for tone in scene_tones.values()
        ),
        dry_sharp_row_count=sum(row.tone == "dry_sharp" for row in enabled_rows),
        seasoning_inventory_count=sum(
            contains_seasoning_marker(row.text) for row in enabled_rows
        ),
        config_sha256=config_digest,
        config=scheduler,
        days=days,
        seeds=canonical_seeds,
        attempts=tuple(attempts),
        distribution_tolerance=distribution_tolerance,
        scenario_coverage=scenario_coverage,
        inventory_coverage=inventory_coverage,
        adversarial_result=adversarial_result,
    )


def _percent(value: float) -> str:
    return f"{value * 100:.2f}%"


def _markdown_table(headers: Sequence[str], rows: Iterable[Sequence[object]]) -> list[str]:
    def escaped(value: object) -> str:
        text = str(value).replace("\r", " ").replace("\n", " ")
        return text.replace("|", "\\|")

    rendered = ["| " + " | ".join(escaped(header) for header in headers) + " |"]
    rendered.append("| " + " | ".join("---" for _ in headers) + " |")
    rendered.extend(
        "| " + " | ".join(escaped(value) for value in row) + " |" for row in rows
    )
    return rendered


def _final_markdown(lines: Sequence[str]) -> str:
    return "\n".join(lines).rstrip() + "\n"


def _read_tsv(path: Path, expected_header: Sequence[str]) -> list[dict[str, str]]:
    path = Path(path)
    try:
        payload = path.read_bytes()
    except OSError as error:
        raise SimulationError(f"{path}: line 1: cannot read: {error}") from error
    physical_lines = payload.split(b"\n")
    if physical_lines and physical_lines[-1] == b"":
        physical_lines.pop()
    if not physical_lines:
        raise SimulationError(f"{path}: line 1: missing TSV header")
    decoded: list[list[str]] = []
    for line_number, raw in enumerate(physical_lines, start=1):
        if raw.endswith(b"\r"):
            raw = raw[:-1]
        try:
            text = raw.decode("utf-8-sig" if line_number == 1 else "utf-8")
        except UnicodeDecodeError as error:
            raise SimulationError(f"{path}: line {line_number}: invalid UTF-8: {error}") from error
        row = text.split("\t")
        decoded.append(row)
    if tuple(decoded[0]) != tuple(expected_header):
        raise SimulationError(f"{path}: line 1: unexpected TSV header")
    rows: list[dict[str, str]] = []
    for line_number, values in enumerate(decoded[1:], start=2):
        if len(values) != len(expected_header):
            raise SimulationError(
                f"{path}: line {line_number}: expected {len(expected_header)} columns, found {len(values)}"
            )
        record = dict(zip(expected_header, values, strict=True))
        if "source_line" in record:
            try:
                source_line = int(record["source_line"])
            except ValueError as error:
                raise SimulationError(
                    f"{path}: line {line_number}: source_line must be an integer"
                ) from error
            if source_line <= 0:
                raise SimulationError(
                    f"{path}: line {line_number}: source_line must be positive"
                )
        rows.append(record)
    return rows


def _opening_counts(texts: Sequence[str], width: int, limit: int = 5) -> list[tuple[str, int]]:
    counts = Counter(text[:width] for text in texts if len(text) >= width)
    return sorted(counts.items(), key=lambda item: (-item[1], item[0]))[:limit]


def _ending_counts(texts: Sequence[str], width: int, limit: int = 5) -> list[tuple[str, int]]:
    counts = Counter(text[-width:] for text in texts if len(text) >= width)
    return sorted(counts.items(), key=lambda item: (-item[1], item[0]))[:limit]


def _inventory_metrics(lines: Sequence[CorpusLine]) -> dict[str, object]:
    enabled = [row for row in lines if row.enabled]
    texts = [row.text for row in enabled]
    count = len(texts)
    normalized = Counter(normalize_text(text) for text in texts)
    exact = Counter(texts)
    modes = Counter(row.output_mode for row in enabled)
    groups = Counter(row.category_group for row in enabled)
    length_counts = Counter(_length_bucket(len(text)) for text in texts)
    catchphrase_lines = sum(contains_seasoning_marker(text) for text in texts)
    return {
        "count": count,
        "exact_duplicates": sum(value - 1 for value in exact.values() if value > 1),
        "normalized_duplicates": sum(
            value - 1 for value in normalized.values() if value > 1
        ),
        "average_length": sum(map(len, texts)) / count if count else 0.0,
        "length_ratio": _ratio(length_counts, LENGTH_BUCKETS, count),
        "questions": sum("?" in text or "？" in text for text in texts),
        "fake_context": sum(
            (
                row.required_context == "none"
                and any(marker in row.text for marker in DIRECT_STATE_PATTERNS)
            )
            or (
                row.category_group == "technical"
                and row.required_context == "none"
                and any(
                    marker.casefold() in row.text.casefold()
                    for marker in TECHNICAL_CURRENT_PATTERNS
                )
            )
            for row in enabled
        ),
        "mode_ratio": _ratio(modes, OUTPUT_MODES, count),
        "technical_ratio": groups["technical"] / count if count else 0.0,
        "catchphrase_ratio": catchphrase_lines / count if count else 0.0,
        "openings": _opening_counts(texts, 2),
        "endings": _ending_counts(texts, 4),
    }


def _legacy_metrics(lines: Sequence[LegacyLine]) -> dict[str, object]:
    texts = [line.text for line in lines]
    count = len(texts)
    exact = Counter(texts)
    normalized = Counter(normalize_text(text) for text in texts)
    length_counts = Counter(_length_bucket(len(text)) for text in texts)
    catchphrase_lines = sum(contains_seasoning_marker(text) for text in texts)
    technical_categories = {
        "Debugging",
        "Python",
        "Java",
        "Cpp",
        "Frontend",
        "Backend",
        "Database",
        "Algorithms",
        "Systems",
        "Networks",
        "GitDevOps",
        "Architecture",
    }
    return {
        "count": count,
        "exact_duplicates": sum(value - 1 for value in exact.values() if value > 1),
        "normalized_duplicates": sum(
            value - 1 for value in normalized.values() if value > 1
        ),
        "average_length": sum(map(len, texts)) / count if count else 0.0,
        "length_ratio": _ratio(length_counts, LENGTH_BUCKETS, count),
        "questions": sum("?" in text or "？" in text for text in texts),
        "fake_context": sum(
            any(marker in line.text for marker in DIRECT_STATE_PATTERNS)
            or (
                line.category in technical_categories
                and any(
                    marker.casefold() in line.text.casefold()
                    for marker in TECHNICAL_CURRENT_PATTERNS
                )
            )
            for line in lines
        ),
        "catchphrase_ratio": catchphrase_lines / count if count else 0.0,
        "openings": _opening_counts(texts, 2),
        "endings": _ending_counts(texts, 4),
    }


def _render_after_report(
    *,
    corpus: Sequence[CorpusLine],
    source: Sequence[LegacyLine],
    archive: Sequence[Mapping[str, str]],
    review: Sequence[Mapping[str, str]],
    pii: Sequence[Mapping[str, str]],
    simulation_report: SimulationReport | None,
) -> str:
    before = _legacy_metrics(source)
    after = _inventory_metrics(corpus)
    mode_ratio = after["mode_ratio"]
    before_lengths = before["length_ratio"]
    after_lengths = after["length_ratio"]
    if not isinstance(mode_ratio, Mapping):
        raise RuntimeError("simulation inventory metrics must contain an output-mode mapping")
    if not isinstance(before_lengths, Mapping) or not isinstance(after_lengths, Mapping):
        raise RuntimeError("simulation inventory metrics must contain length mappings")
    rows: list[tuple[object, object, object]] = [
        ("Total corpus rows", before["count"], len(corpus)),
        ("Enabled rows", "n/a", after["count"]),
        ("Archive rows", "0", len(archive)),
        ("Review rows", "0", len(review)),
        ("Exact duplicate texts", before["exact_duplicates"], after["exact_duplicates"]),
        (
            "Normalized duplicate texts",
            before["normalized_duplicates"],
            after["normalized_duplicates"],
        ),
        ("Average text length", f"{before['average_length']:.3f}", f"{after['average_length']:.3f}"),
    ]
    for bucket in LENGTH_BUCKETS:
        rows.append(
            (
                f"Length {bucket}",
                _percent(float(before_lengths[bucket])),
                _percent(float(after_lengths[bucket])),
            )
        )
    rows.extend(
        [
            ("Question texts", before["questions"], after["questions"]),
            ("Fake-context heuristic hits", before["fake_context"], after["fake_context"]),
            ("self_talk inventory ratio", "n/a", _percent(float(mode_ratio["self_talk"]))),
            ("ambient inventory ratio", "n/a", _percent(float(mode_ratio["ambient"]))),
            ("user_direct inventory ratio", "n/a", _percent(float(mode_ratio["user_direct"]))),
            (
                "system_observe inventory ratio",
                "n/a",
                _percent(float(mode_ratio["system_observe"])),
            ),
            (
                "Technical enabled-inventory ratio",
                "n/a",
                _percent(float(after["technical_ratio"])),
            ),
            (
                "Technical simulated-playback ratio",
                "n/a",
                _percent(simulation_report.technical_ratio)
                if simulation_report is not None
                else "see simulation-report.md",
            ),
            (
                "Catchphrase line ratio",
                _percent(float(before["catchphrase_ratio"])),
                _percent(float(after["catchphrase_ratio"])),
            ),
            ("PII review rows", "0", len(pii)),
        ]
    )
    lines = [
        "# Persona Corpus Audit After",
        "",
        "The inventory share and the simulated playback share are deliberately separate. The curated file contains technical coverage for traceability; the selector controls what is actually played.",
        "",
        "## Before/after comparison",
        "",
    ]
    lines.extend(_markdown_table(("Metric", "Legacy source", "Curated v2"), rows))
    lines.extend(["", "## Frequent openings", ""])
    lines.extend(
        _markdown_table(
            ("Corpus", "2-character opening", "Count"),
            [("Legacy", text, count) for text, count in before["openings"]]
            + [("v2 enabled", text, count) for text, count in after["openings"]],
        )
    )
    lines.extend(["", "## Frequent endings", ""])
    lines.extend(
        _markdown_table(
            ("Corpus", "4-character ending", "Count"),
            [("Legacy", text, count) for text, count in before["endings"]]
            + [("v2 enabled", text, count) for text, count in after["endings"]],
        )
    )
    return _final_markdown(lines)


_LEGACY_REFERENCE = re.compile(r"(?:^|;)legacy:(\d+)(?:;|$)")


def _diverse_archive_examples(
    archive: Sequence[Mapping[str, str]], limit: int
) -> list[Mapping[str, str]]:
    buckets: dict[str, list[Mapping[str, str]]] = defaultdict(list)
    for row in sorted(archive, key=lambda item: int(item["source_line"])):
        buckets[row["archive_reason"]].append(row)
    reasons = sorted(buckets)
    result: list[Mapping[str, str]] = []
    position = 0
    while len(result) < limit and reasons:
        next_reasons: list[str] = []
        for reason in reasons:
            rows = buckets[reason]
            if position < len(rows) and len(result) < limit:
                result.append(rows[position])
            if position + 1 < len(rows):
                next_reasons.append(reason)
        reasons = next_reasons
        position += 1
    return result


def _rewrite_evidence(
    corpus: Sequence[CorpusLine], archive: Sequence[Mapping[str, str]]
) -> tuple[
    list[tuple[Mapping[str, str], CorpusLine]],
    list[tuple[Mapping[str, str], CorpusLine]],
    list[tuple[Mapping[str, str], CorpusLine]],
]:
    archive_by_source = {int(row["source_line"]): row for row in archive}
    exact: list[tuple[Mapping[str, str], CorpusLine]] = []
    tone: list[tuple[Mapping[str, str], CorpusLine]] = []
    for row in sorted((item for item in corpus if item.enabled), key=lambda item: item.id):
        match = _LEGACY_REFERENCE.search(row.source_reference)
        if match is None:
            continue
        source = archive_by_source.get(int(match.group(1)))
        if source is None or source["original_text"] == row.text:
            continue
        exact.append((source, row))
        present_tone = [marker for marker in _TONE_MARKERS if marker in source["original_text"]]
        if present_tone and all(marker not in row.text for marker in present_tone):
            tone.append((source, row))

    enabled_by_topic: dict[str, list[CorpusLine]] = defaultdict(list)
    for row in sorted((item for item in corpus if item.enabled), key=lambda item: item.id):
        enabled_by_topic[row.topic_id].append(row)
    fake: list[tuple[Mapping[str, str], CorpusLine]] = []
    seen_topics: set[str] = set()
    for source in sorted(archive, key=lambda item: int(item["source_line"])):
        if source["archive_reason"] != "fake_context":
            continue
        topic = source["topic_id"]
        if topic in seen_topics or not enabled_by_topic.get(topic):
            continue
        replacement = enabled_by_topic[topic][0]
        if replacement.text == source["original_text"]:
            continue
        fake.append((source, replacement))
        seen_topics.add(topic)
    return exact, tone, fake

_AUTHORED_REFERENCE = re.compile(
    r"^catalog:authored-v1:(b\d{3});variant:(authored\.[a-z0-9._-]+)$"
)


def _render_authored_runtime_report(
    *,
    corpus: Sequence[CorpusLine],
    archive: Sequence[Mapping[str, str]],
    review: Sequence[Mapping[str, str]],
) -> tuple[str, int, int, int, int, int]:
    enabled = sorted((row for row in corpus if row.enabled), key=lambda row: row.id)
    expected_rows = RELEASE_INVENTORY["expanded_runtime_rows"]
    if len(enabled) != expected_rows:
        raise SimulationError(
            f"hybrid report needs {expected_rows} runtime rows; found {len(enabled)}"
        )
    authored = [row for row in enabled if row.source_kind == "curated_authored"]
    legacy_surface = [
        row for row in enabled if row.source_kind == "legacy_surface_variant"
    ]
    legacy_curated = [
        row
        for row in enabled
        if row.source_kind not in {"curated_authored", "legacy_surface_variant"}
    ]
    expected_partition = (
        RELEASE_INVENTORY["authored_runtime_rows"],
        RELEASE_INVENTORY["legacy_curated_rows"],
        RELEASE_INVENTORY["legacy_surface_rows"],
    )
    actual_partition = (len(authored), len(legacy_curated), len(legacy_surface))
    if actual_partition != expected_partition:
        raise SimulationError(
            f"hybrid report partition mismatch: expected {expected_partition!r}, "
            f"found {actual_partition!r}"
        )

    parsed: list[tuple[str, str, CorpusLine]] = []
    batch_counts: Counter[str] = Counter()
    for row in authored:
        match = _AUTHORED_REFERENCE.fullmatch(row.source_reference)
        if match is None:
            raise SimulationError(
                f"authored report found invalid source_reference {row.source_reference!r}"
            )
        batch_id, variant_id = match.groups()
        parsed.append((batch_id, variant_id, row))
        batch_counts[batch_id] += 1
    if len(batch_counts) != 100 or set(batch_counts.values()) != {300}:
        raise SimulationError(
            "authored report needs exactly 100 batches with 300 runtime rows each"
        )

    trace_samples: list[tuple[str, str, CorpusLine]] = []
    sampled_batches: set[str] = set()
    for item in sorted(parsed, key=lambda value: (value[0], value[1])):
        if item[0] in sampled_batches:
            continue
        sampled_batches.add(item[0])
        trace_samples.append(item)
    relationship_examples = [
        item for item in parsed if item[2].relationship_profile != "neutral"
    ][:20]
    disabled = _diverse_archive_examples(archive, 20)
    if len(trace_samples) < 50:
        raise SimulationError(
            f"authored report needs 50 traceable batch examples; found {len(trace_samples)}"
        )
    if len(relationship_examples) < 20:
        raise SimulationError(
            "authored report needs 20 non-neutral relationship-profile examples"
        )
    if len(disabled) < 20:
        raise SimulationError(
            f"authored report needs 20 disabled legacy examples; found {len(disabled)}"
        )

    group_counts = Counter(row.category_group for row in enabled)
    profile_counts = Counter(row.relationship_profile for row in enabled)
    reviewed = Counter(row["category"] for row in review)
    lines = [
        "# Persona Corpus Hybrid Runtime Summary",
        "",
        "The runtime combines hash-bound authored-v1 rows with the exact audited v1.2.1 legacy runtime. Inventory and lineage are reported separately by source partition.",
        "",
        "## Runtime inventory",
        "",
    ]
    lines.extend(
        _markdown_table(
            ("Metric", "Value"),
            (
                ("Enabled runtime rows", len(enabled)),
                ("Enabled authored runtime rows", len(authored)),
                ("Legacy curated runtime rows", len(legacy_curated)),
                ("Legacy runtime surfaces", len(legacy_surface)),
                ("Authored batches", len(batch_counts)),
                ("Rows per batch", 300),
            ),
        )
    )
    lines.extend(["", "## Category-group distribution", ""])
    lines.extend(
        _markdown_table(
            ("Category group", "Runtime rows"),
            sorted(group_counts.items()),
        )
    )
    lines.extend(["", "## Relationship-profile distribution", ""])
    lines.extend(
        _markdown_table(
            ("Relationship profile", "Runtime rows"),
            sorted(profile_counts.items()),
        )
    )
    lines.extend(["", "## 50 hash-bound authored lineage examples", ""])
    lines.extend(
        _markdown_table(
            ("Batch", "Variant", "v2 id", "source_reference", "Text"),
            (
                (batch, variant, row.id, row.source_reference, row.text)
                for batch, variant, row in trace_samples[:50]
            ),
        )
    )
    lines.extend(["", "## 20 non-neutral relationship-profile examples", ""])
    lines.extend(
        _markdown_table(
            ("Batch", "Variant", "Profile", "Text"),
            (
                (batch, variant, row.relationship_profile, row.text)
                for batch, variant, row in relationship_examples
            ),
        )
    )
    lines.extend(["", "## 20 archived legacy examples", ""])
    lines.extend(
        _markdown_table(
            ("source_line", "Category", "Disabled reason", "Original"),
            (
                (
                    row["source_line"],
                    row["category"],
                    row["archive_reason"],
                    row["original_text"],
                )
                for row in disabled[:20]
            ),
        )
    )
    lines.extend(["", "## Review queue by category", ""])
    lines.extend(
        _markdown_table(("Category", "Review rows"), sorted(reviewed.items()))
    )
    return (
        _final_markdown(lines),
        len(authored),
        50,
        20,
        20,
        len(batch_counts),
    )


def _render_rewrite_report(
    *,
    corpus: Sequence[CorpusLine],
    archive: Sequence[Mapping[str, str]],
    review: Sequence[Mapping[str, str]],
) -> tuple[str, int, int, int, int]:
    exact, tone, fake = _rewrite_evidence(corpus, archive)
    disabled = _diverse_archive_examples(archive, 20)
    if len(exact) < 50:
        raise SimulationError(f"rewrite report needs 50 exact legacy mappings; found {len(exact)}")
    if len(disabled) < 20:
        raise SimulationError(f"rewrite report needs 20 disabled examples; found {len(disabled)}")
    if len(tone) < 20:
        raise SimulationError(f"rewrite report needs 20 tone fixes; found {len(tone)}")
    if len(fake) < 20:
        raise SimulationError(f"rewrite report needs 20 fake-context fixes; found {len(fake)}")

    enabled = [row for row in corpus if row.enabled]
    retained = Counter(row.category for row in enabled)
    rewritten = Counter(
        row.category for row in enabled if row.source_kind == "rewritten_topic"
    )
    archived = Counter(row["category"] for row in archive)
    reviewed = Counter(row["category"] for row in review)
    categories = sorted(set(retained) | set(rewritten) | set(archived) | set(reviewed))
    rewrite_reasons = Counter(row.rewrite_reason for row in enabled)

    lines = [
        "# Persona Corpus Rewrite Summary",
        "",
        "Every example below is joined to generated data. Exact examples use `source_reference=legacy:N`; tone examples use the same exact lineage plus a documented report-time marker-removal heuristic; fake-context examples are explicitly labeled as a topic-level rewritten outcome and are not claimed to be one-to-one line rewrites.",
        "",
        "## Category disposition",
        "",
    ]
    lines.extend(
        _markdown_table(
            ("Category", "Enabled retained", "Rewritten topics", "Archived", "Review"),
            (
                (
                    category,
                    retained[category],
                    rewritten[category],
                    archived[category],
                    reviewed[category],
                )
                for category in categories
            ),
        )
    )
    lines.extend(["", "## Main enabled rewrite reasons", ""])
    lines.extend(
        _markdown_table(
            ("Rewrite reason", "Enabled rows"),
            sorted(rewrite_reasons.items(), key=lambda item: (-item[1], item[0])),
        )
    )

    lines.extend(["", "## 50 exact original-to-rewrite examples", ""])
    lines.extend(
        _markdown_table(
            ("source_line", "v2 id", "Original", "Rewritten"),
            (
                (source["source_line"], row.id, source["original_text"], row.text)
                for source, row in exact[:50]
            ),
        )
    )
    lines.extend(["", "## 20 original-to-disabled-reason examples", ""])
    lines.extend(
        _markdown_table(
            ("source_line", "topic_id", "Original", "Disabled reason"),
            (
                (
                    source["source_line"],
                    source["topic_id"],
                    source["original_text"],
                    source["archive_reason"],
                )
                for source in disabled[:20]
            ),
        )
    )
    lines.extend(["", "## 20 tone fixes from exact lineage", ""])
    lines.append(
        "Report-time tone heuristic: the archived original contains a controlled harsh/commanding marker and the exact legacy-linked v2 sentence removes every matched marker. This is evidence classification for the report, not a claim that the builder used `tone_conflict`."
    )
    lines.append("")
    lines.extend(
        _markdown_table(
            ("source_line", "v2 id", "Original", "Tone-safe rewrite"),
            (
                (source["source_line"], row.id, source["original_text"], row.text)
                for source, row in tone[:20]
            ),
        )
    )
    lines.extend(["", "## 20 fake-context fixes", ""])
    lines.append(
        "Mapping kind: **topic-level rewritten outcome**. Each source row is authoritatively archived as `fake_context`; the enabled outcome shares its `topic_id`. It is not presented as an exact per-line rewrite."
    )
    lines.append("")
    lines.extend(
        _markdown_table(
            ("source_line", "topic_id", "Archived fake-context original", "Enabled topic outcome"),
            (
                (
                    source["source_line"],
                    source["topic_id"],
                    source["original_text"],
                    row.text,
                )
                for source, row in fake[:20]
            ),
        )
    )
    return _final_markdown(lines), 50, 20, 20, 20


def _render_manual_review(
    review: Sequence[Mapping[str, str]], pii: Sequence[Mapping[str, str]]
) -> str:
    review_counts = Counter(row["risk_type"] for row in review)
    pii_counts = Counter(row["pii_type"] for row in pii)
    lines = [
        "# Persona Corpus Manual Review",
        "",
        "All items remain disabled and unapproved until a human decision is recorded. This report does not infer consent, fictional status, or relationship boundaries.",
        "",
        "## Required product-owner decisions",
        "",
    ]
    lines.extend(
        _markdown_table(
            ("Decision", "Current safe default", "Human confirmation needed"),
            (
                ("Use of the full name 雷琳玥", "Disabled / privacy review", "Confirm fictional identity and publication authorization"),
                ("湖南 and 广东 life history", "Disabled / privacy review", "Confirm the history may be made public"),
                ("Salary, temporary work and job changes", "Disabled / privacy review", "Confirm the facts are fictional or expressly authorized"),
                ("小笨蛋 and similar intimate address", "Disabled by default", "Confirm the default relationship boundary"),
                ("Rhetorical questions", "No question marks in enabled corpus", "Decide whether any narrow exception is desired later"),
                ("Direct technical speech in IDE context", "Future signal is nullable and unused", "Confirm wording and reliable signal source before enabling"),
                ("One-time EasterEgg lines", "Cooldown and recent-window limits only", "Identify which exact IDs require lifetime one-shot storage"),
            ),
        )
    )
    lines.extend(["", "## Review risk summary", ""])
    lines.extend(
        _markdown_table(
            ("Risk type", "Rows"),
            sorted(review_counts.items(), key=lambda item: (-item[1], item[0])),
        )
    )
    lines.extend(["", "## PII risk summary", ""])
    lines.extend(
        _markdown_table(
            ("PII type", "Rows"),
            sorted(pii_counts.items(), key=lambda item: (-item[1], item[0])),
        )
    )
    lines.extend(["", "## Exhaustive corpus review rows", ""])
    lines.extend(
        _markdown_table(
            (
                "review_id",
                "source_line",
                "category",
                "risk_type",
                "Original",
                "Suggested action",
                "Suggested rewrite",
            ),
            (
                (
                    row["review_id"],
                    row["source_line"],
                    row["category"],
                    row["risk_type"],
                    row["original_text"],
                    row["suggested_action"],
                    row["suggested_rewrite"],
                )
                for row in sorted(
                    review, key=lambda item: (int(item["source_line"]), item["review_id"])
                )
            ),
        )
    )
    lines.extend(["", "## Exhaustive PII review rows", ""])
    lines.extend(
        _markdown_table(
            (
                "review_id",
                "source_line",
                "category",
                "pii_type",
                "Original",
                "Suggested action",
                "Suggested rewrite",
            ),
            (
                (
                    row["review_id"],
                    row["source_line"],
                    row["category"],
                    row["pii_type"],
                    row["original_text"],
                    row["suggested_action"],
                    row["suggested_rewrite"],
                )
                for row in sorted(
                    pii, key=lambda item: (int(item["source_line"]), item["review_id"])
                )
            ),
        )
    )
    return _final_markdown(lines)


def _write_lf(path: Path, text: str | bytes) -> None:
    path = Path(path)
    payload = text if isinstance(text, bytes) else text.encode("utf-8")
    if b"\r" in payload or not payload.endswith(b"\n") or payload.endswith(b"\n\n"):
        raise SimulationError(f"{path}: generated artifact must use LF and one trailing newline")
    try:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(payload)
    except OSError as error:
        raise SimulationError(f"{path}: cannot write report: {error}") from error


def write_editorial_reports(
    *,
    corpus: Sequence[CorpusLine],
    source_path: Path,
    archive_path: Path,
    review_path: Path,
    pii_path: Path,
    audit_after_path: Path,
    rewrite_summary_path: Path,
    manual_review_path: Path,
    simulation_report: SimulationReport | None = None,
) -> EditorialReportSummary:
    try:
        source = load_legacy(Path(source_path))
    except CorpusFormatError as error:
        raise SimulationError(str(error)) from error
    archive = _read_tsv(Path(archive_path), ARCHIVE_HEADER)
    review = _read_tsv(Path(review_path), REVIEW_HEADER)
    pii = _read_tsv(Path(pii_path), PII_REVIEW_HEADER)
    after = _render_after_report(
        corpus=corpus,
        source=source,
        archive=archive,
        review=review,
        pii=pii,
        simulation_report=simulation_report,
    )
    (
        rewrite,
        authored_runtime_rows,
        authored_trace_examples,
        disabled_examples,
        relationship_profile_examples,
        authored_batch_count,
    ) = _render_authored_runtime_report(
        corpus=corpus,
        archive=archive,
        review=review,
    )
    manual = _render_manual_review(review, pii)
    _write_lf(Path(audit_after_path), after)
    _write_lf(Path(rewrite_summary_path), rewrite)
    _write_lf(Path(manual_review_path), manual)
    return EditorialReportSummary(
        authored_runtime_rows=authored_runtime_rows,
        authored_trace_examples=authored_trace_examples,
        disabled_examples=disabled_examples,
        relationship_profile_examples=relationship_profile_examples,
        authored_batch_count=authored_batch_count,
        manual_review_items=len(review) + len(pii),
    )


__all__ = [
    "SUBSEED_DERIVATION_SHA256",
    "SUBSEED_DERIVATION_SPEC",
    "SUBSEED_DERIVATION_VERSION",
    "CandidateIndex",
    "DistributionTolerance",
    "EditorialReportSummary",
    "SeedMetrics",
    "SimulationAttempt",
    "SimulationError",
    "SimulationReport",
    "analyze_constraints",
    "build_scenario_coverage",
    "combine_hard_violations",
    "derive_distribution_policy",
    "derive_dry_sharp_policy",
    "derive_lexical_exposure_policy",
    "derive_subseed",
    "probe_inventory_coverage",
    "render_simulation_report",
    "run_adversarial_suite",
    "simulate",
    "write_editorial_reports",
]
