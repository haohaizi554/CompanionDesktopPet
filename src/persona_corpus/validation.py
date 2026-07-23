from __future__ import annotations

import hashlib
import json
import math
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable, Mapping, Sequence

from .loader import CorpusFormatError, load_v2
from .models import CorpusLine
from .normalization import normalize_text


VALIDATION_GROUPS = (
    (1, "exact_header"),
    (2, "row_width"),
    (3, "unique_id"),
    (4, "exact_enabled_text"),
    (5, "normalized_enabled_text"),
    (6, "output_mode"),
    (7, "trigger"),
    (8, "tone"),
    (9, "interrupt_cost"),
    (10, "cooldown_hours"),
    (11, "semantic_cooldown_hours"),
    (12, "max_per_day"),
    (13, "weight"),
    (14, "reply_free"),
    (15, "question_free"),
    (16, "text_field_integrity"),
    (17, "user_direct_context"),
    (18, "required_context"),
    (19, "pii_review"),
    (20, "easter_egg_cooldown"),
    (21, "high_cost_weight"),
    (22, "technical_fake_context"),
    (23, "cartesian_generation"),
    (24, "catchphrase_frequency"),
    (25, "length_distribution"),
    (26, "scheduler_weights"),
    (27, "simulation"),
)

CATEGORY_GROUPS = frozenset(
    {
        "technical",
        "growth",
        "career",
        "daily_care",
        "emotional_reflection",
        "character_life",
        "easter_egg",
        "system_ambient",
    }
)
OUTPUT_MODES = frozenset({"self_talk", "ambient", "user_direct", "system_observe"})
MVP_TRIGGERS = frozenset(
    {
        "any",
        "app_start",
        "morning",
        "noon",
        "afternoon",
        "evening",
        "late_night",
        "day_changed",
        "weekday",
        "weekend",
        "holiday",
        "anniversary",
        "long_silence",
    }
)
FUTURE_TRIGGERS = frozenset(
    {"ide_foreground", "long_active", "idle_return", "story_timer"}
)
TRIGGERS = MVP_TRIGGERS | FUTURE_TRIGGERS
TONES = frozenset(
    {
        "calm",
        "gentle",
        "playful",
        "dry",
        "serious",
        "sleepy",
        "nostalgic",
        "curious",
        "intimate",
        "encouraging",
    }
)
SOURCE_KINDS = frozenset(
    {
        "rewritten_topic",
        "curated_standalone",
        "preserved_easter_egg",
        "new_ambient",
        "archived_question",
        "manual_review",
    }
)
ALLOWED_CONTEXT_TOKENS = frozenset(
    {
        "none",
        "app_started",
        "holiday",
        "anniversary",
        "ide_foreground",
        "active_90m",
        "idle_return",
        "not_fullscreen",
        "day:weekday",
        "day:weekend",
        "time:dawn",
        "time:morning",
        "time:noon",
        "time:afternoon",
        "time:evening",
        "time:late_night",
        "season:spring",
        "season:summer",
        "season:autumn",
        "season:winter",
        "date:holiday",
        "date:month_boundary",
    }
)
CONTEXT_TOKEN_PATTERN = re.compile(r"^[a-z][a-z0-9_]*(?::[a-z][a-z0-9_]*)?$")
ID_PATTERN = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._-]*$")

DIRECT_STATE_PATTERNS = (
    "你今天",
    "你现在",
    "你看起来",
    "我知道你",
    "你已经",
    "你又",
    "是不是又",
    "你的杯子",
    "你没休息",
    "你没有休息",
    "你很累",
    "你难过",
    "你焦虑",
)
TECHNICAL_CURRENT_PATTERNS = (
    "这个 bug",
    "这个bug",
    "这个空指针",
    "这个事务",
    "这条 SQL",
    "这条SQL",
    "这次死锁",
    "这个请求",
)
PII_MARKERS = (
    "雷琳玥",
)
PII_PATTERNS = (
    re.compile(r"(?<!\d)1[3-9]\d{9}(?!\d)"),
    re.compile(r"(?<!\d)\d{17}[\dXx](?!\d)"),
    re.compile(r"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}"),
)
STRONG_EMOTION_MARKERS = (
    "永远陪",
    "绝对不会离开",
    "离不开你",
    "只有我懂你",
    "崩溃",
    "绝望",
)
CATCHPHRASES = (
    "哈？",
    "我丢",
    "我靠",
    "真的假的",
    "啊推",
    "小笨蛋",
    "我真的不想多说什么了",
    "本姑娘",
    "玥玥",
)
ALLOWLIST_KEYS = frozenset({"line_id", "normalized_text_sha256", "reason"})
ALLOWLISTABLE_CODES = frozenset(
    {"fake_context", "user_direct_context", "technical_fake_context", "pii_enabled"}
)
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
        "interrupt_cost_minimum_intervals_minutes",
    }
)
FORMAT_ERROR_CODES = frozenset(
    {"config_format", "config_keys", "allowlist_format", "simulation_format"}
)
SIMULATION_KEYS = frozenset({"days", "seeds", "hard_violations", "plays", "metrics"})
SIMULATION_METRIC_KEYS = frozenset(
    {
        "actual_output_count",
        "category_group_ratio",
        "output_mode_ratio",
        "id_cooldown_violations",
        "semantic_cooldown_violations",
        "required_context_violations",
        "adjacent_technical",
        "adjacent_daily_care",
        "adjacent_emotional_reflection",
        "question_count",
    }
)
SIMULATION_PLAY_KEYS = frozenset(
    {
        "seed",
        "category_group",
        "output_mode",
        "question",
        "required_context_violation",
        "id_cooldown_violation",
        "semantic_cooldown_violation",
        "adjacent_group_violation",
    }
)


@dataclass(frozen=True, slots=True)
class ValidationIssue:
    code: str
    message: str
    line_id: str = ""
    row_number: int | None = None


@dataclass(frozen=True, slots=True)
class ValidationReport:
    errors: tuple[ValidationIssue, ...]
    warnings: tuple[ValidationIssue, ...]

    @property
    def hard_error_count(self) -> int:
        return len(self.errors)


class ValidationInputError(ValueError):
    """An input file cannot be interpreted under the strict validation contract."""


class _Issues:
    def __init__(self) -> None:
        self.errors: list[ValidationIssue] = []
        self.warnings: list[ValidationIssue] = []

    def error(
        self,
        code: str,
        message: str,
        line_id: object = "",
        row_number: int | None = None,
    ) -> None:
        self.errors.append(
            ValidationIssue(code, message, str(line_id) if line_id is not None else "", row_number)
        )

    def warning(
        self,
        code: str,
        message: str,
        line_id: object = "",
        row_number: int | None = None,
    ) -> None:
        self.warnings.append(
            ValidationIssue(code, message, str(line_id) if line_id is not None else "", row_number)
        )

    @staticmethod
    def _key(issue: ValidationIssue) -> tuple[str, str, int, str]:
        return (
            issue.code,
            issue.line_id,
            issue.row_number if issue.row_number is not None else -1,
            issue.message,
        )

    def report(self) -> ValidationReport:
        return ValidationReport(
            tuple(sorted(self.errors, key=self._key)),
            tuple(sorted(self.warnings, key=self._key)),
        )


def _is_finite_number(value: object) -> bool:
    return (
        isinstance(value, (int, float))
        and not isinstance(value, bool)
        and math.isfinite(float(value))
    )


def _is_integer(value: object) -> bool:
    return isinstance(value, int) and not isinstance(value, bool)


def normalized_text_sha256(text: str) -> str:
    return hashlib.sha256(normalize_text(text).encode("utf-8")).hexdigest()


def _json_pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def _reject_json_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number {value!r}")


def load_json_object(path: Path) -> dict[str, Any]:
    path = Path(path)
    try:
        payload = path.read_text(encoding="utf-8-sig")
        value = json.loads(
            payload,
            object_pairs_hook=_json_pairs,
            parse_constant=_reject_json_constant,
        )
    except (OSError, UnicodeError, json.JSONDecodeError, ValueError) as error:
        raise ValidationInputError(f"{path}: invalid JSON: {error}") from error
    if not isinstance(value, dict):
        raise ValidationInputError(f"{path}: JSON root must be an object")
    return value


def _validate_weight_map(config: Mapping[str, object], issues: _Issues) -> None:
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

    technical = values.get("technical")
    if technical is None or not 0.10 <= technical <= 0.20:
        issues.error("technical_weight", "technical playback weight must be in [0.10, 0.20]")
    easter = values.get("easter_egg")
    if easter is None or easter > 0.02:
        issues.error("easter_egg_config_weight", "easter_egg playback weight must not exceed 0.02")
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
    if (
        abs(sum(targets.values()) - 1.0) > 1e-9
        or targets["self_talk"] + targets["ambient"] < 0.65
        or targets["user_direct"] > 0.15
    ):
        issues.error(
            "output_mode_targets",
            "output mode targets must sum to 1.0, keep self_talk+ambient >= 0.65 and user_direct <= 0.15",
        )


def _valid_int_limit(value: object, *, minimum: int, maximum: int | None = None) -> bool:
    return _is_integer(value) and value >= minimum and (maximum is None or value <= maximum)


def _validate_runtime_limits(config: Mapping[str, object], issues: _Issues) -> None:
    raw = config.get("runtime_limits")
    if not isinstance(raw, Mapping) or set(raw) != RUNTIME_LIMIT_KEYS:
        issues.error("runtime_limits", "runtime_limits must use the exact Task 5 key set")
        return
    valid = True
    valid &= _valid_int_limit(raw.get("minimum_interval_minutes"), minimum=8)
    valid &= _valid_int_limit(raw.get("max_outputs_per_hour"), minimum=1, maximum=2)
    valid &= _valid_int_limit(
        raw.get("late_night_max_outputs_per_hour"), minimum=1, maximum=1
    )
    valid &= raw.get("semantic_group_no_repeat") is True

    adjacent = raw.get("block_adjacent_category_groups")
    valid &= (
        isinstance(adjacent, list)
        and len(adjacent) == len(set(adjacent))
        and set(adjacent)
        == {"technical", "daily_care", "emotional_reflection"}
    )
    expected_pairs = {
        "technical_recent_window": 5,
        "technical_recent_max": 2,
        "user_direct_recent_window": 10,
        "user_direct_recent_max": 2,
        "easter_egg_recent_window": 50,
        "easter_egg_recent_max": 1,
    }
    valid &= all(raw.get(name) == value and _is_integer(raw.get(name)) for name, value in expected_pairs.items())

    intervals = raw.get("interrupt_cost_minimum_intervals_minutes")
    if not isinstance(intervals, Mapping) or set(intervals) != {str(value) for value in range(6)}:
        valid = False
    else:
        ordered = [intervals[str(value)] for value in range(6)]
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


def _required_context_tokens(value: object) -> tuple[str, ...] | None:
    if not isinstance(value, str) or not value:
        return None
    tokens = tuple(value.split(","))
    if (
        any(not token or token.strip() != token for token in tokens)
        or len(tokens) != len(set(tokens))
        or any(CONTEXT_TOKEN_PATTERN.fullmatch(token) is None for token in tokens)
        or any(token not in ALLOWED_CONTEXT_TOKENS for token in tokens)
        or ("none" in tokens and tokens != ("none",))
    ):
        return None
    return tokens


def _looks_like_pii(text: str) -> bool:
    if any(marker in text for marker in PII_MARKERS) or any(
        pattern.search(text) for pattern in PII_PATTERNS
    ):
        return True
    location = r"(?:湖南|长沙|广东)"
    personal_location = (
        rf"(?:我的老家|老家|来自|住在|漂在|生活在|我(?:住|来自|漂|生活|长大)).{{0,10}}{location}",
        rf"{location}.{{0,10}}(?:是我老家|长大|漂泊|打工)",
    )
    income = r"(?:工资|收入|月薪)"
    personal_income = (
        rf"(?:我|我的).{{0,10}}{income}",
        rf"{income}(?:(?:是|有|约|大概|只有|才))?\s*[\d零一二三四五六七八九十百千万两]+(?:元|块|千|万|[kK])",
    )
    personal_work = r"(?:我|我的).{0,12}打零工|打零工.{0,12}(?:我|那几年|经历)"
    return (
        any(re.search(pattern, text) for pattern in personal_location)
        or any(re.search(pattern, text) for pattern in personal_income)
        or re.search(personal_work, text) is not None
    )


def _trigger_context_conflict(trigger: object, tokens: tuple[str, ...] | None) -> bool:
    if not isinstance(trigger, str) or tokens is None:
        return False
    expected = {
        "morning": ("time", frozenset({"dawn", "morning"})),
        "noon": ("time", frozenset({"noon"})),
        "afternoon": ("time", frozenset({"afternoon"})),
        "evening": ("time", frozenset({"evening"})),
        "late_night": ("time", frozenset({"late_night"})),
        "weekday": ("day", frozenset({"weekday"})),
        "weekend": ("day", frozenset({"weekend"})),
    }.get(trigger)
    if expected is None:
        return False
    dimension, values = expected
    dimension_tokens = [token for token in tokens if token.startswith(f"{dimension}:")]
    allowed_tokens = {f"{dimension}:{value}" for value in values}
    return bool(dimension_tokens) and not any(
        token in allowed_tokens for token in dimension_tokens
    )


def _validate_line(row: CorpusLine, row_number: int, issues: _Issues) -> None:
    line_id = row.id
    required_strings = (
        "id",
        "category",
        "category_group",
        "topic_id",
        "semantic_group",
        "output_mode",
        "trigger",
        "required_context",
        "tone",
        "text",
        "source_kind",
        "source_reference",
        "rewrite_reason",
    )
    for field in required_strings:
        value = getattr(row, field)
        if not isinstance(value, str) or not value.strip():
            issues.error("required_field", f"{field} must be a non-empty string", line_id, row_number)
    if isinstance(row.id, str) and row.id and ID_PATTERN.fullmatch(row.id) is None:
        issues.error("invalid_id", "id must use only stable ASCII identifier characters", line_id, row_number)
    if row.category_group not in CATEGORY_GROUPS:
        issues.error("invalid_category_group", f"unknown category_group {row.category_group!r}", line_id, row_number)
    if row.output_mode not in OUTPUT_MODES:
        issues.error("invalid_output_mode", f"unknown output_mode {row.output_mode!r}", line_id, row_number)
    if row.trigger not in TRIGGERS:
        issues.error("invalid_trigger", f"unknown trigger {row.trigger!r}", line_id, row_number)
    if row.tone not in TONES:
        issues.error("invalid_tone", f"unknown tone {row.tone!r}", line_id, row_number)
    if row.source_kind not in SOURCE_KINDS:
        issues.error("invalid_source_kind", f"unknown source_kind {row.source_kind!r}", line_id, row_number)
    if not _is_integer(row.interrupt_cost) or not 0 <= row.interrupt_cost <= 5:
        issues.error("invalid_interrupt_cost", "interrupt_cost must be an integer in [0, 5]", line_id, row_number)
    if not _is_finite_number(row.cooldown_hours) or row.cooldown_hours < 1:
        issues.error("invalid_cooldown", "cooldown_hours must be finite and >= 1", line_id, row_number)
    if not _is_finite_number(row.semantic_cooldown_hours) or row.semantic_cooldown_hours < 1:
        issues.error(
            "invalid_semantic_cooldown",
            "semantic_cooldown_hours must be finite and >= 1",
            line_id,
            row_number,
        )
    elif _is_finite_number(row.cooldown_hours) and row.semantic_cooldown_hours < row.cooldown_hours:
        issues.error(
            "semantic_cooldown_shorter",
            "semantic_cooldown_hours must not be shorter than the row cooldown",
            line_id,
            row_number,
        )
    if not _is_integer(row.max_per_day) or row.max_per_day not in {1, 2}:
        issues.error("invalid_max_per_day", "max_per_day must be integer 1 or 2", line_id, row_number)
    if not _is_finite_number(row.weight) or not 0 < row.weight <= 2:
        issues.error("invalid_weight", "weight must be finite and in (0, 2]", line_id, row_number)
    if not isinstance(row.requires_reply, bool) or not isinstance(row.enabled, bool):
        issues.error("invalid_boolean", "requires_reply and enabled must be booleans", line_id, row_number)

    tokens = _required_context_tokens(row.required_context)
    if tokens is None:
        issues.error(
            "invalid_required_context",
            "required_context must be comma-separated controlled tokens; none must stand alone",
            line_id,
            row_number,
        )
    has_context = tokens is not None and tokens != ("none",)
    text = row.text if isinstance(row.text, str) else ""
    enabled = row.enabled is True
    if isinstance(row.text, str) and not normalize_text(row.text):
        issues.error(
            "normalized_text_empty",
            "text is empty after NFKC/casefold/punctuation/format normalization",
            line_id,
            row_number,
        )
    if _trigger_context_conflict(row.trigger, tokens):
        issues.error(
            "trigger_context_conflict",
            f"trigger {row.trigger!r} conflicts with required_context {row.required_context!r}",
            line_id,
            row_number,
        )
    if any(character in text for character in ("\t", "\r", "\n")) or any(
        unicoded in text for unicoded in ("\u2028", "\u2029")
    ):
        issues.error("control_character", "text contains a tab or physical line separator", line_id, row_number)
    if enabled and row.requires_reply is True:
        issues.error("requires_reply", "enabled text must not require a reply", line_id, row_number)
    if enabled and ("?" in text or "？" in text):
        issues.error("question", "enabled original text contains a question mark", line_id, row_number)

    direct_state = next((pattern for pattern in DIRECT_STATE_PATTERNS if pattern in text), None)
    if enabled and direct_state and not has_context:
        issues.error(
            "fake_context",
            f"text asserts unavailable user context via {direct_state!r}",
            line_id,
            row_number,
        )
        if row.output_mode == "user_direct":
            issues.error(
                "user_direct_context",
                "user_direct state assertion needs a non-none required_context gate",
                line_id,
                row_number,
            )
    if (
        enabled
        and row.category_group == "technical"
        and row.output_mode == "user_direct"
        and (tokens is None or "ide_foreground" not in tokens)
    ):
        issues.error(
            "user_direct_context",
            "technical user_direct text must be gated by ide_foreground",
            line_id,
            row_number,
        )
    folded_text = text.casefold()
    technical_pattern = next(
        (
            pattern
            for pattern in TECHNICAL_CURRENT_PATTERNS
            if pattern.casefold() in folded_text
        ),
        None,
    )
    if enabled and row.category_group == "technical" and technical_pattern and not has_context:
        issues.error(
            "technical_fake_context",
            f"technical text uses current-object shorthand {technical_pattern!r} without context",
            line_id,
            row_number,
        )
    if enabled and _looks_like_pii(text):
        issues.error(
            "pii_enabled",
            "enabled text matches a name, location, income, employment or identifier PII heuristic",
            line_id,
            row_number,
        )

    if enabled and row.category_group == "easter_egg":
        rare = any(
            marker in f"{row.semantic_group};{row.source_reference}".lower()
            for marker in ("rare", "privacy", "anniversary")
        )
        minimum = 720 if rare else 168
        if not _is_finite_number(row.cooldown_hours) or row.cooldown_hours < minimum:
            issues.error(
                "easter_egg_cooldown",
                f"EasterEgg cooldown_hours must be >= {minimum}",
                line_id,
                row_number,
            )
        if row.max_per_day != 1 or isinstance(row.max_per_day, bool):
            issues.error(
                "easter_egg_daily_limit", "EasterEgg max_per_day must be 1", line_id, row_number
            )
        if not _is_finite_number(row.weight) or row.weight > 0.10:
            issues.error(
                "easter_egg_row_weight",
                "EasterEgg row weight must not exceed 0.10",
                line_id,
                row_number,
            )

    if _is_integer(row.interrupt_cost) and row.interrupt_cost >= 4 and _is_finite_number(row.weight) and row.weight > 0.5:
        issues.error(
            "high_cost_weight",
            "interrupt_cost 4-5 content must use weight <= 0.5",
            line_id,
            row_number,
        )
    strong_emotion = row.tone == "intimate" or (
        row.category_group != "technical"
        and any(marker in text for marker in STRONG_EMOTION_MARKERS)
    )
    if enabled and strong_emotion and _is_finite_number(row.weight) and row.weight > 0.5:
        issues.error(
            "high_emotion_weight",
            "strong emotional content must use weight <= 0.5",
            line_id,
            row_number,
        )
    if enabled and row.source_kind in {"archived_question", "manual_review"}:
        issues.error(
            "unsafe_source_kind",
            "archived_question and manual_review rows cannot be enabled",
            line_id,
            row_number,
        )

    lineage = row.source_reference.lower() if isinstance(row.source_reference, str) else ""
    if (
        re.search(r"(?:^|;)prefix:[^;]+", lineage)
        and re.search(r"(?:^|;)(?:core|topic):[^;]+", lineage)
        and re.search(r"(?:^|;)suffix:[^;]+", lineage)
    ):
        issues.error(
            "cartesian_signature",
            "runtime row exposes prefix/core/suffix combination lineage",
            line_id,
            row_number,
        )


def _duplicate_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    id_rows: dict[object, list[int]] = defaultdict(list)
    exact_rows: dict[str, list[str]] = defaultdict(list)
    normalized_rows: dict[str, list[str]] = defaultdict(list)
    for index, row in enumerate(rows, start=2):
        id_rows[row.id].append(index)
        if row.enabled is True and isinstance(row.text, str):
            exact_rows[row.text].append(str(row.id))
            normalized_rows[normalize_text(row.text)].append(str(row.id))
    for value, positions in id_rows.items():
        if len(positions) > 1:
            issues.error(
                "duplicate_id",
                f"id {value!r} occurs on {len(positions)} rows",
                value,
                min(positions),
            )
    for text, ids in exact_rows.items():
        if len(ids) > 1:
            issues.error(
                "duplicate_text",
                f"enabled text occurs {len(ids)} times: {text!r}",
                min(ids),
            )
    for text, ids in normalized_rows.items():
        if len(ids) > 1:
            issues.error(
                "duplicate_normalized_text",
                f"normalized enabled text occurs {len(ids)} times: {text!r}",
                min(ids),
            )


def _cartesian_grid_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    by_topic: dict[tuple[str, str], list[CorpusLine]] = defaultdict(list)
    for row in rows:
        if row.enabled is True and isinstance(row.text, str):
            by_topic[(str(row.category), str(row.topic_id))].append(row)
    for (category, topic_id), topic_rows in sorted(by_topic.items()):
        if len(topic_rows) < 8:
            continue
        texts = [row.text for row in topic_rows]
        detected = False
        for prefix_width in range(2, 7):
            for suffix_width in (4, 6, 8, 10):
                eligible = [text for text in texts if len(text) >= prefix_width + suffix_width + 1]
                if len(eligible) < 8:
                    continue
                pairs = {(text[:prefix_width], text[-suffix_width:]) for text in eligible}
                prefixes = {prefix for prefix, _ in pairs}
                suffixes = {suffix for _, suffix in pairs}
                pair_product = (
                    len(prefixes) >= 3
                    and len(suffixes) >= 3
                    and len(pairs) == len(prefixes) * len(suffixes)
                    and len(pairs) == len(eligible)
                )
                triples = {
                    (text[:prefix_width], text[prefix_width:-suffix_width], text[-suffix_width:])
                    for text in eligible
                }
                cores = {core for _, core, _ in triples}
                cube_size = len(prefixes) * len(cores) * len(suffixes)
                cube_product = (
                    len(prefixes) >= 2
                    and len(cores) >= 2
                    and len(suffixes) >= 2
                    and cube_size >= 8
                    and len(triples) / cube_size >= 0.90
                )
                if pair_product or cube_product:
                    detected = True
                    break
            if detected:
                break
        if detected:
            issues.error(
                "cartesian_signature",
                f"topic {category}/{topic_id} forms a complete repeated opening-ending grid",
            )


def _distribution_issues(rows: Sequence[CorpusLine], issues: _Issues) -> None:
    texts = [row.text for row in rows if row.enabled is True and isinstance(row.text, str)]
    count = len(texts)
    if count >= 20:
        catchphrase_lines = sum(
            1 for text in texts if any(phrase in text for phrase in CATCHPHRASES)
        )
        if catchphrase_lines / count > 0.10 + 1e-12:
            issues.error(
                "catchphrase_frequency",
                f"catchphrases appear in {catchphrase_lines}/{count} enabled texts, above 10%",
            )

        average = sum(map(len, texts)) / count
        shares = {
            "8-16": sum(8 <= len(text) <= 16 for text in texts) / count,
            "17-24": sum(17 <= len(text) <= 24 for text in texts) / count,
            "25-36": sum(25 <= len(text) <= 36 for text in texts) / count,
            ">36": sum(len(text) > 36 for text in texts) / count,
        }
        if not (
            18 <= average <= 26
            and 0.25 <= shares["8-16"] <= 0.35
            and 0.35 <= shares["17-24"] <= 0.45
            and 0.20 <= shares["25-36"] <= 0.30
            and shares[">36"] <= 0.08
        ):
            issues.error(
                "length_distribution",
                "enabled length distribution must meet average 18-26 and the 8-16/17-24/25-36/>36 targets; "
                f"observed average={average:.3f}, shares={shares}",
            )

    if count >= 100:
        for width in range(2, 7):
            frequencies = Counter(text[:width] for text in texts if len(text) >= width)
            if frequencies:
                phrase, frequency = frequencies.most_common(1)[0]
                if frequency / count > 0.02 + 1e-12:
                    issues.error(
                        "opening_frequency",
                        f"{width}-character opening {phrase!r} appears {frequency}/{count} times, above 2%",
                    )
        for width in (4, 6, 8, 10):
            frequencies = Counter(text[-width:] for text in texts if len(text) >= width)
            if frequencies:
                phrase, frequency = frequencies.most_common(1)[0]
                if frequency / count > 0.02 + 1e-12:
                    issues.error(
                        "ending_frequency",
                        f"{width}-character ending {phrase!r} appears {frequency}/{count} times, above 2%",
                    )


def _simulation_issues(simulation: object | None, issues: _Issues) -> None:
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
    days = simulation.get("days")
    if not _is_integer(days) or days < 30:
        issues.error("simulation_duration", "simulation must cover at least 30 days")
    seeds = simulation.get("seeds")
    seeds_valid = (
        isinstance(seeds, list)
        and all(_is_integer(seed) for seed in seeds)
        and len(seeds) == len(set(seeds))
        and len(seeds) >= 10
    )
    if not seeds_valid:
        issues.error("simulation_seed_count", "simulation must contain at least 10 distinct seeds")
    violations = simulation.get("hard_violations")
    if not isinstance(violations, list):
        issues.error("simulation_format", "hard_violations must be an array")
    else:
        for violation in violations:
            detail = json.dumps(violation, ensure_ascii=False, sort_keys=True)
            issues.error("simulation_hard_violation", f"simulation reported: {detail}")

    metrics = simulation.get("metrics")
    if not isinstance(metrics, Mapping) or set(metrics) != SIMULATION_METRIC_KEYS:
        issues.error("simulation_format", "simulation metrics must be an object")
        return
    plays = simulation.get("plays")
    if not isinstance(plays, list):
        issues.error("simulation_format", "simulation plays must be an array")
        return
    if not plays:
        issues.error("simulation_zero_outputs", "simulation must contain at least one actual output")
        return

    play_format_valid = True
    group_counts: Counter[str] = Counter()
    mode_counts: Counter[str] = Counter()
    computed_flags = {
        "id_cooldown_violations": 0,
        "semantic_cooldown_violations": 0,
        "required_context_violations": 0,
        "question_count": 0,
    }
    for index, play in enumerate(plays):
        if not isinstance(play, Mapping) or set(play) != SIMULATION_PLAY_KEYS:
            issues.error("simulation_format", f"simulation play {index} has unknown or missing keys")
            play_format_valid = False
            continue
        seed = play.get("seed")
        category_group = play.get("category_group")
        output_mode = play.get("output_mode")
        flags = (
            "question",
            "required_context_violation",
            "id_cooldown_violation",
            "semantic_cooldown_violation",
            "adjacent_group_violation",
        )
        if (
            not _is_integer(seed)
            or (seeds_valid and seed not in seeds)
            or category_group not in CATEGORY_GROUPS
            or output_mode not in OUTPUT_MODES
            or any(not isinstance(play.get(flag), bool) for flag in flags)
        ):
            issues.error("simulation_format", f"simulation play {index} has invalid field values")
            play_format_valid = False
            continue
        group_counts[str(category_group)] += 1
        mode_counts[str(output_mode)] += 1
        computed_flags["question_count"] += int(play["question"])
        computed_flags["required_context_violations"] += int(
            play["required_context_violation"]
        )
        computed_flags["id_cooldown_violations"] += int(play["id_cooldown_violation"])
        computed_flags["semantic_cooldown_violations"] += int(
            play["semantic_cooldown_violation"]
        )
        if play["adjacent_group_violation"]:
            issues.error(
                "simulation_hard_violation",
                f"simulation play {index} reports an adjacent constrained-group violation",
            )

    if not play_format_valid:
        return
    output_count = len(plays)
    if metrics.get("actual_output_count") != output_count or isinstance(
        metrics.get("actual_output_count"), bool
    ):
        issues.error(
            "simulation_aggregate_mismatch",
            "actual_output_count does not equal the structured plays length",
        )
    group_ratio = metrics.get("category_group_ratio")
    mode_ratio = metrics.get("output_mode_ratio")
    valid_ratios = (
        isinstance(group_ratio, Mapping)
        and isinstance(mode_ratio, Mapping)
        and set(group_ratio) == CATEGORY_GROUPS
        and set(mode_ratio) == OUTPUT_MODES
        and all(
            _is_finite_number(value) and 0 <= float(value) <= 1
            for value in group_ratio.values()
        )
        and all(
            _is_finite_number(value) and 0 <= float(value) <= 1
            for value in mode_ratio.values()
        )
        and abs(sum(float(value) for value in group_ratio.values()) - 1.0) <= 1e-9
        and abs(sum(float(value) for value in mode_ratio.values()) - 1.0) <= 1e-9
    )
    if valid_ratios:
        needed = (
            group_ratio.get("technical"),
            group_ratio.get("easter_egg"),
            mode_ratio.get("self_talk"),
            mode_ratio.get("ambient"),
            mode_ratio.get("user_direct"),
        )
        valid_ratios = all(_is_finite_number(value) for value in needed)
    if not valid_ratios:
        issues.error("simulation_metric", "simulation ratio metrics are missing or non-finite")
    else:
        technical = float(group_ratio["technical"])
        easter = float(group_ratio["easter_egg"])
        self_talk = float(mode_ratio["self_talk"])
        ambient = float(mode_ratio["ambient"])
        user_direct = float(mode_ratio["user_direct"])
        if (
            not 0.10 <= technical <= 0.20
            or easter > 0.02
            or self_talk + ambient < 0.65
            or user_direct > 0.15
        ):
            issues.error(
                "simulation_metric",
                "simulation ratios violate technical, EasterEgg or output-mode acceptance bounds",
            )
        expected_ratios = {
            name: group_counts[name] / output_count for name in CATEGORY_GROUPS
        }
        expected_modes = {
            name: mode_counts[name] / output_count
            for name in ("self_talk", "ambient", "user_direct", "system_observe")
        }
        if any(
            abs(float(group_ratio.get(name, math.nan)) - value) > 1e-9
            for name, value in expected_ratios.items()
        ) or any(
            not _is_finite_number(mode_ratio.get(name))
            or abs(float(mode_ratio[name]) - value) > 1e-9
            for name, value in expected_modes.items()
        ):
            issues.error(
                "simulation_aggregate_mismatch",
                "reported group/output-mode ratios do not match structured plays",
            )
    zero_metrics = (
        "id_cooldown_violations",
        "semantic_cooldown_violations",
        "required_context_violations",
        "adjacent_technical",
        "adjacent_daily_care",
        "adjacent_emotional_reflection",
        "question_count",
    )
    for name in zero_metrics:
        value = metrics.get(name)
        if not _is_integer(value) or value != 0:
            issues.error("simulation_metric", f"simulation metric {name} must be integer zero")
    for name, computed in computed_flags.items():
        if metrics.get(name) != computed:
            issues.error(
                "simulation_aggregate_mismatch",
                f"reported {name}={metrics.get(name)!r} does not match structured plays value {computed}",
            )


def _apply_allowlist(
    rows: Sequence[CorpusLine],
    allowlist: object,
    issues: _Issues,
    *,
    expected_corpus_sha256: str,
    require_corpus_binding: bool,
) -> None:
    if not isinstance(allowlist, Mapping):
        issues.error("allowlist_format", "allowlist root must be a JSON object")
        return
    keys = set(allowlist)
    legacy_unbound = keys == {"exceptions"} and not require_corpus_binding
    bound = keys == {"schema_version", "corpus_sha256", "exceptions"}
    if not legacy_unbound and not bound:
        issues.error(
            "allowlist_format",
            "file allowlist must contain exactly schema_version, corpus_sha256 and exceptions",
        )
        return
    if bound:
        digest = allowlist.get("corpus_sha256")
        if allowlist.get("schema_version") != 1 or isinstance(
            allowlist.get("schema_version"), bool
        ):
            issues.error("allowlist_format", "allowlist schema_version must be integer 1")
            return
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            issues.error("allowlist_format", "allowlist corpus_sha256 must be lowercase SHA-256")
            return
        if digest != expected_corpus_sha256:
            issues.error(
                "allowlist_corpus_hash_mismatch",
                "allowlist corpus_sha256 does not match the exact corpus under validation",
            )
            return
    entries = allowlist.get("exceptions")
    if not isinstance(entries, list):
        issues.error("allowlist_format", "allowlist exceptions must be an array")
        return

    by_id: dict[str, list[CorpusLine]] = defaultdict(list)
    for row in rows:
        by_id[str(row.id)].append(row)
    seen: set[str] = set()
    active: dict[str, str] = {}
    invalid_ids: set[str] = set()
    for position, entry in enumerate(entries, start=1):
        if not isinstance(entry, Mapping) or set(entry) != ALLOWLIST_KEYS:
            issues.error(
                "allowlist_format",
                f"allowlist exception {position} must contain exactly line_id, normalized_text_sha256 and reason",
            )
            continue
        line_id = entry.get("line_id")
        digest = entry.get("normalized_text_sha256")
        reason = entry.get("reason")
        if (
            not isinstance(line_id, str)
            or not line_id
            or not isinstance(digest, str)
            or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            or not isinstance(reason, str)
            or not reason.strip()
        ):
            issues.error("allowlist_format", f"allowlist exception {position} has invalid values")
            continue
        if line_id in seen:
            issues.error("allowlist_duplicate", f"allowlist line_id {line_id!r} occurs more than once", line_id)
            invalid_ids.add(line_id)
            continue
        seen.add(line_id)
        matches = by_id.get(line_id, [])
        if len(matches) != 1:
            issues.error("allowlist_unknown_line", f"allowlist line_id {line_id!r} does not resolve uniquely", line_id)
            invalid_ids.add(line_id)
            continue
        actual = normalized_text_sha256(matches[0].text)
        if actual != digest:
            issues.error(
                "allowlist_hash_mismatch",
                f"allowlist normalized-text SHA-256 for {line_id!r} is stale or mismatched",
                line_id,
            )
            invalid_ids.add(line_id)
            continue
        active[line_id] = reason.strip()

    original_errors = list(issues.errors)
    retained: list[ValidationIssue] = []
    used: set[str] = set()
    for issue in original_errors:
        reason = active.get(issue.line_id)
        if reason is not None and issue.line_id not in invalid_ids and issue.code in ALLOWLISTABLE_CODES:
            used.add(issue.line_id)
            issues.warning(
                f"allowlisted_{issue.code}",
                f"{issue.message} Exception reason: {reason}",
                issue.line_id,
                issue.row_number,
            )
        else:
            retained.append(issue)
    issues.errors = retained
    for line_id in sorted(set(active) - used - invalid_ids):
        issues.error(
            "allowlist_stale",
            f"allowlist entry for {line_id!r} no longer matches an allowlistable heuristic finding",
            line_id,
        )


def validate_corpus(
    lines: Sequence[CorpusLine],
    scheduler_config: object,
    allowlist: object,
    simulation_result: object | None = None,
    *,
    _corpus_sha256: str | None = None,
    _require_allowlist_binding: bool = False,
    _enforce_canonical_size: bool = False,
) -> ValidationReport:
    rows = tuple(lines)
    issues = _Issues()
    config_report = validate_config(scheduler_config)
    issues.errors.extend(config_report.errors)
    issues.warnings.extend(config_report.warnings)
    for row_number, row in enumerate(rows, start=2):
        if not isinstance(row, CorpusLine):
            issues.error("row_type", "validate_corpus accepts CorpusLine objects", row_number=row_number)
            continue
        _validate_line(row, row_number, issues)
    typed_rows = tuple(row for row in rows if isinstance(row, CorpusLine))
    if _enforce_canonical_size:
        enabled_count = sum(row.enabled is True for row in typed_rows)
        if not 800 <= enabled_count <= 1200:
            issues.error(
                "enabled_count",
                f"canonical runtime corpus must contain 800-1200 enabled rows; found {enabled_count}",
            )
    _duplicate_issues(typed_rows, issues)
    _cartesian_grid_issues(typed_rows, issues)
    _distribution_issues(typed_rows, issues)
    _simulation_issues(simulation_result, issues)
    if _corpus_sha256 is None:
        from .builder import serialize_v2

        try:
            payload = serialize_v2(typed_rows)
        except ValueError:
            payload = repr(typed_rows).encode("utf-8", errors="backslashreplace")
        _corpus_sha256 = hashlib.sha256(payload).hexdigest()
    _apply_allowlist(
        typed_rows,
        allowlist,
        issues,
        expected_corpus_sha256=_corpus_sha256,
        require_corpus_binding=_require_allowlist_binding,
    )
    return issues.report()


def validate_file(
    corpus_path: Path,
    config_path: Path,
    allowlist_path: Path,
    simulation_path: Path | None = None,
) -> ValidationReport:
    corpus_path = Path(corpus_path)
    try:
        lines = load_v2(corpus_path)
    except CorpusFormatError as error:
        raise ValidationInputError(str(error)) from error
    config = load_json_object(Path(config_path))
    allowlist = load_json_object(Path(allowlist_path))
    simulation = load_json_object(Path(simulation_path)) if simulation_path is not None else None
    try:
        corpus_sha256 = hashlib.sha256(corpus_path.read_bytes()).hexdigest()
    except OSError as error:
        raise ValidationInputError(f"{corpus_path}: cannot hash corpus: {error}") from error
    return validate_corpus(
        lines,
        config,
        allowlist,
        simulation_result=simulation,
        _corpus_sha256=corpus_sha256,
        _require_allowlist_binding=True,
        _enforce_canonical_size=True,
    )


def format_report(report: ValidationReport) -> str:
    lines = [
        f"Validation: {len(report.errors)} hard errors, {len(report.warnings)} warnings"
    ]
    for severity, entries in (("ERROR", report.errors), ("WARNING", report.warnings)):
        for issue in entries:
            location = ""
            if issue.line_id:
                location += f" [{issue.line_id}]"
            if issue.row_number is not None:
                location += f" [row {issue.row_number}]"
            lines.append(f"{severity} {issue.code}{location}: {issue.message}")
    return "\n".join(lines)


__all__ = [
    "FORMAT_ERROR_CODES",
    "VALIDATION_GROUPS",
    "ValidationInputError",
    "ValidationIssue",
    "ValidationReport",
    "format_report",
    "load_json_object",
    "normalized_text_sha256",
    "validate_config",
    "validate_corpus",
    "validate_file",
]
