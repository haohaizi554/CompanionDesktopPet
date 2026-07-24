from __future__ import annotations

import hashlib
import json
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Iterable, Mapping, Sequence

from .builder import serialize_v2
from .context import ContextError, PersonaContext
from .history import SelectionHistory
from .loader import CorpusFormatError, load_legacy
from .models import CorpusLine, LegacyLine
from .normalization import normalize_text
from .schema import ARCHIVE_HEADER, PII_REVIEW_HEADER, REVIEW_HEADER
from .selector import SchedulerConfig, SelectorConfigError, select_line
from .simulation_core.constraints import analyze_constraints, run_adversarial_suite
from .simulation_core.scenarios import SUBSEED_DERIVATION_VERSION, derive_subseed
from .validation import (
    CATCHPHRASES,
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
SIMULATION_SCHEMA_VERSION = 1
SIMULATION_START = datetime(2026, 1, 1, tzinfo=timezone(timedelta(hours=8)))
ATTEMPT_SLOTS = (
    (1, 20, "day_changed"),
    (7, 20, "app_start"),
    (11, 40, "tick"),
    (15, 15, "tick"),
    (19, 30, "tick"),
)
SIMULATED_HOLIDAYS = {(1, 1): "元旦"}
ANNIVERSARY_DAY_INDEX = 14
ANNIVERSARY_DAYS = 365
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


@dataclass(frozen=True, slots=True)
class EditorialReportSummary:
    general_rewrite_examples: int
    disabled_examples: int
    tone_fix_examples: int
    fake_context_examples: int
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


def _analyse(
    *,
    corpus_sha256: str,
    config_sha256: str,
    config: SchedulerConfig,
    days: int,
    seeds: tuple[int, ...],
    attempts: tuple[SimulationAttempt, ...],
) -> SimulationReport:
    hard: set[str] = set()
    anomalies: dict[int, set[str]] = {seed: set() for seed in seeds}
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
        daily_ids: Counter[tuple[object, str]] = Counter()
        rolling_hour: list[datetime] = []
        rolling_late: list[datetime] = []
        recent_rows: list[CorpusLine] = []

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
                    _add_hard(hard, anomalies, seed, "elapsed_context_mismatch")
                if elapsed_minutes + _EPSILON < config.minimum_interval_minutes:
                    _add_hard(hard, anomalies, seed, "minimum_interval_violation")
                required_interval = config.interrupt_cost_minimum_intervals_minutes[
                    row.interrupt_cost
                ]
                if elapsed_minutes + _EPSILON < required_interval:
                    _add_hard(hard, anomalies, seed, "interrupt_budget_violation")
                if row.semantic_group == previous.row.semantic_group:
                    _add_hard(hard, anomalies, seed, "adjacent_semantic_violation")
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
                        _add_hard(hard, anomalies, seed, "adjacent_technical")
                    elif row.category_group == "daily_care":
                        adjacent_daily += 1
                        _add_hard(hard, anomalies, seed, "adjacent_daily_care")
                    elif row.category_group == "emotional_reflection":
                        adjacent_emotional += 1
                        _add_hard(
                            hard,
                            anomalies,
                            seed,
                            "adjacent_emotional_reflection",
                        )

            if not _trigger_satisfied(attempt, config) or not _required_context_satisfied(attempt):
                unmet_context_count += 1
                _add_hard(hard, anomalies, seed, "context_or_trigger_violation")
            if row.requires_reply or "?" in row.text or "？" in row.text:
                question_count += 1
                _add_hard(hard, anomalies, seed, "question_or_reply_violation")

            if row.id in last_id:
                elapsed_hours = (now - last_id[row.id]).total_seconds() / 3600
                if elapsed_hours + _EPSILON < row.cooldown_hours:
                    id_cooldown_repeats += 1
                    _add_hard(hard, anomalies, seed, "id_cooldown_violation")
            if row.semantic_group in last_semantic:
                elapsed_hours = (now - last_semantic[row.semantic_group]).total_seconds() / 3600
                if elapsed_hours + _EPSILON < row.semantic_cooldown_hours:
                    semantic_cooldown_repeats += 1
                    _add_hard(hard, anomalies, seed, "semantic_cooldown_violation")

            daily_key = (now.date(), row.id)
            daily_ids[daily_key] += 1
            if daily_ids[daily_key] > row.max_per_day:
                _add_hard(hard, anomalies, seed, "max_per_day_violation")

            rolling_hour = [
                played_at
                for played_at in rolling_hour
                if now - played_at < timedelta(hours=1)
            ]
            rolling_hour.append(now)
            max_outputs_per_hour = max(max_outputs_per_hour, len(rolling_hour))
            if len(rolling_hour) > config.max_outputs_per_hour:
                _add_hard(hard, anomalies, seed, "hourly_budget_violation")

            if attempt.context.daypart == "late_night":
                rolling_late = [
                    played_at
                    for played_at in rolling_late
                    if now - played_at < timedelta(hours=1)
                ]
                rolling_late.append(now)
                if len(rolling_late) > config.late_night_max_outputs_per_hour:
                    _add_hard(hard, anomalies, seed, "late_night_budget_violation")

            recent_rows.append(row)
            if (
                sum(
                    item.category_group == "technical"
                    for item in recent_rows[-config.technical_recent_window :]
                )
                > config.technical_recent_max
            ):
                _add_hard(hard, anomalies, seed, "recent_technical_violation")
            if (
                sum(
                    item.output_mode == "user_direct"
                    for item in recent_rows[-config.user_direct_recent_window :]
                )
                > config.user_direct_recent_max
            ):
                _add_hard(hard, anomalies, seed, "recent_user_direct_violation")
            if (
                sum(
                    item.category_group == "easter_egg"
                    for item in recent_rows[-config.easter_egg_recent_window :]
                )
                > config.easter_egg_recent_max
            ):
                _add_hard(hard, anomalies, seed, "recent_easter_egg_violation")

            last_id[row.id] = now
            last_semantic[row.semantic_group] = now
            previous = attempt

        seed_output_count = len(outputs)
        seed_group_ratio = _ratio(seed_group_counts, CATEGORY_GROUPS, seed_output_count)
        seed_mode_ratio = _ratio(seed_mode_counts, OUTPUT_MODES, seed_output_count)
        if seed_output_count:
            if not 0.10 <= seed_group_ratio["technical"] <= 0.20:
                _add_hard(hard, anomalies, seed, "technical_ratio_out_of_bounds")
            if seed_group_ratio["easter_egg"] > 0.02:
                _add_hard(hard, anomalies, seed, "easter_egg_ratio_above_limit")
            if seed_mode_ratio["self_talk"] + seed_mode_ratio["ambient"] < 0.65:
                _add_hard(hard, anomalies, seed, "self_ambient_ratio_below_minimum")
            if seed_mode_ratio["user_direct"] > 0.15:
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
        if not 0.10 <= group_ratio["technical"] <= 0.20:
            hard.add("technical_ratio_out_of_bounds")
        if group_ratio["easter_egg"] > 0.02:
            hard.add("easter_egg_ratio_above_limit")
        if mode_ratio["self_talk"] + mode_ratio["ambient"] < 0.65:
            hard.add("self_ambient_ratio_below_minimum")
        if mode_ratio["user_direct"] > 0.15:
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
        hard_violations=sorted(hard),
    )


def simulate(
    corpus: Sequence[CorpusLine],
    config: SchedulerConfig | Mapping[str, object],
    days: int,
    seeds: Sequence[int],
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

    attempts: list[SimulationAttempt] = []
    for seed in canonical_seeds:
        history = SelectionHistory()
        last_output_at: datetime | None = None
        for day_index in range(days):
            for slot_index, (hour, minute, event) in enumerate(ATTEMPT_SLOTS):
                now = SIMULATION_START + timedelta(
                    days=day_index, hours=hour, minutes=minute
                )
                elapsed = (
                    max(1440.0, float(scheduler.long_silence_minutes))
                    if last_output_at is None
                    else (now - last_output_at).total_seconds() / 60
                )
                holiday = SIMULATED_HOLIDAYS.get((now.month, now.day))
                anniversary_days = (
                    ANNIVERSARY_DAYS if day_index == ANNIVERSARY_DAY_INDEX else 0
                )
                context = PersonaContext.from_datetime(
                    now,
                    event=event,
                    holiday=holiday,
                    anniversary_days=anniversary_days,
                    minutes_since_last_output=elapsed,
                    ide_foreground=None,
                    active_minutes=None,
                    idle_return=None,
                    fullscreen=None,
                )
                selected = select_line(
                    rows,
                    context,
                    history,
                    now,
                    seed=derive_subseed(
                        seed=seed,
                        day_index=day_index,
                        slot_index=slot_index,
                        corpus_sha256=corpus_digest,
                        scheduler_config_sha256=config_digest,
                        scenario=f"natural:{event}:{hour:02d}:{minute:02d}",
                    ),
                    scheduler_config=scheduler,
                )
                row = selected.row if selected is not None else None
                if row is not None:
                    last_output_at = now
                attempts.append(
                    SimulationAttempt(
                        seed=seed,
                        attempted_at=now,
                        context=context,
                        row=row,
                    )
                )

    return _analyse(
        corpus_sha256=corpus_digest,
        config_sha256=config_digest,
        config=scheduler,
        days=days,
        seeds=canonical_seeds,
        attempts=tuple(attempts),
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
                    "Hard violations",
                    ", ".join(report.hard_violations) if report.hard_violations else "none",
                ),
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
    catchphrase_lines = sum(any(phrase in text for phrase in CATCHPHRASES) for text in texts)
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
    catchphrase_lines = sum(any(phrase in text for phrase in CATCHPHRASES) for text in texts)
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
    assert isinstance(mode_ratio, Mapping)
    before_lengths = before["length_ratio"]
    after_lengths = after["length_ratio"]
    assert isinstance(before_lengths, Mapping) and isinstance(after_lengths, Mapping)
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
    rewrite, general_count, disabled_count, tone_count, fake_count = _render_rewrite_report(
        corpus=corpus,
        archive=archive,
        review=review,
    )
    manual = _render_manual_review(review, pii)
    _write_lf(Path(audit_after_path), after)
    _write_lf(Path(rewrite_summary_path), rewrite)
    _write_lf(Path(manual_review_path), manual)
    return EditorialReportSummary(
        general_rewrite_examples=general_count,
        disabled_examples=disabled_count,
        tone_fix_examples=tone_count,
        fake_context_examples=fake_count,
        manual_review_items=len(review) + len(pii),
    )


__all__ = [
    "EditorialReportSummary",
    "SeedMetrics",
    "SimulationAttempt",
    "SimulationError",
    "SimulationReport",
    "render_simulation_report",
    "simulate",
    "write_editorial_reports",
]
