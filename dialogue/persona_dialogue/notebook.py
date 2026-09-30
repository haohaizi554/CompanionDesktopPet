"""本地就能答完的三件小事。

记住、报时、提醒都不调用模型。事实最多留八条，每条很短。
"""

from __future__ import annotations

import re
from datetime import datetime

from persona_dialogue.distill import PRIVACY_MARKERS

FACT_LIMIT = 8
FACT_CHARS = 40
REMINDER_TEXT_CHARS = 24

_CLOCK_TIME = re.compile(r"^(?:现在|这会儿|当前)?(?:是)?(?:几点了?|几点钟|什么时间|几时)$")
_CLOCK_WEEK = re.compile(r"^(?:今天|现在)?(?:是)?(?:星期几|周几|礼拜几)$")
_CLOCK_DATE = re.compile(r"^(?:今天|现在)?(?:是)?(?:几号|几月几号)$")
_REMIND = re.compile(
    r"^(?:过|再过)?(?P<num>半|一刻|\d{1,4}|[零一二两三四五六七八九十]{1,3})"
    r"(?:个)?(?P<unit>分钟|分|小时|钟头|刻钟)"
    r"(?:之后|以后|后)?(?:请)?(?:记得)?(?:提醒我|叫我|喊我)"
    r"(?P<what>.*)$"
)
_REMEMBER = re.compile(r"^(?:请)?(?:帮我)?记住[，,\s]*(?P<what>.+)$")
_REMEMBER_EMPTY = re.compile(r"^(?:请)?(?:帮我)?记住[。！？!?…]*$")
_FORGET = re.compile(
    r"^(?:请)?(?:帮我)?(?:把)?(?:这些|全部|所有|都)?(?:都)?"
    r"(?:忘掉|忘记|别记)[，,\s]*(?P<what>.*)$"
)
_RECALL = re.compile(r"^(?:你)?(?:还)?记得(?:些)?(?:什么|哪些|啥)(?:吗|么|呢)?$")
_ROLE = re.compile(
    r"^(?:可以|能不能|可不可以)?(?:你)?(?:来)?(?:做|当)我的?"
    r"(?P<role>女朋友|女友|老婆|伴侣|朋友)"
    r"(?:吧|好不好|好吗|可以吗|吗)?$"
)
_ROLE_IS = re.compile(r"^你(?:现在)?是我的?(?P<role>女朋友|女友|老婆|伴侣|朋友)(?:了|吧|啦)?$")
_ROLE_FACT = {
    "女朋友": "她是对方的女朋友",
    "女友": "她是对方的女朋友",
    "老婆": "她是对方的老婆",
    "伴侣": "她是对方的伴侣",
    "朋友": "她是对方的朋友",
}
_ROLE_REPLY = {
    "她是对方的女朋友": "好，我做你女朋友。",
    "她是对方的老婆": "好，我是你老婆。",
    "她是对方的伴侣": "好，我做你的伴侣。",
    "她是对方的朋友": "好，那我就做你朋友。",
}
_DIGITS = {"零": 0, "一": 1, "二": 2, "两": 2, "三": 3, "四": 4, "五": 5, "六": 6, "七": 7, "八": 8, "九": 9}
_WEEKDAYS = "一二三四五六日"


def local_turn(
    text: str,
    facts: list[str] | None,
    now: datetime | None = None,
    relationship: str = "",
) -> dict | None:
    """答完就返回 draft、facts、actions。普通闲聊返回 None。"""
    cleaned = _compact(text)
    kept = [fact for fact in sanitize_facts(facts) if not fact.startswith("她是对方的")]
    if not cleaned:
        return None
    moment = now or datetime.now()

    if _CLOCK_TIME.fullmatch(cleaned):
        return _done(speak_clock(moment), kept)
    if _CLOCK_WEEK.fullmatch(cleaned):
        return _done(f"今天星期{_WEEKDAYS[moment.weekday()]}。", kept)
    if _CLOCK_DATE.fullmatch(cleaned):
        return _done(f"今天{_zh(moment.month)}月{_zh(moment.day)}号。", kept)
    if _REMEMBER_EMPTY.fullmatch(cleaned):
        return _done("记什么，你说具体一点。", kept)
    role = _role(cleaned, kept, relationship)
    if role is not None:
        return role

    reminder = _reminder(cleaned)
    if reminder == "blocked":
        return _done("这个我就不提醒了。", kept)
    if isinstance(reminder, tuple):
        minutes, spoken = reminder
        return _done("好，到点我叫你。", kept, [{"skill": "remind", "minutes": minutes, "text": spoken}])

    if _RECALL.fullmatch(cleaned):
        return _done(_recall_line(kept), kept)

    forgotten = _FORGET.fullmatch(cleaned)
    if forgotten:
        what = _compact(forgotten.group("what")).strip("。！？!?…")
        if not what or what in {"了", "吧", "这些", "全部", "所有", "都"}:
            if not kept:
                if relationship:
                    return _done("关系还在。", [])
                return _done("本来也没记着什么。", [])
            if relationship:
                return _done("那些小事我不记了。", [])
            return _done("那我都不记了。", [])
        if _targets_relationship(what, relationship):
            return _done("这个关系我留着。", kept)
        remained = [fact for fact in kept if what not in fact]
        if len(remained) == len(kept):
            return _done("这件事我没记着。", kept)
        return _done("那件我不记了。", remained)

    remembered = _REMEMBER.fullmatch(cleaned)
    if remembered:
        fact = _fact(remembered.group("what"))
        if fact is None:
            return _done("这个我就不记了。", kept)
        if fact in kept:
            return _done("这个我记着了。", kept)
        updated = [*kept, fact][-FACT_LIMIT:]
        return _done("我记下了。", updated)
    return None


def sanitize_facts(facts: list[str] | None) -> list[str]:
    kept: list[str] = []
    for item in facts or []:
        if not isinstance(item, str):
            continue
        cleaned = _fact(item)
        if cleaned and cleaned not in kept:
            kept.append(cleaned)
        if len(kept) == FACT_LIMIT:
            break
    return kept


def speak_clock(moment: datetime) -> str:
    hour = moment.hour
    if hour < 5:
        period = "凌晨"
    elif hour < 11:
        period = "早上"
    elif hour < 13:
        period = "中午"
    elif hour < 18:
        period = "下午"
    else:
        period = "晚上"
    spoken_hour = hour % 12 or 12
    minute = moment.minute
    if minute == 0:
        tail = ""
    elif minute < 10:
        tail = "零" + _zh(minute)
    else:
        tail = _zh(minute)
    return f"现在是{period}{_zh(spoken_hour)}点{tail}。"


def _role(text: str, facts: list[str], relationship: str) -> dict | None:
    matched = _ROLE.fullmatch(text) or _ROLE_IS.fullmatch(text)
    if not matched:
        return None
    fact = _ROLE_FACT[matched.group("role")]
    return {
        "draft": _ROLE_REPLY[fact],
        "facts": facts,
        "actions": [],
        "relationship": fact,
    }


def _targets_relationship(what: str, relationship: str) -> bool:
    if relationship == "她是对方的女朋友" and what in {"女朋友", "女友"}:
        return True
    if relationship == "她是对方的老婆" and what == "老婆":
        return True
    if relationship == "她是对方的伴侣" and what == "伴侣":
        return True
    return relationship == "她是对方的朋友" and what == "朋友"


def _reminder(text: str) -> tuple[int, str] | str | None:
    matched = _REMIND.fullmatch(text)
    if not matched:
        return None
    what = _compact(matched.group("what"))
    what = re.sub(r"^(?:一下|去|要|记得)", "", what).strip(" ，,。！？!?…")
    if any(marker in what for marker in PRIVACY_MARKERS):
        return "blocked"
    minutes = _minutes(matched.group("num"), matched.group("unit"))
    if minutes is None:
        return None
    if len(what) > REMINDER_TEXT_CHARS:
        what = what[:REMINDER_TEXT_CHARS].rstrip()
    spoken = "到点了。" if not what else f"到点了，{what}。"
    return minutes, spoken


def _minutes(num: str, unit: str) -> int | None:
    if num == "半" and unit in {"小时", "钟头"}:
        return 30
    if num == "一刻" and unit == "刻钟":
        return 15
    if unit == "刻钟":
        count = _zh_int(num)
        if count is None or count < 1 or count > 4:
            return None
        return count * 15
    count = _zh_int(num)
    if count is None or count < 1:
        return None
    if unit in {"小时", "钟头"}:
        count *= 60
    if count > 24 * 60:
        return None
    return count


def _zh_int(text: str) -> int | None:
    if text.isdigit():
        return int(text)
    if text == "十":
        return 10
    if len(text) == 2 and text[0] == "十" and text[1] in _DIGITS:
        return 10 + _DIGITS[text[1]]
    if len(text) == 2 and text[0] in _DIGITS and text[1] == "十":
        return _DIGITS[text[0]] * 10
    if len(text) == 3 and text[0] in _DIGITS and text[1] == "十" and text[2] in _DIGITS:
        return _DIGITS[text[0]] * 10 + _DIGITS[text[2]]
    if len(text) == 1 and text in _DIGITS and text != "零":
        return _DIGITS[text]
    return None


def _fact(text: str) -> str | None:
    cleaned = _compact(text).strip(" ，,。！？!?…")
    if not cleaned or any(marker in cleaned for marker in PRIVACY_MARKERS):
        return None
    return cleaned[:FACT_CHARS].rstrip()


def _recall_line(facts: list[str]) -> str:
    if not facts:
        return "还没记下什么。"
    if len(facts) == 1:
        return f"我记着{facts[0]}。"
    return f"我记着{facts[-1]}，还有{facts[-2]}。"


def _done(draft: str, facts: list[str], actions: list[dict] | None = None) -> dict:
    return {"draft": draft, "facts": facts, "actions": actions or []}


def _compact(text: str) -> str:
    return " ".join((text or "").split()).strip()


def _zh(number: int) -> str:
    if number <= 10:
        return "零一二三四五六七八九十"[number]
    tens, ones = divmod(number, 10)
    head = "十" if tens == 1 else "零一二三四五六七八九"[tens] + "十"
    return head if ones == 0 else head + "零一二三四五六七八九"[ones]
