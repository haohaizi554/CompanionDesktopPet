"""Scheduler configuration validation rules."""

from __future__ import annotations

import math
import re
from typing import Mapping

from ..contract import (
    ALLOWED_CONTEXT_TOKENS,
    CATEGORY_GROUPS,
    FUTURE_TRIGGERS,
    MVP_TRIGGERS,
    OUTPUT_MODES,
    PERSONA_CONTRACT,
)
from .core import (
    ValidationReport,
    _Issues,
    _is_finite_number,
    _is_integer,
)


CONTEXT_TOKEN_PATTERN = re.compile(r"^[a-z][a-z0-9_]*(?::[a-z][a-z0-9_]*)?$")

TOP_LEVEL_CONFIG_KEYS = frozenset(
    {
        "schema_version",
        "category_group_weights",
        "output_mode_targets",
        "runtime_limits",
        "context_tokens",
        "mvp_triggers",
        "future_triggers",
    }
)
RUNTIME_LIMIT_KEYS = frozenset(
    {
        "minimum_interval_minutes",
        "max_outputs_per_hour",
        "late_night_max_outputs_per_hour",
        "semantic_group_no_repeat",
        "block_adjacent_category_groups",
        "technical_recent_window",
        "technical_recent_max",
        "user_direct_recent_window",
        "user_direct_recent_max",
        "easter_egg_recent_window",
        "easter_egg_recent_max",
        "long_silence_minutes",
        "interrupt_cost_minimum_intervals_minutes",
    }
)

def _validate_weight_map(config: Mapping[str, object], issues: _Issues) -> None:
    expected_raw = PERSONA_CONTRACT.scheduler["category_group_weights"]
    if not isinstance(expected_raw, Mapping):
        raise RuntimeError("persona scheduler weight contract is malformed")
    expected = {str(name): float(value) for name, value in expected_raw.items()}
    raw = config.get("category_group_weights")
    if not isinstance(raw, Mapping):
        issues.error("group_weights", "category_group_weights must be an object")
        issues.error("group_weight_sum", "group weights cannot be summed until all are finite")
        return
    keys = set(raw)
    valid = keys == CATEGORY_GROUPS
    if not valid:
        issues.error(
            "group_weights",
            "category_group_weights must contain exactly the eight documented groups",
        )
    values: dict[str, float] = {}
    for name, value in raw.items():
        if not _is_finite_number(value) or not 0 <= float(value) <= 1:
            valid = False
            issues.error("group_weights", f"group weight {name!r} must be finite in [0, 1]")
        else:
            values[str(name)] = float(value)
    if not valid or abs(sum(values.values()) - 1.0) > 1e-9:
        issues.error("group_weight_sum", "the eight category_group weights must sum to 1.0")

    if valid and any(
        abs(values.get(name, math.inf) - target) > 1e-9
        for name, target in expected.items()
    ):
        issues.error(
            "group_weights",
            "category_group_weights must match the shared persona contract",
        )

    technical = values.get("technical")
    acceptance = PERSONA_CONTRACT.scheduler["acceptance"]
    if not isinstance(acceptance, Mapping):
        raise RuntimeError("persona scheduler acceptance contract is malformed")
    technical_range = acceptance["technical_playback_ratio"]
    if not isinstance(technical_range, tuple) or len(technical_range) != 2:
        raise RuntimeError("technical playback acceptance must contain two values")
    if technical is None or not float(technical_range[0]) <= technical <= float(technical_range[1]):
        issues.error("technical_weight", "technical playback weight must be in [0.10, 0.20]")
    easter = values.get("easter_egg")
    easter_range = acceptance["easter_egg_playback_ratio"]
    if not isinstance(easter_range, tuple) or len(easter_range) != 2:
        raise RuntimeError("EasterEgg playback acceptance must contain two values")
    if easter is None or not float(easter_range[0]) <= easter <= float(easter_range[1]):
        issues.error(
            "easter_egg_config_weight",
            "easter_egg playback weight must stay inside the shared 10% acceptance band",
        )
    character = values.get("character_life")
    other_values = [value for name, value in values.items() if name != "character_life"]
    if (
        not valid
        or character is None
        or not other_values
        or character <= max(other_values)
    ):
        issues.error(
            "character_life_weight",
            "character_life must be the unique highest category_group weight",
        )


def _validate_output_targets(config: Mapping[str, object], issues: _Issues) -> None:
    expected_raw = PERSONA_CONTRACT.scheduler["output_mode_targets"]
    if not isinstance(expected_raw, Mapping):
        raise RuntimeError("persona output-mode target contract is malformed")
    expected = {str(name): float(value) for name, value in expected_raw.items()}
    raw = config.get("output_mode_targets")
    if not isinstance(raw, Mapping) or set(raw) != OUTPUT_MODES:
        issues.error(
            "output_mode_targets",
            "output_mode_targets must contain exactly self_talk, ambient, user_direct and system_observe",
        )
        return
    if any(
        not _is_finite_number(value) or not 0 <= float(value) <= 1
        for value in raw.values()
    ):
        issues.error("output_mode_targets", "output mode targets must be finite values in [0, 1]")
        return
    targets = {str(name): float(value) for name, value in raw.items()}
    group_weights = config.get("category_group_weights")
    group_modes = PERSONA_CONTRACT.scheduler["category_group_output_modes"]
    if isinstance(group_weights, Mapping) and isinstance(group_modes, Mapping):
        aggregate = {mode: 0.0 for mode in OUTPUT_MODES}
        aggregate_valid = True
        for group, mode in group_modes.items():
            weight = group_weights.get(group)
            if not isinstance(mode, str) or mode not in aggregate or not _is_finite_number(weight):
                aggregate_valid = False
                break
            aggregate[mode] += float(weight)
        if not aggregate_valid or any(
            abs(aggregate[mode] - targets[mode]) > 1e-9 for mode in OUTPUT_MODES
        ):
            issues.error(
                "output_mode_group_aggregate",
                "output_mode_targets must exactly equal category_group weight aggregation",
            )
    if (
        abs(sum(targets.values()) - 1.0) > 1e-9
        or targets["self_talk"] + targets["ambient"]
        < float(PERSONA_CONTRACT.scheduler["acceptance"]["self_talk_ambient_minimum"])
        or targets["user_direct"]
        > float(PERSONA_CONTRACT.scheduler["acceptance"]["user_direct_maximum"])
        or any(abs(targets[name] - value) > 1e-9 for name, value in expected.items())
    ):
        issues.error(
            "output_mode_targets",
            "output mode targets must sum to 1.0, keep self_talk+ambient >= 0.65 and user_direct <= 0.15",
        )


def _valid_int_limit(value: object, *, minimum: int, maximum: int | None = None) -> bool:
    return _is_integer(value) and value >= minimum and (maximum is None or value <= maximum)


def _validate_runtime_limits(config: Mapping[str, object], issues: _Issues) -> None:
    expected = PERSONA_CONTRACT.scheduler["runtime_limits"]
    if not isinstance(expected, Mapping):
        raise RuntimeError("persona runtime-limit contract is malformed")
    raw = config.get("runtime_limits")
    if not isinstance(raw, Mapping) or set(raw) != RUNTIME_LIMIT_KEYS:
        issues.error("runtime_limits", "runtime_limits must use the exact Task 5 key set")
        return
    valid = True
    valid &= raw.get("minimum_interval_minutes") == expected["minimum_interval_minutes"]
    valid &= raw.get("max_outputs_per_hour") == expected["max_outputs_per_hour"]
    valid &= (
        raw.get("late_night_max_outputs_per_hour")
        == expected["late_night_max_outputs_per_hour"]
    )
    valid &= raw.get("semantic_group_no_repeat") is expected["semantic_group_no_repeat"]

    adjacent = raw.get("block_adjacent_category_groups")
    adjacent_is_string_list = isinstance(adjacent, list) and all(
        isinstance(value, str) for value in adjacent
    )
    if not adjacent_is_string_list:
        issues.error(
            "config_format",
            "block_adjacent_category_groups must be an array of strings",
        )
        valid = False
    else:
        valid &= (
            len(adjacent) == len(set(adjacent))
            and set(adjacent)
            == set(expected["block_adjacent_category_groups"])
        )
    exact_names = (
        "technical_recent_window",
        "technical_recent_max",
        "user_direct_recent_window",
        "user_direct_recent_max",
        "easter_egg_recent_window",
        "easter_egg_recent_max",
        "long_silence_minutes",
    )
    valid &= all(
        raw.get(name) == expected[name] and _is_integer(raw.get(name))
        for name in exact_names
    )

    intervals = raw.get("interrupt_cost_minimum_intervals_minutes")
    if not isinstance(intervals, Mapping) or set(intervals) != {str(value) for value in range(6)}:
        valid = False
    else:
        ordered = [intervals[str(value)] for value in range(6)]
        expected_intervals = expected["interrupt_cost_minimum_intervals_minutes"]
        valid &= isinstance(expected_intervals, Mapping) and all(
            intervals[str(value)] == expected_intervals[str(value)] for value in range(6)
        )
        valid &= all(_valid_int_limit(value, minimum=8) for value in ordered)
        valid &= all(left < right for left, right in zip(ordered, ordered[1:]))
        minimum = raw.get("minimum_interval_minutes")
        valid &= _is_integer(minimum) and ordered[0] >= minimum
    if not valid:
        issues.error(
            "runtime_limits",
            "runtime limits must enforce the 8-minute, hourly, adjacency, recent-window and interrupt-cost contracts",
        )


def _validate_context_and_triggers(config: Mapping[str, object], issues: _Issues) -> None:
    tokens = config.get("context_tokens")
    valid_tokens = (
        isinstance(tokens, list)
        and all(isinstance(token, str) for token in tokens)
        and len(tokens) == len(set(tokens))
        and set(tokens) == ALLOWED_CONTEXT_TOKENS
        and all(CONTEXT_TOKEN_PATTERN.fullmatch(token) for token in tokens)
    )
    if not valid_tokens:
        issues.error(
            "context_tokens",
            "context_tokens must be the unique controlled Task 5 whitelist",
        )

    mvp = config.get("mvp_triggers")
    future = config.get("future_triggers")
    valid_triggers = (
        isinstance(mvp, list)
        and isinstance(future, list)
        and all(isinstance(value, str) for value in mvp + future)
        and len(mvp) == len(set(mvp))
        and len(future) == len(set(future))
        and set(mvp) == MVP_TRIGGERS
        and set(future) == FUTURE_TRIGGERS
        and not (set(mvp) & set(future))
    )
    if not valid_triggers:
        issues.error("trigger_partition", "MVP and future triggers must match the controlled disjoint sets")


def validate_config(config: object) -> ValidationReport:
    issues = _Issues()
    if not isinstance(config, Mapping):
        issues.error("config_format", "scheduler config must be a JSON object")
        return issues.report()
    if set(config) != TOP_LEVEL_CONFIG_KEYS:
        issues.error("config_keys", "scheduler config must use the exact documented top-level keys")
    if config.get("schema_version") != 1 or isinstance(config.get("schema_version"), bool):
        issues.error("config_format", "schema_version must be integer 1")
    _validate_weight_map(config, issues)
    _validate_output_targets(config, issues)
    _validate_runtime_limits(config, issues)
    _validate_context_and_triggers(config, issues)
    return issues.report()
