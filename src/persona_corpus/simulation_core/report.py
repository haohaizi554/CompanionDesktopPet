from __future__ import annotations

import json
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
from typing import Iterable, Mapping, Sequence

from ..context import ContextError, PersonaContext
from ..models import CorpusLine
from ..selector import SchedulerConfig
from ..validation import CATCHPHRASES
from .constraints import AdversarialSuiteResult
from .constraints import analyze_constraints
from .metrics import DistributionPolicy, DistributionTolerance, derive_distribution_policy
from .scenarios import (
    SUBSEED_DERIVATION_VERSION,
    InventoryCoverage,
    ScenarioCoverage,
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
SIMULATION_SCHEMA_VERSION = 1
_EPSILON = 1e-9


def combine_hard_violations(
    natural: Sequence[str],
    adversarial: Sequence[str],
) -> list[str]:
    """A run is clean only when both independent suites are clean."""

    return sorted(set(natural) | set(adversarial))


@dataclass(frozen=True, slots=True)
class SimulationAttempt:
    seed: int
    attempted_at: datetime
    context: PersonaContext
    row: CorpusLine | None

    @property
    def selected_id(self) -> str | None:
        return self.row.id if self.row is not None else None

    def context_payload(self) -> dict[str, object]:
        return {
            "event": self.context.event,
            "daypart": self.context.daypart,
            "weekday": self.context.weekday,
            "is_weekend": self.context.is_weekend,
            "holiday": self.context.holiday,
            "anniversary_days": self.context.anniversary_days,
            "minutes_since_last_output": float(self.context.minutes_since_last_output),
            "ide_foreground": self.context.ide_foreground,
            "active_minutes": self.context.active_minutes,
            "idle_return": self.context.idle_return,
            "fullscreen": self.context.fullscreen,
        }

    def validation_payload(self) -> dict[str, object]:
        return {
            "seed": self.seed,
            "attempted_at": self.attempted_at.isoformat(timespec="seconds"),
            "context": self.context_payload(),
            "selected_id": self.selected_id,
        }


@dataclass(frozen=True, slots=True)
class SeedMetrics:
    seed: int
    attempts: int
    outputs: int
    none_count: int
    group_counts: Mapping[str, int]
    group_ratio: Mapping[str, float]
    mode_counts: Mapping[str, int]
    mode_ratio: Mapping[str, float]
    anomalies: tuple[str, ...]


@dataclass(slots=True)
class SimulationReport:
    schema_version: int
    corpus_sha256: str
    scheduler_config_sha256: str
    subseed_derivation_version: str
    distribution_policy: DistributionPolicy
    scenario_coverage: ScenarioCoverage
    inventory_coverage: InventoryCoverage
    adversarial_result: AdversarialSuiteResult
    days: int
    seeds: tuple[int, ...]
    attempts: tuple[SimulationAttempt, ...]
    total_attempts: int
    output_count: int
    none_count: int
    average_outputs_per_day: float
    max_outputs_per_hour: int
    group_counts: dict[str, int]
    group_ratio: dict[str, float]
    mode_counts: dict[str, int]
    mode_ratio: dict[str, float]
    technical_ratio: float
    easter_egg_ratio: float
    user_direct_ratio: float
    id_cooldown_repeats: int
    semantic_cooldown_repeats: int
    adjacent_same_category_group: int
    adjacent_technical: int
    adjacent_daily_care: int
    adjacent_emotional_reflection: int
    adjacent_care: int
    average_text_length: float
    length_distribution: dict[str, float]
    common_openings: dict[int, list[tuple[str, int]]]
    common_endings: dict[int, list[tuple[str, int]]]
    catchphrase_ratio: float
    catchphrase_counts: dict[str, int]
    question_count: int
    unmet_context_count: int
    per_seed: dict[int, SeedMetrics]
    per_seed_anomalies: dict[int, list[str]]
    natural_hard_violations: list[str]
    adversarial_hard_violations: list[str]
    hard_violations: list[str]

    def to_validation_payload(self) -> dict[str, object]:
        return {
            "schema_version": int(self.schema_version),
            "corpus_sha256": self.corpus_sha256,
            "scheduler_config_sha256": self.scheduler_config_sha256,
            "days": self.days,
            "seeds": list(self.seeds),
            "attempts": [attempt.validation_payload() for attempt in self.attempts],
        }

    def to_validation_json(self) -> bytes:
        return (
            json.dumps(
                self.to_validation_payload(),
                ensure_ascii=False,
                sort_keys=True,
                separators=(",", ":"),
                allow_nan=False,
            )
            + "\n"
        ).encode("utf-8")

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


def _stable_common(texts: Sequence[str], widths: Sequence[int]) -> dict[int, list[tuple[str, int]]]:
    result: dict[int, list[tuple[str, int]]] = {}
    for width in widths:
        counts = Counter(text[:width] for text in texts if len(text) >= width)
        result[width] = sorted(counts.items(), key=lambda item: (-item[1], item[0]))[:10]
    return result


def _stable_common_endings(
    texts: Sequence[str], widths: Sequence[int]
) -> dict[int, list[tuple[str, int]]]:
    result: dict[int, list[tuple[str, int]]] = {}
    for width in widths:
        counts = Counter(text[-width:] for text in texts if len(text) >= width)
        result[width] = sorted(counts.items(), key=lambda item: (-item[1], item[0]))[:10]
    return result


def _trigger_satisfied(attempt: SimulationAttempt, config: SchedulerConfig) -> bool:
    row = attempt.row
    if row is None:
        return True
    context = attempt.context
    trigger = row.trigger
    elapsed = float(context.minutes_since_last_output)
    if trigger == "any":
        return True
    if trigger == "app_start":
        return context.event == "app_start"
    if trigger == "day_changed":
        return context.event == "day_changed"
    if trigger in {"morning", "noon", "afternoon", "evening", "late_night"}:
        return context.daypart == trigger
    if trigger == "weekday":
        return not context.is_weekend
    if trigger == "weekend":
        return context.is_weekend
    if trigger == "holiday":
        return context.holiday is not None
    if trigger == "anniversary":
        return context.anniversary_days > 0
    if trigger == "long_silence":
        return elapsed + _EPSILON >= config.long_silence_minutes
    if trigger == "ide_foreground":
        return context.ide_foreground is True
    if trigger == "long_active":
        return context.active_minutes is not None and context.active_minutes >= 90
    if trigger == "idle_return":
        return context.idle_return is True
    return False


def _required_context_satisfied(attempt: SimulationAttempt) -> bool:
    row = attempt.row
    if row is None:
        return True
    try:
        tokens = attempt.context.controlled_tokens(attempt.attempted_at)
    except ContextError:
        return False
    required = tuple(token.strip() for token in row.required_context.split(","))
    if not required or any(not token for token in required):
        return False
    return required == ("none",) or all(token in tokens for token in required)


def _add_hard(
    hard: set[str], anomalies: dict[int, set[str]], seed: int, code: str
) -> None:
    scoped = f"seed_{seed}:{code}"
    hard.add(scoped)
    anomalies[seed].add(code)


def analyze_simulation(
    *,
    corpus_sha256: str,
    config_sha256: str,
    config: SchedulerConfig,
    days: int,
    seeds: tuple[int, ...],
    attempts: tuple[SimulationAttempt, ...],
    distribution_tolerance: DistributionTolerance,
    scenario_coverage: ScenarioCoverage,
    inventory_coverage: InventoryCoverage,
    adversarial_result: AdversarialSuiteResult,
) -> SimulationReport:
    hard: set[str] = set()
    distribution_policy = derive_distribution_policy(config, distribution_tolerance)
    anomalies: dict[int, set[str]] = {seed: set() for seed in seeds}
    for violation in analyze_constraints(attempts, config).violations:
        _add_hard(hard, anomalies, violation.seed, violation.code)
    if days < 30:
        hard.add("duration_below_30_days")
    if len(seeds) < 10:
        hard.add("seed_count_below_10")

    attempts_by_seed: dict[int, list[SimulationAttempt]] = defaultdict(list)
    for attempt in attempts:
        attempts_by_seed[attempt.seed].append(attempt)

    total_group_counts: Counter[str] = Counter()
    total_mode_counts: Counter[str] = Counter()
    selected_texts: list[str] = []
    id_cooldown_repeats = 0
    semantic_cooldown_repeats = 0
    adjacent_same_group = 0
    adjacent_technical = 0
    adjacent_daily = 0
    adjacent_emotional = 0
    adjacent_care = 0
    question_count = 0
    unmet_context_count = 0
    max_outputs_per_hour = 0
    per_seed: dict[int, SeedMetrics] = {}

    for seed in seeds:
        seed_attempts = sorted(attempts_by_seed[seed], key=lambda item: item.attempted_at)
        outputs = [attempt for attempt in seed_attempts if attempt.row is not None]
        seed_group_counts: Counter[str] = Counter()
        seed_mode_counts: Counter[str] = Counter()
        previous: SimulationAttempt | None = None
        last_id: dict[str, datetime] = {}
        last_semantic: dict[str, datetime] = {}
        rolling_hour: list[datetime] = []

        if not outputs:
            _add_hard(hard, anomalies, seed, "zero_outputs")

        for attempt in outputs:
            assert attempt.row is not None
            row = attempt.row
            now = attempt.attempted_at
            seed_group_counts[row.category_group] += 1
            seed_mode_counts[row.output_mode] += 1
            total_group_counts[row.category_group] += 1
            total_mode_counts[row.output_mode] += 1
            selected_texts.append(row.text)

            if previous is not None and previous.row is not None:
                elapsed_minutes = (now - previous.attempted_at).total_seconds() / 60
                if abs(elapsed_minutes - float(attempt.context.minutes_since_last_output)) > _EPSILON:
                    unmet_context_count += 1
                if (
                    row.category_group in {"daily_care", "emotional_reflection"}
                    and previous.row.category_group
                    in {"daily_care", "emotional_reflection"}
                ):
                    adjacent_care += 1
                if row.category_group == previous.row.category_group:
                    adjacent_same_group += 1
                    if row.category_group == "technical":
                        adjacent_technical += 1
                    elif row.category_group == "daily_care":
                        adjacent_daily += 1
                    elif row.category_group == "emotional_reflection":
                        adjacent_emotional += 1

            if not _trigger_satisfied(attempt, config) or not _required_context_satisfied(attempt):
                unmet_context_count += 1
            if row.requires_reply or "?" in row.text or "？" in row.text:
                question_count += 1

            if row.id in last_id:
                elapsed_hours = (now - last_id[row.id]).total_seconds() / 3600
                if elapsed_hours + _EPSILON < row.cooldown_hours:
                    id_cooldown_repeats += 1
            if row.semantic_group in last_semantic:
                elapsed_hours = (now - last_semantic[row.semantic_group]).total_seconds() / 3600
                if elapsed_hours + _EPSILON < row.semantic_cooldown_hours:
                    semantic_cooldown_repeats += 1

            rolling_hour = [
                played_at
                for played_at in rolling_hour
                if now - played_at < timedelta(hours=1)
            ]
            rolling_hour.append(now)
            max_outputs_per_hour = max(max_outputs_per_hour, len(rolling_hour))
            last_id[row.id] = now
            last_semantic[row.semantic_group] = now
            previous = attempt

        seed_output_count = len(outputs)
        seed_group_ratio = _ratio(seed_group_counts, CATEGORY_GROUPS, seed_output_count)
        seed_mode_ratio = _ratio(seed_mode_counts, OUTPUT_MODES, seed_output_count)
        if seed_output_count:
            if not (
                distribution_policy.technical.minimum
                <= seed_group_ratio["technical"]
                <= distribution_policy.technical.maximum
            ):
                _add_hard(hard, anomalies, seed, "technical_ratio_out_of_bounds")
            if seed_group_ratio["easter_egg"] > distribution_policy.easter_egg.maximum:
                _add_hard(hard, anomalies, seed, "easter_egg_ratio_above_limit")
            if (
                seed_mode_ratio["self_talk"] + seed_mode_ratio["ambient"]
                < distribution_policy.self_ambient.minimum
            ):
                _add_hard(hard, anomalies, seed, "self_ambient_ratio_below_minimum")
            if seed_mode_ratio["user_direct"] > distribution_policy.user_direct.maximum:
                _add_hard(hard, anomalies, seed, "user_direct_ratio_above_limit")
            if seed_group_counts["easter_egg"] == 0:
                anomalies[seed].add("easter_egg_not_observed")
            if seed_mode_counts["user_direct"] == 0:
                anomalies[seed].add("user_direct_not_observed")

        per_seed[seed] = SeedMetrics(
            seed=seed,
            attempts=len(seed_attempts),
            outputs=seed_output_count,
            none_count=len(seed_attempts) - seed_output_count,
            group_counts={key: seed_group_counts[key] for key in CATEGORY_GROUPS},
            group_ratio=seed_group_ratio,
            mode_counts={key: seed_mode_counts[key] for key in OUTPUT_MODES},
            mode_ratio=seed_mode_ratio,
            anomalies=(),
        )

    output_count = len(selected_texts)
    group_counts = {key: total_group_counts[key] for key in CATEGORY_GROUPS}
    mode_counts = {key: total_mode_counts[key] for key in OUTPUT_MODES}
    group_ratio = _ratio(group_counts, CATEGORY_GROUPS, output_count)
    mode_ratio = _ratio(mode_counts, OUTPUT_MODES, output_count)
    if output_count == 0:
        hard.add("zero_outputs")
    else:
        if not (
            distribution_policy.technical.minimum
            <= group_ratio["technical"]
            <= distribution_policy.technical.maximum
        ):
            hard.add("technical_ratio_out_of_bounds")
        if group_ratio["easter_egg"] > distribution_policy.easter_egg.maximum:
            hard.add("easter_egg_ratio_above_limit")
        if (
            mode_ratio["self_talk"] + mode_ratio["ambient"]
            < distribution_policy.self_ambient.minimum
        ):
            hard.add("self_ambient_ratio_below_minimum")
        if mode_ratio["user_direct"] > distribution_policy.user_direct.maximum:
            hard.add("user_direct_ratio_above_limit")

    lengths = [len(text) for text in selected_texts]
    length_counts = Counter(_length_bucket(length) for length in lengths)
    length_distribution = _ratio(length_counts, LENGTH_BUCKETS, output_count)
    catchphrase_counts = {
        phrase: sum(phrase in text for text in selected_texts) for phrase in CATCHPHRASES
    }
    catchphrase_lines = sum(
        any(phrase in text for phrase in CATCHPHRASES) for text in selected_texts
    )


    per_seed_anomalies = {
        seed: sorted(anomalies[seed]) for seed in seeds
    }
    for seed in seeds:
        metrics = per_seed[seed]
        per_seed[seed] = SeedMetrics(
            seed=metrics.seed,
            attempts=metrics.attempts,
            outputs=metrics.outputs,
            none_count=metrics.none_count,
            group_counts=metrics.group_counts,
            group_ratio=metrics.group_ratio,
            mode_counts=metrics.mode_counts,
            mode_ratio=metrics.mode_ratio,
            anomalies=tuple(per_seed_anomalies[seed]),
        )

    return SimulationReport(
        schema_version=SIMULATION_SCHEMA_VERSION,
        corpus_sha256=corpus_sha256,
        scheduler_config_sha256=config_sha256,
        subseed_derivation_version=SUBSEED_DERIVATION_VERSION,
        distribution_policy=distribution_policy,
        scenario_coverage=scenario_coverage,
        inventory_coverage=inventory_coverage,
        adversarial_result=adversarial_result,
        days=days,
        seeds=seeds,
        attempts=attempts,
        total_attempts=len(attempts),
        output_count=output_count,
        none_count=len(attempts) - output_count,
        average_outputs_per_day=(output_count / (days * len(seeds))),
        max_outputs_per_hour=max_outputs_per_hour,
        group_counts=group_counts,
        group_ratio=group_ratio,
        mode_counts=mode_counts,
        mode_ratio=mode_ratio,
        technical_ratio=group_ratio["technical"],
        easter_egg_ratio=group_ratio["easter_egg"],
        user_direct_ratio=mode_ratio["user_direct"],
        id_cooldown_repeats=id_cooldown_repeats,
        semantic_cooldown_repeats=semantic_cooldown_repeats,
        adjacent_same_category_group=adjacent_same_group,
        adjacent_technical=adjacent_technical,
        adjacent_daily_care=adjacent_daily,
        adjacent_emotional_reflection=adjacent_emotional,
        adjacent_care=adjacent_care,
        average_text_length=(sum(lengths) / output_count if output_count else 0.0),
        length_distribution=length_distribution,
        common_openings=_stable_common(selected_texts, PREFIX_WIDTHS),
        common_endings=_stable_common_endings(selected_texts, SUFFIX_WIDTHS),
        catchphrase_ratio=(catchphrase_lines / output_count if output_count else 0.0),
        catchphrase_counts=catchphrase_counts,
        question_count=question_count,
        unmet_context_count=unmet_context_count,
        per_seed=per_seed,
        per_seed_anomalies=per_seed_anomalies,
        natural_hard_violations=sorted(hard),
        adversarial_hard_violations=list(adversarial_result.hard_violations),
        hard_violations=combine_hard_violations(
            sorted(hard),
            adversarial_result.hard_violations,
        ),
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


def render_simulation_report(report: SimulationReport) -> str:
    lines = [
        "# Persona Playback Simulation",
        "",
        "This report is deterministic: it contains no wall-clock generation time, host path, network result, or model output.",
        "The validator-facing event stream is stored separately with an exact schema and input hashes.",
        "",
        "## Run contract",
        "",
    ]
    lines.extend(
        _markdown_table(
            ("Field", "Value"),
            (
                ("Schema version", report.schema_version),
                ("Days per seed", report.days),
                ("Seeds", ", ".join(map(str, report.seeds))),
                ("Corpus SHA-256", f"`{report.corpus_sha256}`"),
                ("Scheduler SHA-256", f"`{report.scheduler_config_sha256}`"),
                ("Subseed derivation", report.subseed_derivation_version),
                (
                    "Distribution tolerance",
                    _percent(report.distribution_policy.tolerance.absolute),
                ),
            ),
        )
    )
    lines.extend(["", "## Approved metrics", ""])
    lines.extend(
        _markdown_table(
            ("Metric", "Value"),
            (
                ("1. Total attempts", report.total_attempts),
                ("2. Actual outputs", report.output_count),
                ("3. Returned None", report.none_count),
                ("4. Average outputs per day per seed", f"{report.average_outputs_per_day:.3f}"),
                ("5. Maximum outputs in rolling (now-60m, now]", report.max_outputs_per_hour),
                ("8. Technical playback ratio", _percent(report.technical_ratio)),
                ("9. EasterEgg playback ratio", _percent(report.easter_egg_ratio)),
                ("10. user_direct playback ratio", _percent(report.user_direct_ratio)),
                ("11. ID cooldown repeats", report.id_cooldown_repeats),
                ("12. Semantic cooldown repeats", report.semantic_cooldown_repeats),
                ("13. Adjacent same category_group", report.adjacent_same_category_group),
                ("14. Adjacent technical", report.adjacent_technical),
                ("15a. Adjacent daily_care", report.adjacent_daily_care),
                ("15b. Adjacent emotional_reflection", report.adjacent_emotional_reflection),
                (
                    "15c. Combined adjacent care (including cross-group pairs)",
                    report.adjacent_care,
                ),
                ("16. Average text length", f"{report.average_text_length:.3f}"),
                ("19. Catchphrase line ratio", _percent(report.catchphrase_ratio)),
                ("20. Question/reply outputs", report.question_count),
                ("21. Unmet trigger/context outputs", report.unmet_context_count),
                (
                    "Natural hard violations",
                    ", ".join(report.natural_hard_violations)
                    if report.natural_hard_violations
                    else "none",
                ),
                (
                    "Adversarial hard violations",
                    ", ".join(report.adversarial_hard_violations)
                    if report.adversarial_hard_violations
                    else "none",
                ),
                (
                    "Combined hard violations",
                    ", ".join(report.hard_violations) if report.hard_violations else "none",
                ),
            ),
        )
    )

    policy = report.distribution_policy
    lines.extend(["", "## Scheduler-derived distribution contract", ""])
    lines.extend(
        _markdown_table(
            ("Metric", "Target", "Minimum", "Maximum"),
            (
                (
                    "technical",
                    _percent(policy.technical.target),
                    _percent(policy.technical.minimum),
                    _percent(policy.technical.maximum),
                ),
                (
                    "easter_egg",
                    _percent(policy.easter_egg.target),
                    _percent(policy.easter_egg.minimum),
                    _percent(policy.easter_egg.maximum),
                ),
                (
                    "self_talk + ambient",
                    _percent(policy.self_ambient.target),
                    _percent(policy.self_ambient.minimum),
                    _percent(policy.self_ambient.maximum),
                ),
                (
                    "user_direct",
                    _percent(policy.user_direct.target),
                    _percent(policy.user_direct.minimum),
                    _percent(policy.user_direct.maximum),
                ),
            ),
        )
    )

    coverage = report.scenario_coverage
    inventory = report.inventory_coverage
    lines.extend(["", "## Scenario and inventory coverage", ""])
    lines.extend(
        _markdown_table(
            ("Coverage", "Value"),
            (
                ("Seasons", ", ".join(coverage.seasons)),
                ("Dayparts", ", ".join(coverage.dayparts)),
                ("Dawn", coverage.dawn),
                ("Events", ", ".join(coverage.events)),
                ("Weekday + weekend", ", ".join(map(str, coverage.weekend_values))),
                ("Holiday", coverage.holiday),
                ("Anniversary", coverage.anniversary),
                ("Month boundary", coverage.month_boundary),
                ("Nullable signal combinations", coverage.nullable_signal_combinations),
                (
                    "Inventory trigger misses",
                    ", ".join(inventory.trigger_misses) or "none",
                ),
                (
                    "Inventory context misses",
                    ", ".join(inventory.context_misses) or "none",
                ),
                (
                    "Unreachable trigger/context pairs",
                    ", ".join(inventory.unreachable_pairs) or "none",
                ),
            ),
        )
    )

    lines.extend(["", "## Adversarial selector and analyzer evidence", ""])
    lines.extend(
        _markdown_table(
            ("Case", "Selector decision", "Expected", "Analyzer codes", "Status"),
            (
                (
                    case.name,
                    "selected" if case.selector_selected else "rejected",
                    "selected" if case.selector_expected_selected else "rejected",
                    ", ".join(case.observed_codes) or "none",
                    "pass" if case.passed else "FAIL",
                )
                for case in report.adversarial_result.cases
            ),
        )
    )

    lines.extend(["", "## 6. category_group playback", ""])
    lines.extend(
        _markdown_table(
            ("category_group", "Count", "Ratio"),
            (
                (group, report.group_counts[group], _percent(report.group_ratio[group]))
                for group in CATEGORY_GROUPS
            ),
        )
    )
    lines.extend(["", "## 7. output_mode playback", ""])
    lines.extend(
        _markdown_table(
            ("output_mode", "Count", "Ratio"),
            (
                (mode, report.mode_counts[mode], _percent(report.mode_ratio[mode]))
                for mode in OUTPUT_MODES
            ),
        )
    )
    lines.extend(["", "## 17. Playback text-length distribution", ""])
    lines.extend(
        _markdown_table(
            ("Length bucket", "Ratio"),
            ((bucket, _percent(report.length_distribution[bucket])) for bucket in LENGTH_BUCKETS),
        )
    )
    lines.extend(["", "## 18. Frequent openings and endings", ""])
    for width in PREFIX_WIDTHS:
        lines.extend([f"### Opening width {width}", ""])
        lines.extend(_markdown_table(("Opening", "Playback count"), report.common_openings[width]))
        lines.append("")
    for width in SUFFIX_WIDTHS:
        lines.extend([f"### Ending width {width}", ""])
        lines.extend(_markdown_table(("Ending", "Playback count"), report.common_endings[width]))
        lines.append("")

    lines.extend(["## Catchphrase counts", ""])
    lines.extend(
        _markdown_table(
            ("Catchphrase", "Playback count"),
            ((phrase, report.catchphrase_counts[phrase]) for phrase in CATCHPHRASES),
        )
    )
    lines.extend(["", "## 22. Per-seed results and anomalies", ""])
    lines.extend(
        _markdown_table(
            (
                "Seed",
                "Attempts",
                "Outputs",
                "None",
                "Technical",
                "Self-talk + ambient",
                "user_direct",
                "EasterEgg",
                "Anomalies",
            ),
            (
                (
                    seed,
                    report.per_seed[seed].attempts,
                    report.per_seed[seed].outputs,
                    report.per_seed[seed].none_count,
                    _percent(report.per_seed[seed].group_ratio["technical"]),
                    _percent(
                        report.per_seed[seed].mode_ratio["self_talk"]
                        + report.per_seed[seed].mode_ratio["ambient"]
                    ),
                    _percent(report.per_seed[seed].mode_ratio["user_direct"]),
                    _percent(report.per_seed[seed].group_ratio["easter_egg"]),
                    ", ".join(report.per_seed_anomalies[seed]) or "none",
                )
                for seed in report.seeds
            ),
        )
    )
    lines.extend(
        [
            "",
            "`easter_egg_not_observed` and `user_direct_not_observed` are transparent non-hard observations. They are not fabricated into the event stream; the current selector and enabled inventory naturally produced zero during this fixed schedule.",
        ]
    )
    return _final_markdown(lines)
