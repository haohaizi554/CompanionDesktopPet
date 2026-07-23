from __future__ import annotations

import math
import random
from collections import Counter
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import MappingProxyType
from typing import Mapping, Sequence

from .context import ContextError, PersonaContext, daypart_for
from .history import HistoryFormatError, HistoryRecord, SelectionHistory
from .models import CorpusLine
from .validation import ValidationInputError, load_json_object, validate_config


DEFAULT_CONFIG_PATH = Path(__file__).resolve().parents[2] / "config" / "persona-scheduler.json"
SCORE_HISTORY_WINDOW = 50
SCORE_BAND_WIDTH = 1.0
_EPSILON = 1e-9


class SelectorConfigError(ValueError):
    """Scheduler configuration is malformed or violates the validated contract."""


@dataclass(frozen=True, slots=True)
class SchedulerConfig:
    schema_version: int
    category_group_weights: Mapping[str, float]
    output_mode_targets: Mapping[str, float]
    minimum_interval_minutes: int
    max_outputs_per_hour: int
    late_night_max_outputs_per_hour: int
    semantic_group_no_repeat: bool
    block_adjacent_category_groups: frozenset[str]
    technical_recent_window: int
    technical_recent_max: int
    user_direct_recent_window: int
    user_direct_recent_max: int
    easter_egg_recent_window: int
    easter_egg_recent_max: int
    long_silence_minutes: int
    interrupt_cost_minimum_intervals_minutes: Mapping[int, int]
    context_tokens: frozenset[str]
    mvp_triggers: frozenset[str]
    future_triggers: frozenset[str]

    @classmethod
    def from_mapping(cls, value: Mapping[str, object]) -> SchedulerConfig:
        report = validate_config(value)
        if report.errors:
            detail = "; ".join(f"{issue.code}: {issue.message}" for issue in report.errors)
            raise SelectorConfigError(detail)
        try:
            group_weights = value["category_group_weights"]
            mode_targets = value["output_mode_targets"]
            limits = value["runtime_limits"]
            context_tokens = value["context_tokens"]
            mvp_triggers = value["mvp_triggers"]
            future_triggers = value["future_triggers"]
            assert isinstance(group_weights, Mapping)
            assert isinstance(mode_targets, Mapping)
            assert isinstance(limits, Mapping)
            assert isinstance(context_tokens, list)
            assert isinstance(mvp_triggers, list)
            assert isinstance(future_triggers, list)
            intervals = limits["interrupt_cost_minimum_intervals_minutes"]
            assert isinstance(intervals, Mapping)
            return cls(
                schema_version=1,
                category_group_weights=MappingProxyType(
                    {str(name): float(weight) for name, weight in group_weights.items()}
                ),
                output_mode_targets=MappingProxyType(
                    {str(name): float(weight) for name, weight in mode_targets.items()}
                ),
                minimum_interval_minutes=int(limits["minimum_interval_minutes"]),
                max_outputs_per_hour=int(limits["max_outputs_per_hour"]),
                late_night_max_outputs_per_hour=int(
                    limits["late_night_max_outputs_per_hour"]
                ),
                semantic_group_no_repeat=bool(limits["semantic_group_no_repeat"]),
                block_adjacent_category_groups=frozenset(
                    str(group) for group in limits["block_adjacent_category_groups"]
                ),
                technical_recent_window=int(limits["technical_recent_window"]),
                technical_recent_max=int(limits["technical_recent_max"]),
                user_direct_recent_window=int(limits["user_direct_recent_window"]),
                user_direct_recent_max=int(limits["user_direct_recent_max"]),
                easter_egg_recent_window=int(limits["easter_egg_recent_window"]),
                easter_egg_recent_max=int(limits["easter_egg_recent_max"]),
                long_silence_minutes=int(limits["long_silence_minutes"]),
                interrupt_cost_minimum_intervals_minutes=MappingProxyType(
                    {int(cost): int(minutes) for cost, minutes in intervals.items()}
                ),
                context_tokens=frozenset(str(token) for token in context_tokens),
                mvp_triggers=frozenset(str(trigger) for trigger in mvp_triggers),
                future_triggers=frozenset(str(trigger) for trigger in future_triggers),
            )
        except (AssertionError, KeyError, TypeError, ValueError) as error:
            raise SelectorConfigError("scheduler config cannot be converted") from error


def load_scheduler_config(path: Path = DEFAULT_CONFIG_PATH) -> SchedulerConfig:
    try:
        value = load_json_object(Path(path))
    except ValidationInputError as error:
        raise SelectorConfigError(str(error)) from error
    return SchedulerConfig.from_mapping(value)


DEFAULT_SCHEDULER_CONFIG = load_scheduler_config()


@dataclass(frozen=True, slots=True)
class SelectedLine:
    row: CorpusLine
    score: float
    score_band: int
    reasons: tuple[str, ...]

    @property
    def selected_id(self) -> str:
        return self.row.id


@dataclass(frozen=True, slots=True)
class _ScoredCandidate:
    row: CorpusLine
    score: float
    score_band: int
    reasons: tuple[str, ...]


def _aware(value: datetime) -> bool:
    return isinstance(value, datetime) and value.tzinfo is not None and value.utcoffset() is not None


def _instant(value: datetime) -> datetime:
    return value.astimezone(UTC)


def _elapsed_minutes(now: datetime, played_at: datetime) -> float:
    return (_instant(now) - _instant(played_at)).total_seconds() / 60.0


def _candidate_row_is_safe(row: object, config: SchedulerConfig) -> bool:
    if not isinstance(row, CorpusLine) or row.enabled is not True or row.requires_reply is not False:
        return False
    required_strings = (
        row.id,
        row.category,
        row.category_group,
        row.semantic_group,
        row.output_mode,
        row.trigger,
        row.required_context,
    )
    if any(not isinstance(value, str) or not value or value != value.strip() for value in required_strings):
        return False
    if row.category_group not in config.category_group_weights:
        return False
    if row.output_mode not in config.output_mode_targets:
        return False
    if row.trigger not in config.mvp_triggers | config.future_triggers:
        return False
    if (
        isinstance(row.interrupt_cost, bool)
        or not isinstance(row.interrupt_cost, int)
        or row.interrupt_cost not in config.interrupt_cost_minimum_intervals_minutes
    ):
        return False
    numbers = (row.cooldown_hours, row.semantic_cooldown_hours, row.weight)
    if any(
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) <= 0
        for value in numbers
    ):
        return False
    if (
        isinstance(row.max_per_day, bool)
        or not isinstance(row.max_per_day, int)
        or row.max_per_day < 1
    ):
        return False
    return True


def _context_tokens(required_context: str, config: SchedulerConfig) -> tuple[str, ...] | None:
    tokens = tuple(required_context.split(","))
    if (
        not tokens
        or any(not token or token != token.strip() for token in tokens)
        or len(tokens) != len(set(tokens))
        or any(token not in config.context_tokens for token in tokens)
        or ("none" in tokens and tokens != ("none",))
    ):
        return None
    return tokens


def _trigger_matches(
    trigger: str,
    context: PersonaContext,
    elapsed_minutes: float,
    config: SchedulerConfig,
) -> bool:
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
        return elapsed_minutes + _EPSILON >= config.long_silence_minutes
    if trigger == "ide_foreground":
        return context.ide_foreground is True
    if trigger == "long_active":
        return context.active_minutes is not None and context.active_minutes >= 90
    if trigger == "idle_return":
        return context.idle_return is True
    return False


def _most_recent(records: Sequence[HistoryRecord], predicate) -> HistoryRecord | None:
    return next((record for record in reversed(records) if predicate(record)), None)


def _outside_cooldown(now: datetime, previous: HistoryRecord | None, hours: float) -> bool:
    return previous is None or _elapsed_minutes(now, previous.played_at) + _EPSILON >= hours * 60


def _candidate_window_count(
    records: Sequence[HistoryRecord],
    window: int,
    candidate_matches: bool,
    predicate,
) -> int:
    preceding = records[-max(0, window - 1) :] if window > 1 else ()
    return sum(predicate(record) for record in preceding) + int(candidate_matches)


def _score(
    row: CorpusLine,
    records: Sequence[HistoryRecord],
    config: SchedulerConfig,
) -> _ScoredCandidate:
    recent = records[-SCORE_HISTORY_WINDOW:]
    total = len(recent)
    group_count = sum(record.category_group == row.category_group for record in recent)
    mode_count = sum(record.output_mode == row.output_mode for record in recent)
    category_count = sum(record.category == row.category for record in recent)
    group_observed = group_count / total if total else 0.0
    mode_observed = mode_count / total if total else 0.0
    category_observed = category_count / total if total else 0.0
    group_target = config.category_group_weights[row.category_group]
    mode_target = config.output_mode_targets[row.output_mode]
    group_deficit = group_target - group_observed
    mode_deficit = mode_target - mode_observed
    row_weight_bonus = float(row.weight) * 0.5
    interrupt_penalty = row.interrupt_cost * 0.75
    category_repeat_penalty = category_observed * 5.0
    score = (
        group_deficit * 100.0
        + mode_deficit * 35.0
        + row_weight_bonus
        - interrupt_penalty
        - category_repeat_penalty
    )
    band = math.floor(score / SCORE_BAND_WIDTH)
    reasons = (
        f"group_deficit={group_deficit:.6f}",
        f"group_target={group_target:.6f}",
        f"group_observed={group_observed:.6f}",
        f"output_mode_deficit={mode_deficit:.6f}",
        f"output_mode_target={mode_target:.6f}",
        f"output_mode_observed={mode_observed:.6f}",
        f"row_weight_bonus={row_weight_bonus:.6f}",
        f"interrupt_penalty={interrupt_penalty:.6f}",
        f"category_repeat_penalty={category_repeat_penalty:.6f}",
    )
    return _ScoredCandidate(row=row, score=score, score_band=band, reasons=reasons)


def _weighted_choice(candidates: Sequence[_ScoredCandidate], seed: int | None) -> _ScoredCandidate:
    rng = random.Random(seed)
    total = math.fsum(float(candidate.row.weight) for candidate in candidates)
    point = rng.random() * total
    cumulative = 0.0
    for candidate in candidates:
        cumulative += float(candidate.row.weight)
        if point < cumulative:
            return candidate
    return candidates[-1]


def _coerce_config(value: SchedulerConfig | Mapping[str, object] | None) -> SchedulerConfig:
    if value is None:
        return DEFAULT_SCHEDULER_CONFIG
    if isinstance(value, SchedulerConfig):
        return value
    if isinstance(value, Mapping):
        return SchedulerConfig.from_mapping(value)
    raise SelectorConfigError("scheduler_config must be SchedulerConfig, mapping or None")


def select_line(
    corpus: Sequence[CorpusLine],
    context: PersonaContext,
    history: SelectionHistory,
    now: datetime,
    seed: int | None = None,
    *,
    scheduler_config: SchedulerConfig | Mapping[str, object] | None = None,
) -> SelectedLine | None:
    """Run the documented twelve-stage selector and append only a successful choice."""

    if (
        not _aware(now)
        or not isinstance(context, PersonaContext)
        or not isinstance(history, SelectionHistory)
        or (seed is not None and (isinstance(seed, bool) or not isinstance(seed, int)))
    ):
        return None
    try:
        config = _coerce_config(scheduler_config)
        context.validate_for(now)
        context_tokens = context.controlled_tokens(now)
        history.validate_for(now)
    except (ContextError, HistoryFormatError, SelectorConfigError, TypeError, ValueError):
        return None

    records = history.records
    actual_elapsed = (
        _elapsed_minutes(now, records[-1].played_at)
        if records
        else float(context.minutes_since_last_output)
    )
    if actual_elapsed < -_EPSILON:
        return None

    # 1. enabled=true, safe rows only; duplicate IDs are excluded rather than made order-dependent.
    enabled = [row for row in corpus if _candidate_row_is_safe(row, config)]
    id_counts = Counter(row.id for row in enabled)
    candidates = sorted((row for row in enabled if id_counts[row.id] == 1), key=lambda row: row.id)

    # 2. exact trigger match.
    candidates = [
        row for row in candidates if _trigger_matches(row.trigger, context, actual_elapsed, config)
    ]

    # 3. all controlled required_context tokens must be demonstrably true.
    context_filtered: list[CorpusLine] = []
    for row in candidates:
        required = _context_tokens(row.required_context, config)
        if required is not None and (
            required == ("none",) or all(token in context_tokens for token in required)
        ):
            context_filtered.append(row)
    candidates = context_filtered

    # 4. per-ID cooldown (the exact boundary is allowed).
    candidates = [
        row
        for row in candidates
        if _outside_cooldown(
            now,
            _most_recent(records, lambda record, row=row: record.selected_id == row.id),
            float(row.cooldown_hours),
        )
    ]

    # 5. semantic-group cooldown (the exact boundary is allowed).
    candidates = [
        row
        for row in candidates
        if _outside_cooldown(
            now,
            _most_recent(
                records,
                lambda record, row=row: record.semantic_group == row.semantic_group,
            ),
            float(row.semantic_cooldown_hours),
        )
    ]

    # 6. max_per_day is evaluated in now's local timezone.
    local_date = now.date()
    candidates = [
        row
        for row in candidates
        if sum(
            record.selected_id == row.id
            and record.played_at.astimezone(now.tzinfo).date() == local_date
            for record in records
        )
        < row.max_per_day
    ]

    # 7. adjacent semantic/group bans and candidate-aware group windows.
    last = records[-1] if records else None
    group_filtered: list[CorpusLine] = []
    for row in candidates:
        if config.semantic_group_no_repeat and last is not None and last.semantic_group == row.semantic_group:
            continue
        if (
            last is not None
            and row.category_group in config.block_adjacent_category_groups
            and last.category_group == row.category_group
        ):
            continue
        if row.category_group == "technical" and _candidate_window_count(
            records,
            config.technical_recent_window,
            True,
            lambda record: record.category_group == "technical",
        ) > config.technical_recent_max:
            continue
        if row.category_group == "easter_egg" and _candidate_window_count(
            records,
            config.easter_egg_recent_window,
            True,
            lambda record: record.category_group == "easter_egg",
        ) > config.easter_egg_recent_max:
            continue
        group_filtered.append(row)
    candidates = group_filtered

    # 8. candidate-aware output-mode repetition window.
    candidates = [
        row
        for row in candidates
        if row.output_mode != "user_direct"
        or _candidate_window_count(
            records,
            config.user_direct_recent_window,
            True,
            lambda record: record.output_mode == "user_direct",
        )
        <= config.user_direct_recent_max
    ]

    # 9. interruption budgets use absolute rolling windows (now-60m, now].
    if records and actual_elapsed + _EPSILON < config.minimum_interval_minutes:
        candidates = []
    rolling_hour = [
        record
        for record in records
        if -_EPSILON <= _elapsed_minutes(now, record.played_at) < 60.0 - _EPSILON
    ]
    if len(rolling_hour) >= config.max_outputs_per_hour:
        candidates = []
    if context.daypart == "late_night":
        late_night_hour = [
            record
            for record in rolling_hour
            if daypart_for(record.played_at.astimezone(now.tzinfo)) == "late_night"
        ]
        if len(late_night_hour) >= config.late_night_max_outputs_per_hour:
            candidates = []
    if records:
        candidates = [
            row
            for row in candidates
            if actual_elapsed + _EPSILON
            >= config.interrupt_cost_minimum_intervals_minutes[row.interrupt_cost]
        ]

    if not candidates:
        return None

    # 10. group/output deficits, row weight and interruption cost form an explicit score.
    scored = [_score(row, records, config) for row in candidates]

    # 11. weighted local-RNG choice is restricted to the single highest integer score band.
    highest_band = max(candidate.score_band for candidate in scored)
    highest = [candidate for candidate in scored if candidate.score_band == highest_band]
    chosen = _weighted_choice(highest, seed)

    # 12. history mutates exactly once and only after a candidate is selected.
    try:
        history.append(
            HistoryRecord(
                selected_id=chosen.row.id,
                played_at=now,
                category=chosen.row.category,
                category_group=chosen.row.category_group,
                semantic_group=chosen.row.semantic_group,
                output_mode=chosen.row.output_mode,
                trigger=chosen.row.trigger,
                interrupt_cost=chosen.row.interrupt_cost,
            )
        )
    except HistoryFormatError:
        return None
    return SelectedLine(
        row=chosen.row,
        score=float(chosen.score),
        score_band=chosen.score_band,
        reasons=chosen.reasons,
    )
