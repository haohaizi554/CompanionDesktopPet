"""Per-row content, context, and safety validation rules."""

from __future__ import annotations

import re
from typing import Mapping

from ..contract import (
    ALLOWED_CONTEXT_TOKENS,
    CATEGORY_GROUPS,
    OUTPUT_MODES,
    PERSONA_CONTRACT,
    SOURCE_KINDS,
    TONES,
    TRIGGERS,
)
from ..editorial import is_exact_identity_easter_egg
from ..models import CorpusLine
from ..normalization import normalize_text
from ..surface_safety import (
    TECHNICAL_DEICTIC_OBJECT_MARKERS,
    TECHNICAL_USER_ENVIRONMENT_MARKERS,
)
from .config_rules import CONTEXT_TOKEN_PATTERN
from .core import _Issues, _is_finite_number, _is_integer
from .safety_rules import validate_safety_preflight


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
) + TECHNICAL_DEICTIC_OBJECT_MARKERS + TECHNICAL_USER_ENVIRONMENT_MARKERS
PII_MARKERS = (
    "雷琳玥",
    "小玥",
    "玥玥",
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


def _has_identity_marker(text: str) -> bool:
    return any(marker in text for marker in PII_MARKERS)


def _looks_like_non_identity_pii(text: str) -> bool:
    if any(pattern.search(text) for pattern in PII_PATTERNS) or (
        CONTEXTUAL_CHINESE_NAME_PATTERN.search(text)
        or LABELED_CHINESE_NAME_PATTERN.search(text)
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
    trigger_by_time_token = PERSONA_CONTRACT.temporal["context_token_trigger"]
    if not isinstance(trigger_by_time_token, Mapping):
        raise RuntimeError("persona temporal contract is malformed")
    allowed_time_values = frozenset(
        token.split(":", 1)[1]
        for token, mapped_trigger in trigger_by_time_token.items()
        if isinstance(token, str)
        and token.startswith("time:")
        and mapped_trigger == trigger
    )
    expected = {
        "morning": ("time", allowed_time_values),
        "noon": ("time", allowed_time_values),
        "afternoon": ("time", allowed_time_values),
        "evening": ("time", allowed_time_values),
        "late_night": ("time", allowed_time_values),
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
    validate_safety_preflight(
        row,
        row_number,
        issues,
        context_tokens=tokens,
        has_identity_marker=_has_identity_marker,
        looks_like_non_identity_pii=_looks_like_non_identity_pii,
        identity_pii_is_adjudicated=is_exact_identity_easter_egg,
        direct_state_patterns=DIRECT_STATE_PATTERNS,
        technical_current_patterns=TECHNICAL_CURRENT_PATTERNS,
        unsafe_emotional_markers=STRONG_EMOTION_MARKERS,
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
