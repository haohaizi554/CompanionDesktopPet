from __future__ import annotations

import calendar
import hashlib
import json
import math
import re
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta
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
COMMON_CHINESE_SURNAMES = (
    "赵钱孙李周吴郑王冯陈褚卫蒋沈韩杨朱秦尤许何吕施张孔曹严华"
    "金魏陶姜戚谢邹喻柏水窦章云苏潘葛奚范彭郎鲁韦昌马苗凤花方"
    "俞任袁柳鲍史唐费廉岑薛雷贺倪汤滕殷罗毕郝邬安常乐于时傅皮"
    "卞齐康伍余元顾孟平黄和穆萧尹姚邵湛汪祁毛禹狄米贝明臧计伏"
    "成戴谈宋茅庞熊纪舒屈项祝董梁杜阮蓝闵季贾路娄江童颜郭梅盛"
    "林刁钟徐邱骆高夏蔡田樊胡凌霍虞万支柯管卢莫经房裘缪干解应"
    "宗丁宣贲邓郁单杭洪包诸左石崔吉龚程邢裴陆荣翁荀羊惠甄曲封"
    "芮羿储靳汲邴糜松井段富巫乌焦巴弓牧隗山谷车侯宓蓬全班仰秋"
    "仲伊宫宁仇栾暴甘厉戎祖武符刘景詹束龙叶幸司韶郜黎蓟薄印宿"
    "白怀蒲邰鄂索咸籍赖卓蔺屠蒙池乔阴胥能苍双闻莘党翟谭贡劳姬"
    "申扶堵冉宰郦雍郤璩桑桂濮牛寿通边扈燕冀郏浦尚农温别庄晏柴"
    "瞿阎充慕连茹习宦艾鱼容向古易慎戈廖庾终暨居衡步都耿满弘匡"
    "国文寇广禄阙东欧利师巩聂关荆司马欧阳上官诸葛夏侯东方"
)
COMMON_CHINESE_GIVEN_NAMES = (
    "伟",
    "芳",
    "娜",
    "敏",
    "静",
    "丽",
    "强",
    "磊",
    "军",
    "洋",
    "勇",
    "艳",
    "杰",
    "娟",
    "涛",
    "超",
    "明",
    "华",
    "平",
    "刚",
    "英",
    "霞",
    "凤",
    "兰",
    "秀英",
    "桂英",
    "秀兰",
    "建华",
    "建国",
    "国强",
    "志强",
    "志伟",
    "文华",
    "小明",
    "小红",
    "雨桐",
    "子涵",
    "梓涵",
    "浩宇",
    "俊杰",
    "佳怡",
    "佳琪",
    "欣怡",
    "梦瑶",
    "诗涵",
    "宇轩",
    "浩然",
    "一诺",
    "可欣",
    "思雨",
    "欣妍",
)
NAME_CONTEXT_MARKERS = (
    "今天",
    "昨天",
    "明天",
    "刚才",
    "曾经",
    "目前",
    "现在",
    "以前",
    "住在",
    "来自",
    "出生于",
    "来了",
    "来过",
    "来到",
    "去了",
    "说过",
    "说道",
    "说",
    "告诉",
    "联系",
    "上班",
    "工作",
    "请假",
    "发来",
    "写了",
)
CONTEXTUAL_CHINESE_NAME_PATTERN = re.compile(
    rf"(?<![\u3400-\u9fff])"
    rf"[{COMMON_CHINESE_SURNAMES}]"
    rf"(?:{'|'.join(sorted(map(re.escape, COMMON_CHINESE_GIVEN_NAMES), key=len, reverse=True))})"
    rf"(?=(?:{'|'.join(map(re.escape, NAME_CONTEXT_MARKERS))}))"
)
LABELED_CHINESE_NAME_PATTERN = re.compile(
    rf"(?:姓名|名字|真名|叫作|叫|我是)\s*[:：]?\s*"
    rf"[{COMMON_CHINESE_SURNAMES}][\u3400-\u9fff]{{1,2}}"
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
ALLOWLIST_KEYS = frozenset(
    {"rule_code", "line_id", "normalized_text_sha256", "reason"}
)
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
        "long_silence_minutes",
        "interrupt_cost_minimum_intervals_minutes",
    }
)
FORMAT_ERROR_CODES = frozenset(
    {"config_format", "config_keys", "allowlist_format", "simulation_format"}
)
SIMULATION_KEYS = frozenset(
    {
        "schema_version",
        "corpus_sha256",
        "scheduler_config_sha256",
        "days",
        "seeds",
        "attempts",
    }
)
SIMULATION_ATTEMPT_KEYS = frozenset(
    {"seed", "attempted_at", "context", "selected_id"}
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


def scheduler_config_sha256(config: object) -> str:
    """Hash the scheduler's semantic JSON value, independent of formatting."""

    payload = json.dumps(
        config,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
        allow_nan=False,
    ).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


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
            == {"technical", "daily_care", "emotional_reflection"}
        )
    expected_pairs = {
        "technical_recent_window": 5,
        "technical_recent_max": 2,
        "user_direct_recent_window": 10,
        "user_direct_recent_max": 2,
        "easter_egg_recent_window": 50,
        "easter_egg_recent_max": 1,
        "long_silence_minutes": 180,
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
    ) or CONTEXTUAL_CHINESE_NAME_PATTERN.search(text) or LABELED_CHINESE_NAME_PATTERN.search(text):
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
    if not isinstance(row.category_group, str) or row.category_group not in CATEGORY_GROUPS:
        issues.error("invalid_category_group", f"unknown category_group {row.category_group!r}", line_id, row_number)
    if not isinstance(row.output_mode, str) or row.output_mode not in OUTPUT_MODES:
        issues.error("invalid_output_mode", f"unknown output_mode {row.output_mode!r}", line_id, row_number)
    if not isinstance(row.trigger, str) or row.trigger not in TRIGGERS:
        issues.error("invalid_trigger", f"unknown trigger {row.trigger!r}", line_id, row_number)
    if not isinstance(row.tone, str) or row.tone not in TONES:
        issues.error("invalid_tone", f"unknown tone {row.tone!r}", line_id, row_number)
    if not isinstance(row.source_kind, str) or row.source_kind not in SOURCE_KINDS:
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
    if (
        enabled
        and isinstance(row.source_kind, str)
        and row.source_kind in {"archived_question", "manual_review"}
    ):
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


@dataclass(frozen=True, slots=True)
class _SimulationAttempt:
    source_index: int
    seed: int
    attempted_at: datetime
    context: Mapping[str, object]
    selected_id: str | None


@dataclass(frozen=True, slots=True)
class _SimulationOutput:
    attempt: _SimulationAttempt
    row: CorpusLine


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


def _simulation_trigger_matches(
    trigger: object,
    context: Mapping[str, object],
    timestamp: datetime,
    elapsed_minutes: float,
    long_silence_minutes: int,
) -> bool:
    if not isinstance(trigger, str):
        return False
    if trigger == "any":
        return True
    if trigger == "app_start":
        return context.get("event") == "app_start"
    if trigger == "day_changed":
        return context.get("event") == "day_changed"
    if trigger in SIMULATION_DAYPARTS:
        return trigger == _expected_daypart(timestamp)
    if trigger == "weekday":
        return timestamp.isoweekday() < 6
    if trigger == "weekend":
        return timestamp.isoweekday() >= 6
    if trigger == "holiday":
        return isinstance(context.get("holiday"), str)
    if trigger == "anniversary":
        return _is_integer(context.get("anniversary_days")) and context["anniversary_days"] > 0
    if trigger == "long_silence":
        return elapsed_minutes >= long_silence_minutes
    if trigger == "ide_foreground":
        return context.get("ide_foreground") is True
    if trigger == "long_active":
        return _is_integer(context.get("active_minutes")) and context["active_minutes"] >= 90
    if trigger == "idle_return":
        return context.get("idle_return") is True
    # story_timer has no signal in the documented MVP context and must not be selected.
    return False


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
    if token == "time:dawn":
        return 4 <= timestamp.hour < 6
    if token.startswith("time:"):
        return token.removeprefix("time:") == _expected_daypart(timestamp)
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


def _simulation_issues(
    simulation: object | None,
    rows: Sequence[CorpusLine],
    scheduler_config: object,
    issues: _Issues,
    *,
    expected_corpus_sha256: str,
    expected_scheduler_config_sha256: str,
) -> None:
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
    if simulation.get("schema_version") != 1 or isinstance(
        simulation.get("schema_version"), bool
    ):
        issues.error("simulation_format", "simulation schema_version must be integer 1")
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
    for index, attempt in enumerate(attempts_value):
        if not isinstance(attempt, Mapping) or set(attempt) != SIMULATION_ATTEMPT_KEYS:
            issues.error(
                "simulation_format",
                f"simulation attempt {index} has unknown or missing keys",
            )
            continue
        seed = attempt.get("seed")
        timestamp = _parse_simulation_timestamp(attempt.get("attempted_at"))
        selected_id = attempt.get("selected_id")
        if (
            not _is_integer(seed)
            or (seeds_structurally_valid and seed not in seed_set)
            or timestamp is None
            or (selected_id is not None and (not isinstance(selected_id, str) or not selected_id))
            or (timestamp is not None and not _simulation_context_valid(attempt.get("context"), timestamp))
        ):
            issues.error(
                "simulation_format",
                f"simulation attempt {index} has invalid seed, timestamp, context or selected_id",
            )
            continue
        assert timestamp is not None and _is_integer(seed)
        key = (seed, timestamp)
        if key in seen_attempt_times:
            issues.error(
                "simulation_format",
                f"simulation seed {seed} repeats attempted_at {timestamp.isoformat()}",
            )
            continue
        seen_attempt_times.add(key)
        context = attempt.get("context")
        assert isinstance(context, Mapping)
        parsed_attempts.append(
            _SimulationAttempt(index, seed, timestamp, context, selected_id)
        )
        covered_dates[seed].add(timestamp.date())

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
    assert isinstance(minimum_interval, int)
    assert isinstance(max_per_hour, int)
    assert isinstance(late_night_max, int)
    assert isinstance(blocked_groups, list)
    assert isinstance(interrupt_intervals, Mapping)
    assert isinstance(long_silence, int)

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
                timestamp,
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
    if (
        not 0.10 <= technical_ratio <= 0.20
        or easter_ratio > 0.02
        or self_ambient_ratio < 0.65
        or user_direct_ratio > 0.15
    ):
        issues.error(
            "simulation_metric",
            "recomputed technical, EasterEgg or output-mode ratios violate acceptance bounds",
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
    seen: set[tuple[str, str]] = set()
    active: dict[tuple[str, str], str] = {}
    invalid_keys: set[tuple[str, str]] = set()
    for position, entry in enumerate(entries, start=1):
        if not isinstance(entry, Mapping) or set(entry) != ALLOWLIST_KEYS:
            issues.error(
                "allowlist_format",
                f"allowlist exception {position} must contain exactly rule_code, line_id, normalized_text_sha256 and reason",
            )
            continue
        rule_code = entry.get("rule_code")
        line_id = entry.get("line_id")
        digest = entry.get("normalized_text_sha256")
        reason = entry.get("reason")
        if (
            not isinstance(rule_code, str)
            or rule_code not in ALLOWLISTABLE_CODES
            or not isinstance(line_id, str)
            or not line_id
            or not isinstance(digest, str)
            or re.fullmatch(r"[0-9a-f]{64}", digest) is None
            or not isinstance(reason, str)
            or not reason.strip()
        ):
            issues.error("allowlist_format", f"allowlist exception {position} has invalid values")
            continue
        key = (rule_code, line_id)
        if key in seen:
            issues.error(
                "allowlist_duplicate",
                f"allowlist tuple ({rule_code!r}, {line_id!r}) occurs more than once",
                line_id,
            )
            invalid_keys.add(key)
            continue
        seen.add(key)
        matches = by_id.get(line_id, [])
        if len(matches) != 1:
            issues.error("allowlist_unknown_line", f"allowlist line_id {line_id!r} does not resolve uniquely", line_id)
            invalid_keys.add(key)
            continue
        actual = normalized_text_sha256(matches[0].text)
        if actual != digest:
            issues.error(
                "allowlist_hash_mismatch",
                f"allowlist normalized-text SHA-256 for {line_id!r} is stale or mismatched",
                line_id,
            )
            invalid_keys.add(key)
            continue
        active[key] = reason.strip()

    original_errors = list(issues.errors)
    retained: list[ValidationIssue] = []
    used: set[tuple[str, str]] = set()
    for issue in original_errors:
        key = (issue.code, issue.line_id)
        reason = active.get(key)
        if reason is not None and key not in invalid_keys:
            used.add(key)
            issues.warning(
                f"allowlisted_{issue.code}",
                f"{issue.message} Exception reason: {reason}",
                issue.line_id,
                issue.row_number,
            )
        else:
            retained.append(issue)
    issues.errors = retained
    for rule_code, line_id in sorted(set(active) - used - invalid_keys):
        issues.error(
            "allowlist_stale",
            f"allowlist entry for ({rule_code!r}, {line_id!r}) no longer matches that exact heuristic finding",
            line_id,
        )


def validate_corpus(
    lines: Sequence[CorpusLine],
    scheduler_config: object,
    allowlist: object,
    simulation_result: object | None = None,
    *,
    _corpus_sha256: str | None = None,
    _scheduler_config_sha256: str | None = None,
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
    if _corpus_sha256 is None:
        from .builder import serialize_v2

        try:
            payload = serialize_v2(typed_rows)
        except ValueError:
            payload = repr(typed_rows).encode("utf-8", errors="backslashreplace")
        _corpus_sha256 = hashlib.sha256(payload).hexdigest()
    if _scheduler_config_sha256 is None:
        try:
            _scheduler_config_sha256 = scheduler_config_sha256(scheduler_config)
        except (TypeError, ValueError):
            _scheduler_config_sha256 = hashlib.sha256(
                repr(scheduler_config).encode("utf-8", errors="backslashreplace")
            ).hexdigest()
    _simulation_issues(
        simulation_result,
        typed_rows,
        scheduler_config,
        issues,
        expected_corpus_sha256=_corpus_sha256,
        expected_scheduler_config_sha256=_scheduler_config_sha256,
    )
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
        _scheduler_config_sha256=scheduler_config_sha256(config),
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
    "scheduler_config_sha256",
    "validate_config",
    "validate_corpus",
    "validate_file",
]
