"""Shared PII classification and named stage policies.

Classification is intentionally independent from enforcement.  Every consumer
sees the same direct-identifier findings, while legacy review/audit stages opt
into broad keyword signals that would be too noisy for published content.
"""

from __future__ import annotations

import re
from dataclasses import dataclass

from .contract import PERSONA_CONTRACT


# Compatibility export for existing consumers. The canonical, immutable source
# is the shared contract; privacy classification must not maintain a copy.
PII_MARKERS = PERSONA_CONTRACT.pii_markers
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
_COMMON_GIVEN_NAME_PATTERN = "|".join(
    sorted(map(re.escape, COMMON_CHINESE_GIVEN_NAMES), key=len, reverse=True)
)
LABELED_CHINESE_NAME_PATTERN = re.compile(
    rf"(?:(?:姓名|名字|真名)\s*[:：]\s*"
    rf"[{COMMON_CHINESE_SURNAMES}][\u3400-\u9fff]{{1,2}}|"
    rf"(?:叫作|叫|我是)\s*[{COMMON_CHINESE_SURNAMES}]"
    rf"(?:{_COMMON_GIVEN_NAME_PATTERN}))"
)

_LOCATION = r"(?:湖南|长沙|广东)"
_PERSONAL_LOCATION_PATTERNS = (
    re.compile(
        rf"(?:我的老家|老家|来自|住在|漂在|生活在|我(?:住|来自|漂|生活|长大)).{{0,10}}{_LOCATION}"
    ),
    re.compile(rf"{_LOCATION}.{{0,10}}(?:是我老家|长大|漂泊|打工)"),
    re.compile(rf"(?:地址|住址)\s*[:：是为]\s*{_LOCATION}.{{0,24}}"),
)
_INCOME = r"(?:工资|收入|月薪)"
_PERSONAL_INCOME_PATTERNS = (
    re.compile(rf"(?:我|我的).{{0,10}}{_INCOME}"),
    re.compile(
        rf"{_INCOME}(?:(?:是|有|约|大概|只有|才))?\s*"
        rf"[\d零一二三四五六七八九十百千万两]+(?:元|块|千|万|[kK])"
    ),
)
_PERSONAL_WORK_PATTERN = re.compile(
    r"(?:我|我的).{0,12}打零工|打零工.{0,12}(?:我|那几年|经历)"
)
_LEGACY_NAME_KEYWORDS = ("姓名", "名字")
_LEGACY_LOCATION_KEYWORDS = ("湖南", "长沙", "广东", "住在", "地址")
_LEGACY_INCOME_WORK_KEYWORDS = (
    "工资",
    "收入",
    "月薪",
    "打零工",
    "换工作",
)


@dataclass(frozen=True, slots=True)
class PiiFinding:
    kind: str
    evidence: str


@dataclass(frozen=True, slots=True)
class PiiPolicy:
    name: str
    included_kinds: frozenset[str]
    excluded_evidence: frozenset[str] = frozenset()


_DIRECT_KINDS = frozenset(
    {
        "phone_number",
        "national_id",
        "email_address",
        "known_identity",
        "person_name",
        "personal_location",
        "personal_income",
        "personal_employment",
    }
)
_LEGACY_SIGNAL_KINDS = frozenset(
    {
        "name_keyword",
        "location_keyword",
        "income_or_employment_keyword",
    }
)

LEGACY_REVIEW_POLICY = PiiPolicy(
    name="legacy_review",
    included_kinds=_DIRECT_KINDS | _LEGACY_SIGNAL_KINDS,
    # This nickname is an intentional, tracked legacy surface convention.
    # Classification remains shared; only legacy disposition is exempted.
    excluded_evidence=frozenset({"玥玥"}),
)
LEGACY_AUDIT_POLICY = PiiPolicy(
    name="legacy_audit",
    included_kinds=_DIRECT_KINDS | _LEGACY_SIGNAL_KINDS,
)
ENABLED_CONTENT_POLICY = PiiPolicy(
    name="enabled_content",
    included_kinds=_DIRECT_KINDS,
)


def classify_pii(text: str) -> tuple[PiiFinding, ...]:
    """Return deduplicated privacy findings without applying a stage policy."""
    findings: list[PiiFinding] = []
    seen: set[tuple[str, str]] = set()

    def add(kind: str, evidence: str) -> None:
        key = (kind, evidence)
        if key not in seen:
            findings.append(PiiFinding(kind=kind, evidence=evidence))
            seen.add(key)

    direct_kinds = ("phone_number", "national_id", "email_address")
    for kind, pattern in zip(direct_kinds, PII_PATTERNS, strict=True):
        match = pattern.search(text)
        if match is not None:
            add(kind, match.group(0))

    for marker in PII_MARKERS:
        if marker in text:
            add("known_identity", marker)

    name_match = CONTEXTUAL_CHINESE_NAME_PATTERN.search(text)
    if name_match is None:
        name_match = LABELED_CHINESE_NAME_PATTERN.search(text)
    if name_match is not None:
        add("person_name", name_match.group(0))

    for pattern in _PERSONAL_LOCATION_PATTERNS:
        match = pattern.search(text)
        if match is not None:
            add("personal_location", match.group(0))
            break
    for pattern in _PERSONAL_INCOME_PATTERNS:
        match = pattern.search(text)
        if match is not None:
            add("personal_income", match.group(0))
            break
    work_match = _PERSONAL_WORK_PATTERN.search(text)
    if work_match is not None:
        add("personal_employment", work_match.group(0))

    if not any(kind == "person_name" for kind, _ in seen):
        for marker in _LEGACY_NAME_KEYWORDS:
            if marker in text:
                add("name_keyword", marker)
                break
    if not any(kind == "personal_location" for kind, _ in seen):
        for marker in _LEGACY_LOCATION_KEYWORDS:
            if marker in text:
                add("location_keyword", marker)
                break
    if not any(
        kind in {"personal_income", "personal_employment"} for kind, _ in seen
    ):
        for marker in _LEGACY_INCOME_WORK_KEYWORDS:
            if marker in text:
                add("income_or_employment_keyword", marker)
                break
    return tuple(findings)


def pii_findings(text: str, policy: PiiPolicy) -> tuple[PiiFinding, ...]:
    """Apply a named stage policy to shared classifier output."""
    return tuple(
        finding
        for finding in classify_pii(text)
        if finding.kind in policy.included_kinds
        and finding.evidence not in policy.excluded_evidence
    )


def pii_kinds(text: str, policy: PiiPolicy) -> frozenset[str]:
    return frozenset(finding.kind for finding in pii_findings(text, policy))


def contains_pii(text: str, policy: PiiPolicy) -> bool:
    return bool(pii_findings(text, policy))


__all__ = (
    "COMMON_CHINESE_GIVEN_NAMES",
    "COMMON_CHINESE_SURNAMES",
    "CONTEXTUAL_CHINESE_NAME_PATTERN",
    "ENABLED_CONTENT_POLICY",
    "LABELED_CHINESE_NAME_PATTERN",
    "LEGACY_AUDIT_POLICY",
    "LEGACY_REVIEW_POLICY",
    "NAME_CONTEXT_MARKERS",
    "PII_MARKERS",
    "PII_PATTERNS",
    "PiiFinding",
    "PiiPolicy",
    "classify_pii",
    "contains_pii",
    "pii_findings",
    "pii_kinds",
)
