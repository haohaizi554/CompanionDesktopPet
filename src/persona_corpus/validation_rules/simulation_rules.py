"""Independent validation of structured playback simulation output."""

from __future__ import annotations

import calendar
import json
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
from functools import lru_cache
from typing import Mapping, Sequence

from ..context import PersonaContext
from ..contract import PERSONA_CONTRACT
from ..history import SelectionHistory
from ..models import CorpusLine
from ..selector import SchedulerConfig, prepare_corpus, select_line
from ..simulation_core.scenarios import (
    ATTEMPT_SLOTS,
    SIMULATION_SCHEMA_VERSION,
    build_natural_attempt,
    derive_subseed,
)
from ..trigger_matching import (
    time_context_token_matches,
    trigger_matches as _simulation_trigger_matches,
)
from .content_rules import _required_context_tokens
from .core import _Issues, _is_finite_number, _is_integer
from .simulation_coverage_rules import validate_simulation_coverage


SIMULATION_KEYS = frozenset(
    {
        "schema_version",
        "corpus_sha256",
        "scheduler_config_sha256",
        "subseed_derivation_version",
        "subseed_derivation_sha256",
        "days",
        "seeds",
        "attempts",
    }
)
SIMULATION_ATTEMPT_KEYS = frozenset(
    {"seed", "day_index", "slot_index", "attempted_at", "context", "selected_id"}
)
SIMULATION_CONTEXT_KEYS = frozenset(
    {
        "event",
        "daypart",
        "weekday",
        "is_weekend",
        "holiday",
        "anniversary_days",
        "minutes_since_last_output",
        "ide_foreground",
        "active_minutes",
        "idle_return",
        "fullscreen",
    }
)
SIMULATION_EVENTS = frozenset({"tick", "app_start", "day_changed"})
SIMULATION_DAYPARTS = frozenset(
    {"morning", "noon", "afternoon", "evening", "late_night"}
)
# Release evidence covers 30 days x 100 fixed seeds x 5 canonical slots.
# Keep the ceiling exact so malformed coordinates cannot turn validation into
# unbounded scheduler work.
MAX_CANONICAL_REPLAY_ATTEMPTS = 15_000


@dataclass(frozen=True, slots=True)
class _SimulationAttempt:
    source_index: int
    seed: int
    day_index: int
    slot_index: int
    attempted_at_text: str
    attempted_at: datetime
    context: Mapping[str, object]
    selected_id: str | None


@dataclass(frozen=True, slots=True)
class _SimulationOutput:
    attempt: _SimulationAttempt
    row: CorpusLine


@dataclass(frozen=True, slots=True)
class _CanonicalReplayAttempt:
    seed: int
    day_index: int
    slot_index: int
    attempted_at_text: str
    context: PersonaContext
    selected_id: str | None


def _expected_daypart(timestamp: datetime) -> str:
    hour = timestamp.hour
    if 6 <= hour < 11:
        return "morning"
    if 11 <= hour < 14:
        return "noon"
    if 14 <= hour < 18:
        return "afternoon"
    if 18 <= hour < 23:
        return "evening"
    return "late_night"


def _parse_simulation_timestamp(value: object) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if timestamp.tzinfo is None or timestamp.utcoffset() is None:
        return None
    return timestamp


def _valid_optional_boolean(value: object) -> bool:
    return value is None or isinstance(value, bool)


def _simulation_context_valid(
    context: object,
    timestamp: datetime,
) -> bool:
    if not isinstance(context, Mapping) or set(context) != SIMULATION_CONTEXT_KEYS:
        return False
    event = context.get("event")
    daypart = context.get("daypart")
    weekday = context.get("weekday")
    is_weekend = context.get("is_weekend")
    holiday = context.get("holiday")
    anniversary_days = context.get("anniversary_days")
    minutes_since_last_output = context.get("minutes_since_last_output")
    active_minutes = context.get("active_minutes")
    return (
        isinstance(event, str)
        and event in SIMULATION_EVENTS
        and isinstance(daypart, str)
        and daypart in SIMULATION_DAYPARTS
        and daypart == _expected_daypart(timestamp)
        and _is_integer(weekday)
        and weekday == timestamp.isoweekday()
        and isinstance(is_weekend, bool)
        and is_weekend == (timestamp.isoweekday() >= 6)
        and (holiday is None or (isinstance(holiday, str) and bool(holiday.strip())))
        and _is_integer(anniversary_days)
        and anniversary_days >= 0
        and _is_finite_number(minutes_since_last_output)
        and float(minutes_since_last_output) >= 0
        and (active_minutes is None or (_is_integer(active_minutes) and active_minutes >= 0))
        and _valid_optional_boolean(context.get("ide_foreground"))
        and _valid_optional_boolean(context.get("idle_return"))
        and _valid_optional_boolean(context.get("fullscreen"))
    )


def _simulation_context_token_matches(
    token: str,
    context: Mapping[str, object],
    timestamp: datetime,
) -> bool:
    if token == "none":
        return True
    if token == "app_started":
        return context.get("event") == "app_start"
    if token in {"holiday", "date:holiday"}:
        return isinstance(context.get("holiday"), str)
    if token == "anniversary":
        return _is_integer(context.get("anniversary_days")) and context["anniversary_days"] > 0
    if token == "ide_foreground":
        return context.get("ide_foreground") is True
    if token == "active_90m":
        return _is_integer(context.get("active_minutes")) and context["active_minutes"] >= 90
    if token == "idle_return":
        return context.get("idle_return") is True
    if token == "not_fullscreen":
        return context.get("fullscreen") is False
    if token == "day:weekday":
        return timestamp.isoweekday() < 6
    if token == "day:weekend":
        return timestamp.isoweekday() >= 6
    if token.startswith("time:"):
        return time_context_token_matches(token, timestamp.hour)
    season = (
        "spring" if timestamp.month in {3, 4, 5}
        else "summer" if timestamp.month in {6, 7, 8}
        else "autumn" if timestamp.month in {9, 10, 11}
        else "winter"
    )
    if token.startswith("season:"):
        return token == f"season:{season}"
    if token == "date:month_boundary":
        return timestamp.day in {1, calendar.monthrange(timestamp.year, timestamp.month)[1]}
    return False


def _context_payload(context: PersonaContext) -> dict[str, object]:
    return {
        "event": context.event,
        "daypart": context.daypart,
        "weekday": context.weekday,
        "is_weekend": context.is_weekend,
        "holiday": context.holiday,
        "anniversary_days": context.anniversary_days,
        "minutes_since_last_output": float(context.minutes_since_last_output),
        "ide_foreground": context.ide_foreground,
        "active_minutes": context.active_minutes,
        "idle_return": context.idle_return,
        "fullscreen": context.fullscreen,
    }


def _matches_canonical_context(
    recorded: Mapping[str, object],
    expected: PersonaContext,
) -> bool:
    expected_payload = _context_payload(expected)
    return set(recorded) == set(expected_payload) and all(
        type(recorded[key]) is type(expected_value)
        and recorded[key] == expected_value
        for key, expected_value in expected_payload.items()
    )


@lru_cache(maxsize=2)
def _canonical_replay_stream(
    rows: tuple[CorpusLine, ...],
    scheduler_config_json: str,
    days: int,
    seeds: tuple[int, ...],
    corpus_sha256: str,
    scheduler_config_sha256: str,
    derivation_version: str,
    derivation_sha256: str,
) -> tuple[_CanonicalReplayAttempt, ...]:
    """Build an immutable exact replay stream for one complete input identity."""

    # The derivation digest is intentionally part of the cache key even though
    # the derivation function consumes the version directly. This prevents a
    # changed published derivation contract from reusing an older decision stream.
    del derivation_sha256
    scheduler_mapping = json.loads(scheduler_config_json)
    if not isinstance(scheduler_mapping, Mapping):
        raise TypeError("scheduler config must decode to an object")
    scheduler = SchedulerConfig.from_mapping(scheduler_mapping)
    prepared = prepare_corpus(rows)
    canonical: list[_CanonicalReplayAttempt] = []
    for seed in seeds:
        history = SelectionHistory()
        for day_index in range(days):
            for slot_index in range(len(ATTEMPT_SLOTS)):
                last_output_at = history.records[-1].played_at if history.records else None
                natural_attempt = build_natural_attempt(
                    seed=seed,
                    day_index=day_index,
                    slot_index=slot_index,
                    scheduler_config=scheduler,
                    last_output_at=last_output_at,
                )
                selected = select_line(
                    prepared,
                    natural_attempt.context,
                    history,
                    natural_attempt.attempted_at,
                    seed=derive_subseed(
                        seed=seed,
                        day_index=day_index,
                        slot_index=slot_index,
                        corpus_sha256=corpus_sha256,
                        scheduler_config_sha256=scheduler_config_sha256,
                        scenario=natural_attempt.scenario,
                        derivation_version=derivation_version,
                    ),
                    scheduler_config=scheduler,
                )
                canonical.append(
                    _CanonicalReplayAttempt(
                        seed=seed,
                        day_index=day_index,
                        slot_index=slot_index,
                        attempted_at_text=natural_attempt.attempted_at.isoformat(
                            timespec="seconds"
                        ),
                        context=natural_attempt.context,
                        selected_id=selected.row.id if selected is not None else None,
                    )
                )
    return tuple(canonical)


def _validate_simulation_replay(
    attempts: Sequence[_SimulationAttempt],
    rows: Sequence[CorpusLine],
    scheduler_config: object,
    issues: _Issues,
    *,
    days: int,
    seeds: Sequence[int],
    corpus_sha256: str,
    scheduler_config_sha256: str,
) -> None:
    """Replay every canonical selector call and compare the exact recorded decision."""

    try:
        scheduler_config_json = json.dumps(
            scheduler_config,
            ensure_ascii=False,
            sort_keys=True,
            separators=(",", ":"),
            allow_nan=False,
        )
        from ..simulation_core.scenarios import (
            SUBSEED_DERIVATION_SHA256,
            SUBSEED_DERIVATION_VERSION,
        )

        canonical_attempts = _canonical_replay_stream(
            tuple(rows),
            scheduler_config_json,
            days,
            tuple(sorted(seeds)),
            corpus_sha256,
            scheduler_config_sha256,
            SUBSEED_DERIVATION_VERSION,
            SUBSEED_DERIVATION_SHA256,
        )
    except (AttributeError, TypeError, ValueError, json.JSONDecodeError) as error:
        issues.error(
            "simulation_format",
            f"simulation replay cannot build canonical selector inputs: {error}",
        )
        return

    mismatch_count = 0
    first_mismatch: tuple[int, str, str | None] | None = None

    def record_mismatch(
        source_index: int,
        reason: str,
        selected_id: str | None = None,
    ) -> None:
        nonlocal mismatch_count, first_mismatch
        mismatch_count += 1
        if first_mismatch is None:
            first_mismatch = (source_index, reason, selected_id)

    canonical_seeds = tuple(sorted(seeds))
    if tuple(seeds) != canonical_seeds:
        record_mismatch(-1, "declared seeds are not in canonical ascending order")

    for recorded, expected in zip(attempts, canonical_attempts, strict=True):
        reasons: list[str] = []
        if (
            recorded.seed,
            recorded.day_index,
            recorded.slot_index,
        ) != (expected.seed, expected.day_index, expected.slot_index):
            reasons.append("seed/day_index/slot_index differs")
        if recorded.attempted_at_text != expected.attempted_at_text:
            reasons.append("attempted_at differs")
        if not _matches_canonical_context(recorded.context, expected.context):
            reasons.append("context differs")
        if recorded.selected_id != expected.selected_id:
            reasons.append(
                "selected_id differs "
                f"(expected {expected.selected_id!r}, got {recorded.selected_id!r})"
            )
        if reasons:
            record_mismatch(
                recorded.source_index,
                "; ".join(reasons),
                recorded.selected_id,
            )

    if mismatch_count and first_mismatch is not None:
        source_index, reason, selected_id = first_mismatch
        location = "top level" if source_index < 0 else f"attempt {source_index}"
        issues.error(
            "simulation_replay_mismatch",
            f"{mismatch_count} simulation replay decision(s) differ from canonical inputs; "
            f"first mismatch at {location}: {reason}",
            selected_id,
        )


def _simulation_issues(
    simulation: object | None,
    rows: Sequence[CorpusLine],
    scheduler_config: object,
    issues: _Issues,
    *,
    expected_corpus_sha256: str,
    expected_scheduler_config_sha256: str,
) -> None:
    # Keep scenarios -> selector -> validation import order acyclic.
    from ..simulation_core.scenarios import (
        SUBSEED_DERIVATION_SHA256,
        SUBSEED_DERIVATION_VERSION,
    )

    if simulation is None:
        issues.warning(
            "simulation_missing",
            "Task 6 structured 30-day simulation JSON is not supplied yet; static gates still ran.",
        )
        return
    if not isinstance(simulation, Mapping):
        issues.error("simulation_format", "simulation result must be a JSON object")
        return
    if set(simulation) != SIMULATION_KEYS:
        issues.error("simulation_format", "simulation result uses unknown or missing top-level keys")
    if (
        type(simulation.get("schema_version")) is not int
        or simulation.get("schema_version") != SIMULATION_SCHEMA_VERSION
    ):
        issues.error(
            "simulation_format",
            f"simulation schema_version must be integer {SIMULATION_SCHEMA_VERSION}",
        )
    for key, expected in (
        ("corpus_sha256", expected_corpus_sha256),
        ("scheduler_config_sha256", expected_scheduler_config_sha256),
    ):
        digest = simulation.get(key)
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            issues.error("simulation_format", f"simulation {key} must be lowercase SHA-256")
        elif digest != expected:
            issues.error(
                "simulation_hash_mismatch",
                f"simulation {key} does not match the inputs under validation",
            )

    derivation_version = simulation.get("subseed_derivation_version")
    if not isinstance(derivation_version, str):
        issues.error(
            "simulation_format",
            "simulation subseed_derivation_version must be a string",
        )
    elif derivation_version != SUBSEED_DERIVATION_VERSION:
        issues.error(
            "simulation_replay_binding_mismatch",
            "simulation subseed_derivation_version does not match the canonical derivation",
        )

    derivation_sha256 = simulation.get("subseed_derivation_sha256")
    if (
        not isinstance(derivation_sha256, str)
        or re.fullmatch(r"[0-9a-f]{64}", derivation_sha256) is None
    ):
        issues.error(
            "simulation_format",
            "simulation subseed_derivation_sha256 must be lowercase SHA-256",
        )
    elif derivation_sha256 != SUBSEED_DERIVATION_SHA256:
        issues.error(
            "simulation_replay_binding_mismatch",
            "simulation subseed_derivation_sha256 does not match the canonical derivation",
        )

    days = simulation.get("days")
    if not _is_integer(days) or days < 30:
        issues.error("simulation_duration", "simulation must cover at least 30 days")
    required_days = days if _is_integer(days) and days >= 30 else 30
    seeds = simulation.get("seeds")
    seeds_structurally_valid = (
        isinstance(seeds, list)
        and all(_is_integer(seed) for seed in seeds)
        and len(seeds) == len(set(seeds))
    )
    if not seeds_structurally_valid or len(seeds) < 10:
        issues.error("simulation_seed_count", "simulation must contain at least 10 distinct seeds")
    seed_set = set(seeds) if seeds_structurally_valid else set()

    attempts_value = simulation.get("attempts")
    if not isinstance(attempts_value, list):
        issues.error("simulation_format", "simulation attempts must be an array")
        return
    if len(attempts_value) > MAX_CANONICAL_REPLAY_ATTEMPTS:
        issues.error(
            "simulation_replay_limit",
            "simulation event stream exceeds the bounded replay limit of "
            f"{MAX_CANONICAL_REPLAY_ATTEMPTS} attempts",
        )
        return
    expected_attempt_count: int | None = None
    replay_shape_valid = False
    if seeds_structurally_valid and _is_integer(days) and days > 0:
        expected_attempt_count = len(seeds) * days * len(ATTEMPT_SLOTS)
        if expected_attempt_count > MAX_CANONICAL_REPLAY_ATTEMPTS:
            issues.error(
                "simulation_replay_limit",
                "declared simulation coordinates require "
                f"{expected_attempt_count} canonical attempts, above the bounded "
                f"limit of {MAX_CANONICAL_REPLAY_ATTEMPTS}",
            )
            return
        elif len(attempts_value) != expected_attempt_count:
            issues.error(
                "simulation_replay_mismatch",
                "simulation event count differs from the canonical coordinate set "
                f"(expected {expected_attempt_count}, got {len(attempts_value)})",
            )
        else:
            replay_shape_valid = True
    runtime_limits = (
        scheduler_config.get("runtime_limits")
        if isinstance(scheduler_config, Mapping)
        else None
    )
    if not isinstance(runtime_limits, Mapping):
        issues.error(
            "simulation_format",
            "simulation constraints cannot be recomputed without valid runtime_limits",
        )
        return

    parsed_attempts: list[_SimulationAttempt] = []
    covered_dates: dict[int, set[object]] = defaultdict(set)
    seen_attempt_times: set[tuple[int, datetime]] = set()
    seen_attempt_coordinates: set[tuple[int, int, int]] = set()
    for index, attempt in enumerate(attempts_value):
        if not isinstance(attempt, Mapping) or set(attempt) != SIMULATION_ATTEMPT_KEYS:
            issues.error(
                "simulation_format",
                f"simulation attempt {index} has unknown or missing keys",
            )
            continue
        seed = attempt.get("seed")
        day_index = attempt.get("day_index")
        slot_index = attempt.get("slot_index")
        attempted_at_text = attempt.get("attempted_at")
        timestamp = _parse_simulation_timestamp(attempted_at_text)
        selected_id = attempt.get("selected_id")
        if (
            not _is_integer(seed)
            or (seeds_structurally_valid and seed not in seed_set)
            or not _is_integer(day_index)
            or day_index < 0
            or (_is_integer(days) and days > 0 and day_index >= days)
            or not _is_integer(slot_index)
            or not 0 <= slot_index < len(ATTEMPT_SLOTS)
            or timestamp is None
            or (selected_id is not None and (not isinstance(selected_id, str) or not selected_id))
            or (timestamp is not None and not _simulation_context_valid(attempt.get("context"), timestamp))
        ):
            issues.error(
                "simulation_format",
                f"simulation attempt {index} has invalid seed, timestamp, context or selected_id",
            )
            continue
        if (
            timestamp is None
            or not isinstance(attempted_at_text, str)
            or not _is_integer(seed)
            or not _is_integer(day_index)
            or not _is_integer(slot_index)
        ):
            raise RuntimeError("validated simulation attempt lost required typed fields")
        key = (seed, timestamp)
        if key in seen_attempt_times:
            issues.error(
                "simulation_format",
                f"simulation seed {seed} repeats attempted_at {timestamp.isoformat()}",
            )
            continue
        seen_attempt_times.add(key)
        coordinate = (seed, day_index, slot_index)
        if coordinate in seen_attempt_coordinates:
            issues.error(
                "simulation_format",
                "simulation repeats a seed/day_index/slot_index coordinate",
            )
        seen_attempt_coordinates.add(coordinate)
        context = attempt.get("context")
        if not isinstance(context, Mapping):
            raise RuntimeError("validated simulation attempt lost its context mapping")
        parsed_attempts.append(
            _SimulationAttempt(
                index,
                seed,
                day_index,
                slot_index,
                attempted_at_text,
                timestamp,
                context,
                selected_id,
            )
        )
        covered_dates[seed].add(timestamp.date())

    validate_simulation_coverage(parsed_attempts, issues)
    if (
        replay_shape_valid
        and expected_attempt_count is not None
        and len(parsed_attempts) != expected_attempt_count
    ):
        issues.error(
            "simulation_replay_mismatch",
            "malformed simulation attempts leave the canonical coordinate set incomplete "
            f"(expected {expected_attempt_count}, parsed {len(parsed_attempts)})",
        )
        replay_shape_valid = False
    if replay_shape_valid:
        if not isinstance(days, int) or not isinstance(seeds, list):
            raise RuntimeError("canonical replay requires validated days and seeds")
        _validate_simulation_replay(
            parsed_attempts,
            rows,
            scheduler_config,
            issues,
            days=days,
            seeds=seeds,
            corpus_sha256=expected_corpus_sha256,
            scheduler_config_sha256=expected_scheduler_config_sha256,
        )

    incomplete_seeds: list[int] = []
    if seeds_structurally_valid:
        for seed in sorted(seed_set):
            dates = covered_dates.get(seed, set())
            span = 0
            if dates:
                span = (max(dates) - min(dates)).days + 1
            if len(dates) < required_days or span < required_days:
                incomplete_seeds.append(seed)
    if incomplete_seeds:
        issues.error(
            "simulation_seed_coverage",
            f"each seed must cover {required_days} calendar days; incomplete seeds={incomplete_seeds!r}",
        )

    by_id: dict[str, list[CorpusLine]] = defaultdict(list)
    for row in rows:
        if isinstance(row.id, str):
            by_id[row.id].append(row)
    outputs_by_seed: dict[int, list[_SimulationOutput]] = defaultdict(list)
    for attempt in parsed_attempts:
        if attempt.selected_id is None:
            continue
        matches = by_id.get(attempt.selected_id, [])
        if len(matches) != 1 or matches[0].enabled is not True:
            issues.error(
                "simulation_unknown_line",
                "selected_id must resolve to exactly one enabled corpus row",
                attempt.selected_id,
            )
            continue
        outputs_by_seed[attempt.seed].append(_SimulationOutput(attempt, matches[0]))

    if seeds_structurally_valid:
        seeds_without_outputs = sorted(seed for seed in seed_set if not outputs_by_seed[seed])
        if seeds_without_outputs:
            issues.error(
                "simulation_seed_coverage",
                f"each declared seed must produce at least one selected output; empty seeds={seeds_without_outputs!r}",
            )

    output_count = sum(map(len, outputs_by_seed.values()))
    if output_count == 0:
        issues.error("simulation_zero_outputs", "simulation must contain at least one actual output")
        return

    minimum_interval = runtime_limits.get("minimum_interval_minutes")
    max_per_hour = runtime_limits.get("max_outputs_per_hour")
    late_night_max = runtime_limits.get("late_night_max_outputs_per_hour")
    blocked_groups = runtime_limits.get("block_adjacent_category_groups")
    interrupt_intervals = runtime_limits.get("interrupt_cost_minimum_intervals_minutes")
    long_silence = runtime_limits.get("long_silence_minutes")
    runtime_types_valid = (
        _is_integer(minimum_interval)
        and _is_integer(max_per_hour)
        and _is_integer(late_night_max)
        and isinstance(blocked_groups, list)
        and all(isinstance(group, str) for group in blocked_groups)
        and isinstance(interrupt_intervals, Mapping)
        and all(_is_integer(interrupt_intervals.get(str(cost))) for cost in range(6))
        and _is_integer(long_silence)
        and all(
            _is_integer(runtime_limits.get(name))
            for name in (
                "technical_recent_window",
                "technical_recent_max",
                "user_direct_recent_window",
                "user_direct_recent_max",
                "easter_egg_recent_window",
                "easter_egg_recent_max",
            )
        )
    )
    if not runtime_types_valid:
        issues.error(
            "simulation_format",
            "simulation constraints cannot be recomputed from malformed runtime limits",
        )
        return
    if (
        not _is_integer(minimum_interval)
        or not _is_integer(max_per_hour)
        or not _is_integer(late_night_max)
        or not isinstance(blocked_groups, list)
        or not isinstance(interrupt_intervals, Mapping)
        or not _is_integer(long_silence)
    ):
        raise RuntimeError("validated runtime limits lost required typed values")

    group_counts: Counter[str] = Counter()
    mode_counts: Counter[str] = Counter()
    for seed in sorted(outputs_by_seed):
        outputs = sorted(
            outputs_by_seed[seed],
            key=lambda output: (output.attempt.attempted_at, output.attempt.source_index),
        )
        previous: _SimulationOutput | None = None
        history: list[CorpusLine] = []
        last_id: dict[str, datetime] = {}
        last_semantic: dict[str, datetime] = {}
        daily_id_counts: Counter[tuple[object, str]] = Counter()
        rolling_hour: list[datetime] = []
        rolling_late_night_hour: list[datetime] = []

        for output in outputs:
            attempt = output.attempt
            row = output.row
            timestamp = attempt.attempted_at
            elapsed_minutes = float(attempt.context["minutes_since_last_output"])
            if previous is not None:
                elapsed_minutes = (
                    timestamp - previous.attempt.attempted_at
                ).total_seconds() / 60
                reported_elapsed = float(attempt.context["minutes_since_last_output"])
                if abs(reported_elapsed - elapsed_minutes) > 1e-9:
                    issues.error(
                        "simulation_context_violation",
                        "minutes_since_last_output does not match the preceding selected event",
                        row.id,
                    )
                if elapsed_minutes < minimum_interval:
                    issues.error(
                        "simulation_minimum_interval_violation",
                        f"selected outputs are only {elapsed_minutes:g} minutes apart",
                        row.id,
                    )
                required_interval = interrupt_intervals.get(str(row.interrupt_cost))
                if _is_integer(required_interval) and elapsed_minutes < required_interval:
                    issues.error(
                        "simulation_interrupt_budget_violation",
                        f"interrupt_cost {row.interrupt_cost!r} requires {required_interval} minutes",
                        row.id,
                    )
                if row.semantic_group == previous.row.semantic_group:
                    issues.error(
                        "simulation_adjacent_semantic_violation",
                        "adjacent outputs repeat semantic_group",
                        row.id,
                    )
                if (
                    isinstance(row.category_group, str)
                    and row.category_group == previous.row.category_group
                    and row.category_group in blocked_groups
                ):
                    issues.error(
                        "simulation_adjacent_group_violation",
                        f"adjacent outputs repeat blocked category_group {row.category_group!r}",
                        row.id,
                    )

            if not _simulation_trigger_matches(
                row.trigger,
                attempt.context,
                elapsed_minutes,
                long_silence,
            ):
                issues.error(
                    "simulation_trigger_violation",
                    f"selected row trigger {row.trigger!r} does not match the event",
                    row.id,
                )
            tokens = _required_context_tokens(row.required_context)
            if tokens is None or not all(
                _simulation_context_token_matches(token, attempt.context, timestamp)
                for token in tokens
            ):
                issues.error(
                    "simulation_context_violation",
                    f"selected row required_context {row.required_context!r} is not satisfied",
                    row.id,
                )

            if row.requires_reply is True or (
                isinstance(row.text, str) and any(mark in row.text for mark in ("?", "？"))
            ):
                issues.error(
                    "simulation_question",
                    "selected row asks a question or requires a reply",
                    row.id,
                )

            if isinstance(row.id, str) and row.id in last_id and _is_finite_number(row.cooldown_hours):
                elapsed_hours = (timestamp - last_id[row.id]).total_seconds() / 3600
                if elapsed_hours < float(row.cooldown_hours):
                    issues.error(
                        "simulation_id_cooldown_violation",
                        f"row repeated after {elapsed_hours:g}h inside its cooldown",
                        row.id,
                    )
            if isinstance(row.semantic_group, str) and row.semantic_group in last_semantic and _is_finite_number(row.semantic_cooldown_hours):
                elapsed_hours = (
                    timestamp - last_semantic[row.semantic_group]
                ).total_seconds() / 3600
                if elapsed_hours < float(row.semantic_cooldown_hours):
                    issues.error(
                        "simulation_semantic_cooldown_violation",
                        f"semantic_group repeated after {elapsed_hours:g}h inside its cooldown",
                        row.id,
                    )

            day_id = (timestamp.date(), str(row.id))
            daily_id_counts[day_id] += 1
            if _is_integer(row.max_per_day) and daily_id_counts[day_id] == row.max_per_day + 1:
                issues.error(
                    "simulation_max_per_day_violation",
                    f"row exceeds max_per_day={row.max_per_day}",
                    row.id,
                )
            rolling_hour = [
                played_at
                for played_at in rolling_hour
                if timestamp - played_at < timedelta(hours=1)
            ]
            rolling_hour.append(timestamp)
            if len(rolling_hour) == max_per_hour + 1:
                issues.error(
                    "simulation_hourly_budget_violation",
                    f"seed {seed} exceeds {max_per_hour} outputs in rolling window (now-60min, now]",
                    row.id,
                )
            if _expected_daypart(timestamp) == "late_night":
                rolling_late_night_hour = [
                    played_at
                    for played_at in rolling_late_night_hour
                    if timestamp - played_at < timedelta(hours=1)
                ]
                rolling_late_night_hour.append(timestamp)
                if len(rolling_late_night_hour) == late_night_max + 1:
                    issues.error(
                        "simulation_late_night_budget_violation",
                        f"seed {seed} exceeds the late-night rolling 60-minute budget",
                        row.id,
                    )

            history.append(row)
            technical_window = int(runtime_limits["technical_recent_window"])
            user_window = int(runtime_limits["user_direct_recent_window"])
            easter_window = int(runtime_limits["easter_egg_recent_window"])
            if sum(item.category_group == "technical" for item in history[-technical_window:]) > int(runtime_limits["technical_recent_max"]):
                issues.error(
                    "simulation_recent_technical_violation",
                    "recent technical outputs exceed the configured window quota",
                    row.id,
                )
            if sum(item.output_mode == "user_direct" for item in history[-user_window:]) > int(runtime_limits["user_direct_recent_max"]):
                issues.error(
                    "simulation_recent_user_direct_violation",
                    "recent user_direct outputs exceed the configured window quota",
                    row.id,
                )
            if sum(item.category_group == "easter_egg" for item in history[-easter_window:]) > int(runtime_limits["easter_egg_recent_max"]):
                issues.error(
                    "simulation_recent_easter_egg_violation",
                    "recent EasterEgg outputs exceed the configured window quota",
                    row.id,
                )

            if isinstance(row.id, str):
                last_id[row.id] = timestamp
            if isinstance(row.semantic_group, str):
                last_semantic[row.semantic_group] = timestamp
            if isinstance(row.category_group, str):
                group_counts[row.category_group] += 1
            if isinstance(row.output_mode, str):
                mode_counts[row.output_mode] += 1
            previous = output

    technical_ratio = group_counts["technical"] / output_count
    easter_ratio = group_counts["easter_egg"] / output_count
    self_ambient_ratio = (
        mode_counts["self_talk"] + mode_counts["ambient"]
    ) / output_count
    user_direct_ratio = mode_counts["user_direct"] / output_count
    acceptance = PERSONA_CONTRACT.scheduler["acceptance"]
    if not isinstance(acceptance, Mapping):
        raise RuntimeError("persona scheduler acceptance contract is malformed")
    technical_range = acceptance["technical_playback_ratio"]
    easter_range = acceptance["easter_egg_playback_ratio"]
    if (
        not isinstance(technical_range, tuple)
        or len(technical_range) != 2
        or not isinstance(easter_range, tuple)
        or len(easter_range) != 2
    ):
        raise RuntimeError("persona playback acceptance ranges are malformed")
    if (
        not float(technical_range[0]) <= technical_ratio <= float(technical_range[1])
        or not float(easter_range[0]) <= easter_ratio <= float(easter_range[1])
        or self_ambient_ratio < float(acceptance["self_talk_ambient_minimum"])
        or user_direct_ratio > float(acceptance["user_direct_maximum"])
    ):
        issues.error(
            "simulation_metric",
            "recomputed technical, EasterEgg or output-mode ratios violate acceptance bounds",
        )
