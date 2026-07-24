from __future__ import annotations

import math
import random
import hashlib
from collections import Counter
from dataclasses import dataclass
from datetime import UTC, datetime, timedelta
from pathlib import Path
from types import MappingProxyType
from typing import Mapping, Sequence

from .context import ContextError, PersonaContext, daypart_for
from .contract import PERSONA_CONTRACT
from .history import HistoryFormatError, HistoryRecord, SelectionHistory
from .lexical import contains_seasoning_marker
from .models import CorpusLine
from .surface_exposure import SURFACE_RECENT_WINDOW, surface_exposure
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
        if type(value.get("schema_version")) is not int or value.get("schema_version") != 1:
            raise SelectorConfigError("scheduler config schema_version must be integer 1")
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


_SCENE_SCHEDULING_FIELDS = (
    "category",
    "category_group",
    "semantic_group",
    "output_mode",
    "trigger",
    "required_context",
    "tone",
    "interrupt_cost",
    "cooldown_hours",
    "semantic_cooldown_hours",
    "max_per_day",
    "weight",
    "requires_reply",
    "enabled",
)


@dataclass(frozen=True, slots=True)
class PreparedScene:
    semantic_group: str
    variants: tuple[CorpusLine, ...]
    seasoning_variants: tuple[CorpusLine, ...]
    neutral_variants: tuple[CorpusLine, ...]

    @property
    def representative(self) -> CorpusLine:
        return self.variants[0]

    @property
    def has_seasoning_variant(self) -> bool:
        return bool(self.seasoning_variants)

    @property
    def has_neutral_variant(self) -> bool:
        return bool(self.neutral_variants)


@dataclass(frozen=True, slots=True)
class PreparedCorpus:
    input_row_count: int
    scenes: tuple[PreparedScene, ...]
    scenes_by_trigger: Mapping[str, tuple[PreparedScene, ...]]
    dry_sharp_semantic_groups: frozenset[str]
    duplicate_ids: tuple[str, ...]
    rejected_semantic_groups: tuple[str, ...]

    @property
    def scene_count(self) -> int:
        return len(self.scenes)

    @property
    def variant_count(self) -> int:
        return sum(len(scene.variants) for scene in self.scenes)


def _scene_signature(row: CorpusLine) -> tuple[object, ...]:
    return tuple(getattr(row, field) for field in _SCENE_SCHEDULING_FIELDS)


def prepare_corpus(corpus: Sequence[CorpusLine] | PreparedCorpus) -> PreparedCorpus:
    """Build an immutable semantic-scene index in one pass over source rows."""
    if isinstance(corpus, PreparedCorpus):
        return corpus
    rows = tuple(corpus)
    typed = tuple(row for row in rows if isinstance(row, CorpusLine))
    id_counts = Counter(row.id for row in typed)
    duplicate_ids = tuple(sorted(line_id for line_id, count in id_counts.items() if count > 1))
    duplicate_set = frozenset(duplicate_ids)
    grouped: dict[str, list[CorpusLine]] = {}
    for row in typed:
        if (
            row.id in duplicate_set
            or row.enabled is not True
            or row.requires_reply is not False
            or not isinstance(row.semantic_group, str)
            or not row.semantic_group.strip()
        ):
            continue
        grouped.setdefault(row.semantic_group, []).append(row)

    scenes: list[PreparedScene] = []
    rejected: list[str] = []
    for semantic_group, variants in sorted(grouped.items()):
        ordered = tuple(sorted(variants, key=lambda row: row.id))
        signature = _scene_signature(ordered[0])
        if any(_scene_signature(row) != signature for row in ordered[1:]):
            rejected.append(semantic_group)
            continue
        seasoning_rows: list[CorpusLine] = []
        neutral_rows: list[CorpusLine] = []
        for row in ordered:
            (seasoning_rows if contains_seasoning_marker(row.text) else neutral_rows).append(row)
        scenes.append(
            PreparedScene(
                semantic_group=semantic_group,
                variants=ordered,
                seasoning_variants=tuple(seasoning_rows),
                neutral_variants=tuple(neutral_rows),
            )
        )

    by_trigger: dict[str, list[PreparedScene]] = {}
    for scene in scenes:
        by_trigger.setdefault(scene.representative.trigger, []).append(scene)
    return PreparedCorpus(
        input_row_count=len(rows),
        scenes=tuple(scenes),
        scenes_by_trigger=MappingProxyType(
            {
                trigger: tuple(trigger_scenes)
                for trigger, trigger_scenes in sorted(by_trigger.items())
            }
        ),
        dry_sharp_semantic_groups=frozenset(
            scene.semantic_group
            for scene in scenes
            if scene.representative.tone == "dry_sharp"
        ),
        duplicate_ids=duplicate_ids,
        rejected_semantic_groups=tuple(rejected),
    )


@dataclass(frozen=True, slots=True)
class _ScoredCandidate:
    row: CorpusLine
    score: float
    score_band: int
    reasons: tuple[str, ...]


@dataclass(frozen=True, slots=True)
class _ScoredScene:
    scene: PreparedScene
    scored: _ScoredCandidate


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
    dry_sharp_semantic_groups: frozenset[str],
) -> _ScoredCandidate:
    recent = records[-SCORE_HISTORY_WINDOW:]
    total = len(recent)
    group_count = sum(record.category_group == row.category_group for record in recent)
    mode_count = sum(record.output_mode == row.output_mode for record in recent)
    category_count = sum(record.category == row.category for record in recent)
    group_observed = group_count / total if total else 0.0
    mode_observed = mode_count / total if total else 0.0
    category_observed = category_count / total if total else 0.0
    dry_sharp_observed = (
        sum(
            record.was_dry_sharp
            or record.semantic_group in dry_sharp_semantic_groups
            for record in recent
        )
        / total
        if total
        else 0.0
    )
    group_target = config.category_group_weights[row.category_group]
    mode_target = config.output_mode_targets[row.output_mode]
    group_deficit = group_target - group_observed
    mode_deficit = mode_target - mode_observed
    row_weight_bonus = float(row.weight) * 0.5
    interrupt_penalty = row.interrupt_cost * 0.75
    category_repeat_penalty = category_observed * 5.0
    dry_sharp_target = float(PERSONA_CONTRACT.dry_sharp["playback_target"])
    dry_sharp_deficit = dry_sharp_target - dry_sharp_observed
    dry_sharp_bonus = dry_sharp_deficit * 200.0 if row.tone == "dry_sharp" else 0.0
    score = (
        group_deficit * 100.0
        + mode_deficit * 35.0
        + row_weight_bonus
        - interrupt_penalty
        - category_repeat_penalty
        + dry_sharp_bonus
    )
    # Row weight chooses among peers; it must not first exclude the lighter peer
    # by moving otherwise-identical candidates into different score bands.
    band = math.floor((score - row_weight_bonus) / SCORE_BAND_WIDTH)
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
        f"dry_sharp_deficit={dry_sharp_deficit:.6f}",
        f"dry_sharp_bonus={dry_sharp_bonus:.6f}",
    )
    return _ScoredCandidate(row=row, score=score, score_band=band, reasons=reasons)


def _selector_subseed(seed: int | None, stage: str, semantic_group: str = "") -> int | None:
    if seed is None:
        return None
    identity = f"persona-selector-v2:{seed}:{stage}:{semantic_group}"
    return int.from_bytes(hashlib.sha256(identity.encode("utf-8")).digest()[:8], "big")


def _weighted_scene_choice(
    candidates: Sequence[_ScoredScene], seed: int | None
) -> _ScoredScene:
    rng = random.Random(_selector_subseed(seed, "scene"))
    total = math.fsum(float(candidate.scored.row.weight) for candidate in candidates)
    point = rng.random() * total
    cumulative = 0.0
    for candidate in candidates:
        cumulative += float(candidate.scored.row.weight)
        if point < cumulative:
            return candidate
    return candidates[-1]


def _coerce_config(value: SchedulerConfig | Mapping[str, object] | None) -> SchedulerConfig:
    if value is None:
        return DEFAULT_SCHEDULER_CONFIG
    if isinstance(value, SchedulerConfig):
        try:
            normalized = _scheduler_config_mapping(value)
        except (AttributeError, TypeError, ValueError) as error:
            raise SelectorConfigError("typed scheduler config cannot be normalized") from error
        return SchedulerConfig.from_mapping(normalized)
    if isinstance(value, Mapping):
        return SchedulerConfig.from_mapping(value)
    raise SelectorConfigError("scheduler_config must be SchedulerConfig, mapping or None")


def _scheduler_config_mapping(config: SchedulerConfig) -> dict[str, object]:
    return {
        "schema_version": config.schema_version,
        "category_group_weights": dict(config.category_group_weights),
        "output_mode_targets": dict(config.output_mode_targets),
        "runtime_limits": {
            "minimum_interval_minutes": config.minimum_interval_minutes,
            "max_outputs_per_hour": config.max_outputs_per_hour,
            "late_night_max_outputs_per_hour": config.late_night_max_outputs_per_hour,
            "semantic_group_no_repeat": config.semantic_group_no_repeat,
            "block_adjacent_category_groups": sorted(
                config.block_adjacent_category_groups
            ),
            "technical_recent_window": config.technical_recent_window,
            "technical_recent_max": config.technical_recent_max,
            "user_direct_recent_window": config.user_direct_recent_window,
            "user_direct_recent_max": config.user_direct_recent_max,
            "easter_egg_recent_window": config.easter_egg_recent_window,
            "easter_egg_recent_max": config.easter_egg_recent_max,
            "long_silence_minutes": config.long_silence_minutes,
            "interrupt_cost_minimum_intervals_minutes": {
                str(cost): minutes
                for cost, minutes in config.interrupt_cost_minimum_intervals_minutes.items()
            },
        },
        "context_tokens": sorted(config.context_tokens),
        "mvp_triggers": sorted(config.mvp_triggers),
        "future_triggers": sorted(config.future_triggers),
    }


def _prefer_surface_exposure(
    variants: Sequence[CorpusLine],
    records: Sequence[HistoryRecord],
) -> tuple[CorpusLine, ...]:
    """Prefer a fresh surface face, then steer seasoning toward its playback band."""

    if not variants:
        return ()
    recent_surface = records[-SURFACE_RECENT_WINDOW:]
    openings = {record.surface_opening for record in recent_surface if record.surface_opening}
    endings = {record.surface_ending for record in recent_surface if record.surface_ending}
    templates = {record.surface_template for record in recent_surface if record.surface_template}

    profiled = tuple((row, surface_exposure(row.text)) for row in variants)
    conflict_counts = {
        row.id: int(profile.opening in openings)
        + int(profile.ending in endings)
        + int(profile.template in templates)
        for row, profile in profiled
    }
    least_conflicts = min(conflict_counts.values())
    diverse = tuple(row for row, _ in profiled if conflict_counts[row.id] == least_conflicts)

    seasoning = PERSONA_CONTRACT.lexical_exposure["seasoning"]
    acceptance = seasoning["playback_acceptance"]
    target = (float(acceptance[0]) + float(acceptance[1])) / 2.0
    score_window = records[-SCORE_HISTORY_WINDOW:]
    observed = (
        sum(record.was_seasoning is not False for record in score_window) / len(score_window)
        if score_window
        else 0.0
    )
    seasoning_rows = tuple(row for row in diverse if contains_seasoning_marker(row.text))
    neutral_rows = tuple(row for row in diverse if not contains_seasoning_marker(row.text))
    if observed + _EPSILON < target and seasoning_rows:
        return seasoning_rows
    if observed > target + _EPSILON and neutral_rows:
        return neutral_rows
    return diverse


def select_line(
    corpus: Sequence[CorpusLine] | PreparedCorpus,
    context: PersonaContext,
    history: SelectionHistory,
    now: datetime,
    seed: int | None = None,
    *,
    scheduler_config: SchedulerConfig | Mapping[str, object] | None = None,
) -> SelectedLine | None:
    """Select one semantic scene, then one surface variant within that scene."""

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
        prepared = prepare_corpus(corpus)
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

    records_by_id: dict[str, HistoryRecord] = {}
    semantic_records: dict[str, HistoryRecord] = {}
    today_counts: Counter[str] = Counter()
    local_date = now.date()
    for record in records:
        records_by_id[record.selected_id] = record
        semantic_records[record.semantic_group] = record
        if record.played_at.astimezone(now.tzinfo).date() == local_date:
            today_counts[record.selected_id] += 1

    seasoning_policy = PERSONA_CONTRACT.lexical_exposure["seasoning"]
    seasoning_window = int(seasoning_policy["recent_window"])
    seasoning_maximum = int(seasoning_policy["recent_max"])
    seasoning_blocked = (
        sum(record.was_seasoning is not False for record in records[-max(0, seasoning_window - 1) :])
        >= seasoning_maximum
    )

    def variant_available(row: CorpusLine) -> bool:
        return _outside_cooldown(
            now,
            records_by_id.get(row.id),
            float(row.cooldown_hours),
        ) and today_counts[row.id] < row.max_per_day

    def scene_available_variants(scene: PreparedScene) -> tuple[CorpusLine, ...]:
        exposure_candidates = (
            scene.neutral_variants if seasoning_blocked else scene.variants
        )
        return tuple(row for row in exposure_candidates if variant_available(row))

    def scene_has_available_variant(scene: PreparedScene) -> bool:
        exposure_candidates = (
            scene.neutral_variants if seasoning_blocked else scene.variants
        )
        return any(variant_available(row) for row in exposure_candidates)

    # 1-3. Static preparation has removed duplicate/disabled rows. Runtime
    # trigger and context checks now visit one representative per semantic scene.
    candidates: list[PreparedScene] = []
    for scene in prepared.scenes:
        row = scene.representative
        if not _candidate_row_is_safe(row, config):
            continue
        if not _trigger_matches(row.trigger, context, actual_elapsed, config):
            continue
        required = _context_tokens(row.required_context, config)
        if required is not None and (
            required == ("none",) or all(token in context_tokens for token in required)
        ):
            candidates.append(scene)

    # 4-6. Per-ID availability short-circuits at the first usable variant;
    # semantic cooldown remains a scene-level rule.
    candidates = [
        scene
        for scene in candidates
        if scene_has_available_variant(scene)
        and _outside_cooldown(
            now,
            semantic_records.get(scene.semantic_group),
            float(scene.representative.semantic_cooldown_hours),
        )
    ]

    # 7. adjacent semantic/group bans and candidate-aware group windows.
    last = records[-1] if records else None
    group_filtered: list[PreparedScene] = []
    for scene in candidates:
        row = scene.representative
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
        group_filtered.append(scene)
    candidates = group_filtered

    # 8. candidate-aware output-mode repetition window.
    candidates = [
        scene
        for scene in candidates
        for row in (scene.representative,)
        if row.output_mode != "user_direct"
        or _candidate_window_count(
            records,
            config.user_direct_recent_window,
            True,
            lambda record: record.output_mode == "user_direct",
        )
        <= config.user_direct_recent_max
    ]

    dry_sharp_window = int(PERSONA_CONTRACT.dry_sharp["recent_window"])
    dry_sharp_maximum = int(PERSONA_CONTRACT.dry_sharp["recent_max"])
    dry_sharp_playback_window = SCORE_HISTORY_WINDOW
    dry_sharp_playback_maximum = math.floor(
        float(PERSONA_CONTRACT.dry_sharp["playback_acceptance"][1])
        * dry_sharp_playback_window
        + _EPSILON
    )

    def history_was_dry_sharp(record: HistoryRecord) -> bool:
        return (
            record.was_dry_sharp
            or record.semantic_group in prepared.dry_sharp_semantic_groups
        )

    candidates = [
        scene
        for scene in candidates
        if scene.representative.tone != "dry_sharp"
        or (
            _candidate_window_count(
                records,
                dry_sharp_window,
                True,
                history_was_dry_sharp,
            )
            <= dry_sharp_maximum
            and _candidate_window_count(
                records,
                dry_sharp_playback_window,
                True,
                history_was_dry_sharp,
            )
            <= dry_sharp_playback_maximum
        )
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
            scene
            for scene in candidates
            for row in (scene.representative,)
            if actual_elapsed + _EPSILON
            >= config.interrupt_cost_minimum_intervals_minutes[row.interrupt_cost]
        ]

    if not candidates:
        return None

    # 10. A scene receives exactly one score regardless of surface variant count.
    scored = [
        _ScoredScene(
            scene=scene,
            scored=_score(
                scene.representative,
                records,
                config,
                prepared.dry_sharp_semantic_groups,
            ),
        )
        for scene in candidates
    ]

    # 11. Choose a semantic scene first, then a surface variant. Namespaced
    # local RNGs make scene choice invariant when a scene gains more variants.
    highest_band = max(candidate.scored.score_band for candidate in scored)
    highest = [
        candidate for candidate in scored if candidate.scored.score_band == highest_band
    ]
    chosen_scene = _weighted_scene_choice(highest, seed)
    eligible_variants = _prefer_surface_exposure(
        scene_available_variants(chosen_scene.scene),
        records,
    )
    if not eligible_variants:
        return None
    variant_rng = random.Random(
        _selector_subseed(seed, "surface", chosen_scene.scene.semantic_group)
    )
    chosen_row = eligible_variants[variant_rng.randrange(len(eligible_variants))]
    surface = surface_exposure(chosen_row.text)

    # 12. history mutates exactly once and only after a candidate is selected.
    try:
        history.append(
            HistoryRecord(
                selected_id=chosen_row.id,
                played_at=now,
                category=chosen_row.category,
                category_group=chosen_row.category_group,
                semantic_group=chosen_row.semantic_group,
                output_mode=chosen_row.output_mode,
                trigger=chosen_row.trigger,
                interrupt_cost=chosen_row.interrupt_cost,
                was_dry_sharp=chosen_row.tone == "dry_sharp",
                was_seasoning=contains_seasoning_marker(chosen_row.text),
                surface_opening=surface.opening,
                surface_ending=surface.ending,
                surface_template=surface.template,
            )
        )
    except HistoryFormatError:
        return None
    return SelectedLine(
        row=chosen_row,
        score=float(chosen_scene.scored.score),
        score_band=chosen_scene.scored.score_band,
        reasons=chosen_scene.scored.reasons,
    )
