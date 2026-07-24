from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass, replace
from datetime import datetime, timedelta, timezone
from typing import Protocol, Sequence

from ..context import ContextError, PersonaContext, daypart_for
from ..history import HistoryRecord, SelectionHistory
from ..models import CorpusLine
from ..selector import SchedulerConfig, select_line


_EPSILON = 1e-9
_FIXTURE_START = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))


class AttemptLike(Protocol):
    seed: int
    attempted_at: datetime
    context: PersonaContext
    row: CorpusLine | None


@dataclass(frozen=True, slots=True)
class ConstraintViolation:
    code: str
    seed: int
    selected_id: str
    attempted_at: datetime


@dataclass(frozen=True, slots=True)
class ConstraintAnalysis:
    violations: tuple[ConstraintViolation, ...]

    @property
    def codes(self) -> tuple[str, ...]:
        return tuple(sorted({violation.code for violation in self.violations}))


@dataclass(frozen=True, slots=True)
class AdversarialCaseResult:
    name: str
    required_codes: tuple[str, ...]
    forbidden_codes: tuple[str, ...]
    observed_codes: tuple[str, ...]
    selector_checked: bool
    selector_expected_selected: bool
    selector_selected: bool

    @property
    def passed(self) -> bool:
        observed = set(self.observed_codes)
        return (
            set(self.required_codes) <= observed
            and not (set(self.forbidden_codes) & observed)
            and self.selector_checked
            and self.selector_selected is self.selector_expected_selected
        )


@dataclass(frozen=True, slots=True)
class AdversarialSuiteResult:
    cases: tuple[AdversarialCaseResult, ...]
    hard_violations: tuple[str, ...]


def _trigger_satisfied(
    attempt: AttemptLike,
    row: CorpusLine,
    elapsed_minutes: float,
    config: SchedulerConfig,
) -> bool:
    trigger = row.trigger
    context = attempt.context
    actual_daypart = daypart_for(attempt.attempted_at)
    if trigger == "any":
        return True
    if trigger == "app_start":
        return context.event == "app_start"
    if trigger == "day_changed":
        return context.event == "day_changed"
    if trigger in {"morning", "noon", "afternoon", "evening", "late_night"}:
        return actual_daypart == trigger
    if trigger == "weekday":
        return attempt.attempted_at.isoweekday() < 6
    if trigger == "weekend":
        return attempt.attempted_at.isoweekday() >= 6
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


def _required_context_satisfied(attempt: AttemptLike, row: CorpusLine) -> bool:
    required = tuple(row.required_context.split(","))
    if not required or any(not token for token in required):
        return False
    if required == ("none",):
        return True
    try:
        controlled = attempt.context.controlled_tokens(attempt.attempted_at)
    except ContextError:
        return False
    return all(token in controlled for token in required)


def analyze_constraints(
    attempts: Sequence[AttemptLike],
    config: SchedulerConfig,
) -> ConstraintAnalysis:
    """Recompute scheduler hard limits from a supplied output trace."""

    by_seed: dict[int, list[AttemptLike]] = defaultdict(list)
    for attempt in attempts:
        if attempt.row is not None:
            by_seed[attempt.seed].append(attempt)

    violations: list[ConstraintViolation] = []

    def add(code: str, attempt: AttemptLike) -> None:
        assert attempt.row is not None
        violations.append(
            ConstraintViolation(
                code=code,
                seed=attempt.seed,
                selected_id=attempt.row.id,
                attempted_at=attempt.attempted_at,
            )
        )

    for seed in sorted(by_seed):
        outputs = sorted(by_seed[seed], key=lambda item: item.attempted_at)
        previous: AttemptLike | None = None
        last_id: dict[str, datetime] = {}
        last_semantic: dict[str, datetime] = {}
        daily_ids: Counter[tuple[object, str]] = Counter()
        rolling_hour: list[datetime] = []
        rolling_late_night: list[datetime] = []
        recent_rows: list[CorpusLine] = []

        for attempt in outputs:
            assert attempt.row is not None
            row = attempt.row
            now = attempt.attempted_at
            elapsed_for_trigger = float(attempt.context.minutes_since_last_output)
            if previous is not None and previous.row is not None:
                elapsed_minutes = (now - previous.attempted_at).total_seconds() / 60.0
                elapsed_for_trigger = elapsed_minutes
                if (
                    abs(
                        elapsed_minutes
                        - float(attempt.context.minutes_since_last_output)
                    )
                    > _EPSILON
                ):
                    add("elapsed_context_mismatch", attempt)
                if elapsed_minutes + _EPSILON < config.minimum_interval_minutes:
                    add("minimum_interval_violation", attempt)
                required = config.interrupt_cost_minimum_intervals_minutes[
                    row.interrupt_cost
                ]
                if elapsed_minutes + _EPSILON < required:
                    add("interrupt_budget_violation", attempt)
                if (
                    config.semantic_group_no_repeat
                    and row.semantic_group == previous.row.semantic_group
                ):
                    add("adjacent_semantic_violation", attempt)
                if (
                    row.category_group in config.block_adjacent_category_groups
                    and row.category_group == previous.row.category_group
                ):
                    add(f"adjacent_group_violation:{row.category_group}", attempt)

            if not _trigger_satisfied(
                attempt,
                row,
                elapsed_for_trigger,
                config,
            ) or not _required_context_satisfied(attempt, row):
                add("context_or_trigger_violation", attempt)
            if row.requires_reply or "?" in row.text or "？" in row.text:
                add("question_or_reply_violation", attempt)

            if row.id in last_id:
                elapsed_hours = (now - last_id[row.id]).total_seconds() / 3600.0
                if elapsed_hours + _EPSILON < float(row.cooldown_hours):
                    add("id_cooldown_violation", attempt)
            if row.semantic_group in last_semantic:
                elapsed_hours = (
                    now - last_semantic[row.semantic_group]
                ).total_seconds() / 3600.0
                if elapsed_hours + _EPSILON < float(row.semantic_cooldown_hours):
                    add("semantic_cooldown_violation", attempt)

            daily_key = (now.date(), row.id)
            daily_ids[daily_key] += 1
            if daily_ids[daily_key] > row.max_per_day:
                add("max_per_day_violation", attempt)

            rolling_hour = [
                played_at
                for played_at in rolling_hour
                if now - played_at < timedelta(hours=1)
            ]
            rolling_hour.append(now)
            if len(rolling_hour) > config.max_outputs_per_hour:
                add("hourly_budget_violation", attempt)

            if daypart_for(now) == "late_night":
                rolling_late_night = [
                    played_at
                    for played_at in rolling_late_night
                    if now - played_at < timedelta(hours=1)
                ]
                rolling_late_night.append(now)
                if len(rolling_late_night) > config.late_night_max_outputs_per_hour:
                    add("late_night_budget_violation", attempt)

            recent_rows.append(row)
            if (
                sum(
                    item.category_group == "technical"
                    for item in recent_rows[-config.technical_recent_window :]
                )
                > config.technical_recent_max
            ):
                add("recent_technical_violation", attempt)
            if (
                sum(
                    item.output_mode == "user_direct"
                    for item in recent_rows[-config.user_direct_recent_window :]
                )
                > config.user_direct_recent_max
            ):
                add("recent_user_direct_violation", attempt)
            if (
                sum(
                    item.category_group == "easter_egg"
                    for item in recent_rows[-config.easter_egg_recent_window :]
                )
                > config.easter_egg_recent_max
            ):
                add("recent_easter_egg_violation", attempt)

            last_id[row.id] = now
            last_semantic[row.semantic_group] = now
            previous = attempt

    return ConstraintAnalysis(tuple(violations))


@dataclass(frozen=True, slots=True)
class _FixtureAttempt:
    seed: int
    attempted_at: datetime
    context: PersonaContext
    row: CorpusLine | None


def _fixture_row(
    index: int,
    *,
    category_group: str = "character_life",
    output_mode: str = "self_talk",
    interrupt_cost: int = 0,
    max_per_day: int = 20,
) -> CorpusLine:
    return CorpusLine(
        id=f"adversarial-{index}",
        category=f"adversarial-category-{index}",
        category_group=category_group,
        topic_id=f"adversarial-topic-{index}",
        semantic_group=f"adversarial-semantic-{index}",
        output_mode=output_mode,
        trigger="any",
        required_context="none",
        tone="factual",
        interrupt_cost=interrupt_cost,
        cooldown_hours=0.01,
        semantic_cooldown_hours=0.01,
        max_per_day=max_per_day,
        weight=1.0,
        requires_reply=False,
        enabled=True,
        text=f"adversarial fixture {index}",
        source_kind="editorial",
        source_reference=f"adversarial:{index}",
        rewrite_reason="constraint_fixture",
    )


def _fixture_attempt(
    index: int,
    when: datetime,
    elapsed_minutes: float,
    **row_values: object,
) -> _FixtureAttempt:
    row = _fixture_row(index, **row_values)  # type: ignore[arg-type]
    return _FixtureAttempt(
        seed=0,
        attempted_at=when,
        context=PersonaContext.from_datetime(
            when,
            minutes_since_last_output=elapsed_minutes,
        ),
        row=row,
    )


def _safe_groups(config: SchedulerConfig) -> tuple[str, ...]:
    preferred = (
        "character_life",
        "growth",
        "career",
        "system_ambient",
        "daily_care",
        "emotional_reflection",
        "technical",
        "easter_egg",
    )
    available = tuple(group for group in preferred if group in config.category_group_weights)
    return available or tuple(sorted(config.category_group_weights))


def _pair(
    *,
    gap_seconds: int,
    second_cost: int,
    config: SchedulerConfig,
    start: datetime = _FIXTURE_START,
    index: int = 0,
) -> tuple[_FixtureAttempt, _FixtureAttempt]:
    groups = _safe_groups(config)
    first_group = groups[0]
    second_group = groups[1] if len(groups) > 1 else groups[0]
    return (
        _fixture_attempt(
            index,
            start,
            1440,
            category_group=first_group,
        ),
        _fixture_attempt(
            index + 1,
            start + timedelta(seconds=gap_seconds),
            gap_seconds / 60.0,
            category_group=second_group,
            interrupt_cost=second_cost,
        ),
    )


def _selector_accepts_last(
    attempts: Sequence[_FixtureAttempt],
    config: SchedulerConfig,
) -> bool:
    if not attempts or attempts[-1].row is None:
        return False
    history_records: list[HistoryRecord] = []
    for attempt in attempts[:-1]:
        if attempt.row is None:
            continue
        row = attempt.row
        history_records.append(
            HistoryRecord(
                selected_id=row.id,
                played_at=attempt.attempted_at,
                category=row.category,
                category_group=row.category_group,
                semantic_group=row.semantic_group,
                output_mode=row.output_mode,
                trigger=row.trigger,
                interrupt_cost=row.interrupt_cost,
            )
        )
    candidate = attempts[-1]
    selected = select_line(
        (candidate.row,),
        candidate.context,
        SelectionHistory(history_records),
        candidate.attempted_at,
        seed=0,
        scheduler_config=config,
    )
    return selected is not None


def run_adversarial_suite(config: SchedulerConfig) -> AdversarialSuiteResult:
    """Exercise both sides of every scheduler boundary with synthetic traces."""

    cases: list[AdversarialCaseResult] = []

    def evaluate(
        name: str,
        attempts: Sequence[_FixtureAttempt],
        *,
        required: Sequence[str] = (),
        forbidden: Sequence[str] = (),
        selector_expected_selected: bool,
    ) -> None:
        observed = analyze_constraints(attempts, config).codes
        selector_selected = _selector_accepts_last(attempts, config)
        cases.append(
            AdversarialCaseResult(
                name=name,
                required_codes=tuple(required),
                forbidden_codes=tuple(forbidden),
                observed_codes=observed,
                selector_checked=True,
                selector_expected_selected=selector_expected_selected,
                selector_selected=selector_selected,
            )
        )

    minimum_seconds = config.minimum_interval_minutes * 60
    evaluate(
        "minimum_interval:7m59s:reject"
        if config.minimum_interval_minutes == 8
        else f"minimum_interval:{config.minimum_interval_minutes}m-minus-1s:reject",
        _pair(
            gap_seconds=minimum_seconds - 1,
            second_cost=0,
            config=config,
            index=10,
        ),
        required=("minimum_interval_violation",),
        selector_expected_selected=False,
    )
    evaluate(
        "minimum_interval:8m00s:allow"
        if config.minimum_interval_minutes == 8
        else f"minimum_interval:{config.minimum_interval_minutes}m:allow",
        _pair(
            gap_seconds=minimum_seconds,
            second_cost=0,
            config=config,
            index=20,
        ),
        forbidden=("minimum_interval_violation",),
        selector_expected_selected=True,
    )

    for cost, minutes in sorted(
        config.interrupt_cost_minimum_intervals_minutes.items()
    ):
        if cost == 0:
            continue
        evaluate(
            f"interrupt_cost:{cost}:{minutes}m:reject_below",
            _pair(
                gap_seconds=minutes * 60 - 1,
                second_cost=cost,
                config=config,
                index=100 + cost * 10,
            ),
            required=("interrupt_budget_violation",),
            selector_expected_selected=False,
        )
        evaluate(
            f"interrupt_cost:{cost}:{minutes}m:allow_exact",
            _pair(
                gap_seconds=minutes * 60,
                second_cost=cost,
                config=config,
                index=200 + cost * 10,
            ),
            forbidden=("interrupt_budget_violation",),
            selector_expected_selected=True,
        )

    groups = _safe_groups(config)
    gap = max(config.minimum_interval_minutes, config.interrupt_cost_minimum_intervals_minutes[0])
    hourly = tuple(
        _fixture_attempt(
            300 + index,
            _FIXTURE_START + timedelta(minutes=gap * index),
            1440 if index == 0 else gap,
            category_group=groups[index % len(groups)],
        )
        for index in range(config.max_outputs_per_hour + 1)
    )
    evaluate(
        "rolling_hour:max:reject",
        hourly,
        required=("hourly_budget_violation",),
        selector_expected_selected=False,
    )

    late_start = _FIXTURE_START.replace(hour=1)
    late = tuple(
        _fixture_attempt(
            400 + index,
            late_start + timedelta(minutes=gap * index),
            1440 if index == 0 else gap,
            category_group=groups[index % len(groups)],
        )
        for index in range(config.late_night_max_outputs_per_hour + 1)
    )
    evaluate(
        "late_night:max:reject",
        late,
        required=("late_night_budget_violation",),
        selector_expected_selected=False,
    )

    for index, group in enumerate(sorted(config.block_adjacent_category_groups)):
        adjacent = (
            _fixture_attempt(
                500 + index * 2,
                _FIXTURE_START,
                1440,
                category_group=group,
            ),
            _fixture_attempt(
                501 + index * 2,
                _FIXTURE_START + timedelta(minutes=gap),
                gap,
                category_group=group,
            ),
        )
        evaluate(
            f"adjacent_group:{group}:reject",
            adjacent,
            required=(f"adjacent_group_violation:{group}",),
            selector_expected_selected=False,
        )

    daily_first, daily_second = _pair(
        gap_seconds=gap * 60,
        second_cost=0,
        config=config,
        index=600,
    )
    assert daily_first.row is not None and daily_second.row is not None
    repeated_row = replace(daily_first.row, max_per_day=1)
    daily = (
        replace(daily_first, row=repeated_row),
        replace(daily_second, row=repeated_row),
    )
    evaluate(
        "max_per_day:reject",
        daily,
        required=("max_per_day_violation",),
        selector_expected_selected=False,
    )

    def recent_trace(
        *,
        window: int,
        maximum: int,
        match_group: str | None = None,
        match_mode: str | None = None,
        index_base: int,
    ) -> tuple[_FixtureAttempt, ...]:
        count = max(maximum + 1, 1)
        total = max(window, count)
        trace: list[_FixtureAttempt] = []
        for index in range(total):
            is_match = index < maximum or index == total - 1
            group = match_group if is_match and match_group is not None else groups[0]
            mode = match_mode if is_match and match_mode is not None else "self_talk"
            trace.append(
                _fixture_attempt(
                    index_base + index,
                    _FIXTURE_START + timedelta(minutes=61 * index),
                    1440 if index == 0 else 61,
                    category_group=group,
                    output_mode=mode,
                )
            )
        return tuple(trace)

    evaluate(
        "recent:technical:reject",
        recent_trace(
            window=config.technical_recent_window,
            maximum=config.technical_recent_max,
            match_group="technical",
            index_base=700,
        ),
        required=("recent_technical_violation",),
        selector_expected_selected=False,
    )
    evaluate(
        "recent:user_direct:reject",
        recent_trace(
            window=config.user_direct_recent_window,
            maximum=config.user_direct_recent_max,
            match_mode="user_direct",
            index_base=800,
        ),
        required=("recent_user_direct_violation",),
        selector_expected_selected=False,
    )
    evaluate(
        "recent:easter_egg:reject",
        recent_trace(
            window=config.easter_egg_recent_window,
            maximum=config.easter_egg_recent_max,
            match_group="easter_egg",
            index_base=900,
        ),
        required=("recent_easter_egg_violation",),
        selector_expected_selected=False,
    )

    hard = tuple(
        f"adversarial_case_failed:{case.name}"
        for case in cases
        if not case.passed
    )
    return AdversarialSuiteResult(tuple(cases), hard)
