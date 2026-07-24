from __future__ import annotations

import hashlib
from dataclasses import dataclass, replace
from datetime import datetime, timedelta, timezone
from itertools import product
from types import MappingProxyType
from typing import Iterable, Mapping, Protocol

from ..context import PersonaContext
from ..history import SelectionHistory
from ..models import CorpusLine
from ..selector import SchedulerConfig, select_line


SUBSEED_DERIVATION_VERSION = "persona-simulation-v2"
SUBSEED_DERIVATION_SPEC = (
    "encoding=utf-8;separator=U+001F;"
    "fields=derivation_version,corpus_sha256,scheduler_config_sha256,scenario,"
    "seed,day_index,slot_index;digest=sha256;result=first-8-bytes-big-endian-unsigned"
)
SUBSEED_DERIVATION_SHA256 = hashlib.sha256(
    SUBSEED_DERIVATION_SPEC.encode("utf-8")
).hexdigest()
_LOCAL_TIMEZONE = timezone(timedelta(hours=8))
_SEARCH_START = datetime(2026, 1, 1, tzinfo=_LOCAL_TIMEZONE)
NULLABLE_SIGNAL_COMBINATIONS = tuple(
    product(
        (None, False, True),
        (None, 89, 90, 91),
        (None, False, True),
        (None, False, True),
    )
)


def derive_subseed(
    *,
    seed: int,
    day_index: int,
    slot_index: int,
    corpus_sha256: str,
    scheduler_config_sha256: str,
    scenario: str,
    derivation_version: str = SUBSEED_DERIVATION_VERSION,
) -> int:
    """Derive one deterministic selector seed bound to all replay inputs."""

    identity = "\x1f".join(
        (
            derivation_version,
            corpus_sha256,
            scheduler_config_sha256,
            scenario,
            str(seed),
            str(day_index),
            str(slot_index),
        )
    ).encode("utf-8")
    return int.from_bytes(hashlib.sha256(identity).digest()[:8], "big", signed=False)


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
        return elapsed_minutes >= config.long_silence_minutes
    if trigger == "ide_foreground":
        return context.ide_foreground is True
    if trigger == "long_active":
        return context.active_minutes is not None and context.active_minutes >= 90
    if trigger == "idle_return":
        return context.idle_return is True
    return False


@dataclass(frozen=True, slots=True)
class CandidateIndex:
    """One-pass corpus index reusable across every simulation slot and probe."""

    by_trigger: Mapping[str, tuple[CorpusLine, ...]]
    by_required_context: Mapping[str, tuple[CorpusLine, ...]]
    by_pair: Mapping[tuple[str, str], tuple[CorpusLine, ...]]
    enabled_count: int

    @classmethod
    def build(cls, corpus: Iterable[CorpusLine]) -> CandidateIndex:
        trigger_rows: dict[str, list[CorpusLine]] = {}
        context_rows: dict[str, list[CorpusLine]] = {}
        pair_rows: dict[tuple[str, str], list[CorpusLine]] = {}
        enabled_count = 0
        for row in corpus:
            if not isinstance(row, CorpusLine) or row.enabled is not True:
                continue
            enabled_count += 1
            trigger_rows.setdefault(row.trigger, []).append(row)
            context_rows.setdefault(row.required_context, []).append(row)
            pair_rows.setdefault((row.trigger, row.required_context), []).append(row)

        def freeze(source):
            return MappingProxyType(
                {
                    key: tuple(sorted(rows, key=lambda item: item.id))
                    for key, rows in source.items()
                }
            )

        return cls(
            by_trigger=freeze(trigger_rows),
            by_required_context=freeze(context_rows),
            by_pair=freeze(pair_rows),
            enabled_count=enabled_count,
        )

    def candidates_for(
        self,
        context: PersonaContext,
        now: datetime,
        config: SchedulerConfig,
    ) -> tuple[CorpusLine, ...]:
        """Return a safe superset without scanning the source corpus again."""

        context.validate_for(now)
        controlled = context.controlled_tokens(now)
        elapsed = float(context.minutes_since_last_output)
        candidates: list[CorpusLine] = []
        for trigger in sorted(self.by_trigger):
            if not _trigger_matches(trigger, context, elapsed, config):
                continue
            for row in self.by_trigger[trigger]:
                required = tuple(row.required_context.split(","))
                if required == ("none",) or (
                    required
                    and all(token and token in controlled for token in required)
                ):
                    candidates.append(row)
        return tuple(sorted(candidates, key=lambda item: item.id))


@dataclass(frozen=True, slots=True)
class ScenarioCoverage:
    seasons: tuple[str, ...]
    dayparts: tuple[str, ...]
    dawn: bool
    events: tuple[str, ...]
    weekend_values: tuple[bool, ...]
    holiday: bool
    anniversary: bool
    month_boundary: bool
    ide_foreground_values: tuple[bool | None, ...]
    active_minutes_values: tuple[int | None, ...]
    idle_return_values: tuple[bool | None, ...]
    fullscreen_values: tuple[bool | None, ...]
    nullable_signal_combinations: int


class ScenarioAttemptLike(Protocol):
    attempted_at: datetime
    context: PersonaContext


def summarize_scenario_coverage(
    attempts: Iterable[ScenarioAttemptLike],
) -> ScenarioCoverage:
    """Summarize contexts that actually traversed the natural selector run."""

    seasons: set[str] = set()
    dayparts: set[str] = set()
    events: set[str] = set()
    weekends: set[bool] = set()
    ide_values: set[bool | None] = set()
    active_values: set[int | None] = set()
    idle_values: set[bool | None] = set()
    fullscreen_values: set[bool | None] = set()
    nullable_combinations: set[tuple[bool | None, int | None, bool | None, bool | None]] = set()
    dawn = False
    holiday = False
    anniversary = False
    month_boundary = False

    for attempt in attempts:
        now = attempt.attempted_at
        context = attempt.context
        tokens = context.controlled_tokens(now)
        seasons.update(
            token.removeprefix("season:")
            for token in tokens
            if token.startswith("season:")
        )
        dayparts.add(context.daypart)
        events.add(context.event)
        weekends.add(context.is_weekend)
        ide_values.add(context.ide_foreground)
        active_values.add(context.active_minutes)
        idle_values.add(context.idle_return)
        fullscreen_values.add(context.fullscreen)
        nullable_combinations.add(
            (
                context.ide_foreground,
                context.active_minutes,
                context.idle_return,
                context.fullscreen,
            )
        )
        dawn = dawn or "time:dawn" in tokens
        holiday = holiday or "date:holiday" in tokens
        anniversary = anniversary or "anniversary" in tokens
        month_boundary = month_boundary or "date:month_boundary" in tokens

    return ScenarioCoverage(
        seasons=tuple(
            name
            for name in ("spring", "summer", "autumn", "winter")
            if name in seasons
        ),
        dayparts=tuple(
            name
            for name in ("late_night", "morning", "noon", "afternoon", "evening")
            if name in dayparts
        ),
        dawn=dawn,
        events=tuple(
            name for name in ("tick", "app_start", "day_changed") if name in events
        ),
        weekend_values=tuple(value for value in (False, True) if value in weekends),
        holiday=holiday,
        anniversary=anniversary,
        month_boundary=month_boundary,
        ide_foreground_values=tuple(
            value for value in (None, False, True) if value in ide_values
        ),
        active_minutes_values=tuple(
            value for value in (None, 89, 90, 91) if value in active_values
        ),
        idle_return_values=tuple(
            value for value in (None, False, True) if value in idle_values
        ),
        fullscreen_values=tuple(
            value for value in (None, False, True) if value in fullscreen_values
        ),
        nullable_signal_combinations=len(nullable_combinations),
    )


def build_scenario_coverage() -> ScenarioCoverage:
    """Build and summarize the deterministic context scenario matrix."""

    seasons: set[str] = set()
    dayparts: set[str] = set()
    events: set[str] = set()
    weekends: set[bool] = set()
    dawn = False
    holiday = False
    anniversary = False
    month_boundary = False

    anchors = (
        (_SEARCH_START.replace(month=3, day=2, hour=7), "tick", None, 0),
        (_SEARCH_START.replace(month=6, day=2, hour=12), "app_start", None, 0),
        (_SEARCH_START.replace(month=9, day=1, hour=15), "day_changed", None, 365),
        (_SEARCH_START.replace(month=12, day=5, hour=19), "tick", "holiday", 0),
        (_SEARCH_START.replace(month=7, day=4, hour=1), "tick", None, 0),
        (_SEARCH_START.replace(month=7, day=1, hour=5), "tick", None, 0),
        (_SEARCH_START.replace(month=7, day=1, hour=7), "tick", None, 0),
        (_SEARCH_START.replace(month=7, day=1, hour=12), "tick", None, 0),
        (_SEARCH_START.replace(month=7, day=1, hour=15), "tick", None, 0),
        (_SEARCH_START.replace(month=7, day=1, hour=19), "tick", None, 0),
    )
    for now, event, holiday_name, anniversary_days in anchors:
        context = PersonaContext.from_datetime(
            now,
            event=event,
            holiday=holiday_name,
            anniversary_days=anniversary_days,
        )
        tokens = context.controlled_tokens(now)
        seasons.update(token.removeprefix("season:") for token in tokens if token.startswith("season:"))
        dayparts.add(context.daypart)
        events.add(context.event)
        weekends.add(context.is_weekend)
        dawn = dawn or "time:dawn" in tokens
        holiday = holiday or "date:holiday" in tokens
        anniversary = anniversary or "anniversary" in tokens
        month_boundary = month_boundary or "date:month_boundary" in tokens

    ide_values = (None, False, True)
    active_values = (None, 89, 90, 91)
    idle_values = (None, False, True)
    fullscreen_values = (None, False, True)
    signal_now = _SEARCH_START.replace(month=7, day=1, hour=8)
    for ide, active, idle, fullscreen in NULLABLE_SIGNAL_COMBINATIONS:
        PersonaContext.from_datetime(
            signal_now,
            ide_foreground=ide,
            active_minutes=active,
            idle_return=idle,
            fullscreen=fullscreen,
        ).controlled_tokens(signal_now)

    return ScenarioCoverage(
        seasons=tuple(name for name in ("spring", "summer", "autumn", "winter") if name in seasons),
        dayparts=tuple(
            name
            for name in ("late_night", "morning", "noon", "afternoon", "evening")
            if name in dayparts
        ),
        dawn=dawn,
        events=tuple(name for name in ("tick", "app_start", "day_changed") if name in events),
        weekend_values=tuple(value for value in (False, True) if value in weekends),
        holiday=holiday,
        anniversary=anniversary,
        month_boundary=month_boundary,
        ide_foreground_values=ide_values,
        active_minutes_values=active_values,
        idle_return_values=idle_values,
        fullscreen_values=fullscreen_values,
        nullable_signal_combinations=len(NULLABLE_SIGNAL_COMBINATIONS),
    )


@dataclass(frozen=True, slots=True)
class InventoryCoverage:
    trigger_hits: Mapping[str, str]
    context_hits: Mapping[str, str]
    trigger_misses: tuple[str, ...]
    context_misses: tuple[str, ...]
    unreachable_pairs: tuple[str, ...]


def _matching_context(
    trigger: str,
    required_context: str,
    config: SchedulerConfig,
) -> tuple[datetime, PersonaContext] | None:
    required = tuple(required_context.split(","))
    required_set = set(required)
    event = (
        "app_start"
        if trigger == "app_start" or "app_started" in required_set
        else "day_changed"
        if trigger == "day_changed"
        else "tick"
    )
    if trigger == "app_start" and event != "app_start":
        return None
    holiday = "coverage-holiday" if trigger == "holiday" or required_set & {"holiday", "date:holiday"} else None
    anniversary_days = 365 if trigger == "anniversary" or "anniversary" in required_set else 0
    ide = True if trigger == "ide_foreground" or "ide_foreground" in required_set else None
    active = 90 if trigger == "long_active" or "active_90m" in required_set else None
    idle = True if trigger == "idle_return" or "idle_return" in required_set else None
    fullscreen = False if "not_fullscreen" in required_set else None
    elapsed = max(1440.0, float(config.long_silence_minutes))

    for day_offset in range(366):
        day = _SEARCH_START + timedelta(days=day_offset)
        for hour in range(24):
            now = day.replace(hour=hour, minute=0)
            context = PersonaContext.from_datetime(
                now,
                event=event,
                holiday=holiday,
                anniversary_days=anniversary_days,
                minutes_since_last_output=elapsed,
                ide_foreground=ide,
                active_minutes=active,
                idle_return=idle,
                fullscreen=fullscreen,
            )
            if not _trigger_matches(trigger, context, elapsed, config):
                continue
            tokens = context.controlled_tokens(now)
            if required == ("none",) or all(token in tokens for token in required):
                return now, context
    return None


def probe_inventory_coverage(
    corpus: Iterable[CorpusLine],
    config: SchedulerConfig,
) -> InventoryCoverage:
    """Select an actual stocked row for every reachable trigger/context pair."""

    index = CandidateIndex.build(corpus)
    trigger_hits: dict[str, str] = {}
    context_hits: dict[str, str] = {}
    unreachable_pairs: list[str] = []
    for pair in sorted(index.by_pair):
        trigger, required_context = pair
        match = _matching_context(trigger, required_context, config)
        if match is None:
            unreachable_pairs.append(f"{trigger}|{required_context}")
            continue
        now, context = match
        selected = select_line(
            index.by_pair[pair],
            context,
            SelectionHistory(),
            now,
            seed=0,
            scheduler_config=config,
        )
        if selected is None:
            unreachable_pairs.append(f"{trigger}|{required_context}")
            continue
        trigger_hits.setdefault(trigger, selected.row.id)
        context_hits.setdefault(required_context, selected.row.id)

    stocked_triggers = set(index.by_trigger)
    stocked_contexts = set(index.by_required_context)

    # Dimension probes deliberately neutralize the other dimension. Pair-level
    # reachability remains disclosed separately in ``unreachable_pairs``.
    for trigger in sorted(stocked_triggers - set(trigger_hits)):
        match = _matching_context(trigger, "none", config)
        if match is None:
            continue
        now, context = match
        probe_rows = tuple(
            replace(row, required_context="none") for row in index.by_trigger[trigger]
        )
        selected = select_line(
            probe_rows,
            context,
            SelectionHistory(),
            now,
            seed=0,
            scheduler_config=config,
        )
        if selected is not None:
            trigger_hits[trigger] = selected.row.id

    for required_context in sorted(stocked_contexts - set(context_hits)):
        match = _matching_context("any", required_context, config)
        if match is None:
            continue
        now, context = match
        probe_rows = tuple(
            replace(row, trigger="any")
            for row in index.by_required_context[required_context]
        )
        selected = select_line(
            probe_rows,
            context,
            SelectionHistory(),
            now,
            seed=0,
            scheduler_config=config,
        )
        if selected is not None:
            context_hits[required_context] = selected.row.id

    return InventoryCoverage(
        trigger_hits=MappingProxyType(dict(sorted(trigger_hits.items()))),
        context_hits=MappingProxyType(dict(sorted(context_hits.items()))),
        trigger_misses=tuple(sorted(stocked_triggers - set(trigger_hits))),
        context_misses=tuple(sorted(stocked_contexts - set(context_hits))),
        unreachable_pairs=tuple(sorted(unreachable_pairs)),
    )
