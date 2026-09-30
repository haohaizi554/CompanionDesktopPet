"""对话里能改的设置。

面板上的间隔、气泡、语速、语气、重复从这里改。
采样个数和采样范围没有放进面板，只有对方点名才动。
大小、置顶、动画和语音开关在菜单里，对话里同样能改。
"""

from __future__ import annotations

import json
from typing import Optional

from langchain_core.tools import tool

DEFAULT_SETTINGS = {
    "day_min": 5,
    "day_max": 15,
    "evening_min": 10,
    "evening_max": 20,
    "late_min": 30,
    "late_max": 60,
    "fullscreen_min": 60,
    "fullscreen_max": 120,
    "bubble_seconds": 5,
    "speech_speed": 1.0,
    "speech_temperature": 1.0,
    "speech_repetition": 1.35,
    "top_k": 15,
    "top_p": 1.0,
    "reply_max_chars": 80,
    "scale": "Normal",
    "always_on_top": True,
    "animation_paused": False,
    "voice_enabled": True,
}

_INTERVALS = {
    "day": ("白天", "day_min", "day_max"),
    "evening": ("傍晚", "evening_min", "evening_max"),
    "late": ("深夜", "late_min", "late_max"),
    "fullscreen": ("全屏", "fullscreen_min", "fullscreen_max"),
}

_SCALES = {
    "small": "Small",
    "normal": "Normal",
    "large": "Large",
    "小巧": "Small",
    "刚刚好": "Normal",
    "大一点": "Large",
}


@tool
def set_interval(period: str, minimum_minutes: int, maximum_minutes: int) -> str:
    """改她隔多久自己开口。period 只能是 day、evening、late、fullscreen。最短和最长分钟都要给，0 到 1440，最短不能大于最长。"""
    return ""


@tool
def set_bubble(seconds: int) -> str:
    """改气泡停在屏幕上的秒数，1 到 120。"""
    return ""


@tool
def set_speech(
    speed: Optional[float] = None,
    temperature: Optional[float] = None,
    repetition: Optional[float] = None,
    top_k: Optional[int] = None,
    top_p: Optional[float] = None,
) -> str:
    """改说话的声音。语速 0.5 到 2，语气 0.2 到 1.5，重复 1 到 2。top_k 是采样个数 1 到 50，top_p 是采样范围 0.1 到 1，这两项面板上没有，只有对方明确说采样、top_k 或 top_p 才改。没给出的项保持原样。"""
    return ""


@tool
def set_companion(
    scale: Optional[str] = None,
    always_on_top: Optional[bool] = None,
    animation_paused: Optional[bool] = None,
    voice_enabled: Optional[bool] = None,
) -> str:
    """改菜单里的状态。scale 用 Small、Normal 或 Large。always_on_top 是置顶，animation_paused 是暂停动画，voice_enabled 是语音开关。没给出的项保持原样。"""
    return ""


SKILL_TOOLS = [set_interval, set_bubble, set_speech, set_companion]


_SETTING_MARKERS = (
    "语速",
    "语气",
    "气泡",
    "采样",
    "置顶",
    "暂停动画",
    "继续动画",
    "语音开关",
    "关掉语音",
    "打开语音",
    "间隔",
    "top_k",
    "top_p",
)


def confirm_changes(notes: list[str]) -> str:
    """One short spoken line. The model already spent its call on the tool."""
    text = " ".join(notes)
    if "气泡" in text and not any(word in text for word in ("语速", "语气", "重复", "白天", "傍晚", "深夜", "全屏")):
        return "好，气泡我改好了。"
    if any(word in text for word in ("语速", "语气", "重复")):
        return "好，说话的声音我改了。"
    if any(word in text for word in ("白天", "傍晚", "深夜", "全屏")):
        return "好，开口的间隔我改了。"
    return "好，我按你说的改了。"


def wants_setting(text: str) -> bool:
    value = (text or "").strip().lower()
    if any(marker in value for marker in _SETTING_MARKERS):
        return True
    if any(phrase in value for phrase in ("慢一点", "快一点", "慢点", "快点")) and any(
        word in value for word in ("说", "读", "声音", "语速")
    ):
        return True
    return False


def setting_prompt(settings: dict) -> str:
    current = normalize_settings(settings)
    return "\n".join(
        [
            "你正在帮佳怡改设置。必须调用一个技能，不要写旁白。",
            "用户给出的数字原样填进参数，没提到的参数不要填。",
            f"现在语速 {current['speech_speed']}，语气 {current['speech_temperature']}，重复 {current['speech_repetition']}。",
            f"气泡 {current['bubble_seconds']} 秒，采样个数 {current['top_k']}，采样范围 {current['top_p']}。",
            "语速填 speed，语气填 temperature，重复填 repetition，采样个数填 top_k，采样范围填 top_p。",
            "间隔用 set_interval，period 只能是 day、evening、late、fullscreen。气泡用 set_bubble。",
            "大小、置顶、动画暂停、语音开关用 set_companion。",
        ]
    )


def normalize_settings(raw: dict | None) -> dict:
    current = dict(DEFAULT_SETTINGS)
    if not isinstance(raw, dict):
        return current
    for key, default in DEFAULT_SETTINGS.items():
        if key not in raw or raw[key] is None:
            continue
        current[key] = raw[key]
    return current


def reply_limit(settings: dict | None) -> int:
    current = normalize_settings(settings)
    try:
        value = int(current.get("reply_max_chars") or 80)
    except (TypeError, ValueError):
        return 80
    return min(200, max(50, value))


def output_tokens(max_chars: int, floor: int = 180) -> int:
    """Chinese characters often cost one or two tokens each."""
    try:
        chars = int(max_chars)
    except (TypeError, ValueError):
        chars = 80
    try:
        base = int(floor)
    except (TypeError, ValueError):
        base = 180
    if base <= 0:
        base = 180
    return min(2048, max(base, max(1, chars) * 2))


def skill_hint(settings: dict) -> str:
    current = normalize_settings(settings)
    return "\n".join(
        [
            "对方如果是在改她自己的设置，调用对应技能，可以只改提到的那几项。",
            "闲聊、问好、安慰都不要调用技能。",
            "慢一点、久一点这类话，在现在的数值上略改，不要一下打到边界，除非对方说最慢、最快、最久。",
            "top_k 和 top_p 没有出现在面板上。对方没提采样或这两个名字时，不要改它们。",
            "技能执行完，只用她平时的口气回一两句。不要说出技能名字，不要解释参数表，不要列清单。",
            "现在的设置：",
            f"- 白天自己开口 {current['day_min']} 到 {current['day_max']} 分钟，傍晚 {current['evening_min']} 到 {current['evening_max']} 分钟。",
            f"- 深夜 {current['late_min']} 到 {current['late_max']} 分钟，全屏 {current['fullscreen_min']} 到 {current['fullscreen_max']} 分钟。",
            f"- 气泡停 {current['bubble_seconds']} 秒。",
            f"- 语速 {current['speech_speed']}，语气 {current['speech_temperature']}，重复 {current['speech_repetition']}。",
            f"- 采样个数 {current['top_k']}，采样范围 {current['top_p']}。",
            f"- 大小 {current['scale']}，置顶 {'开' if current['always_on_top'] else '关'}，动画{'暂停' if current['animation_paused'] else '在动'}，语音{'开' if current['voice_enabled'] else '关'}。",
        ]
    )


def apply_tool_calls(settings: dict, calls: list) -> tuple[dict, list[dict], list[str]]:
    current = normalize_settings(settings)
    actions: list[dict] = []
    notes: list[str] = []
    for call in calls or []:
        name = _call_name(call)
        args = _args(call)
        if name == "set_interval":
            action, note = _interval(current, args)
        elif name == "set_bubble":
            action, note = _bubble(current, args)
        elif name == "set_speech":
            action, note = _speech(current, args)
        elif name == "set_companion":
            action, note = _companion(current, args)
        else:
            action, note = None, "没有这项设置。"
        notes.append(note)
        if action is not None:
            actions.append(action)
    return current, actions, notes


def _call_name(call) -> str:
    if isinstance(call, dict):
        return str(call.get("name") or "")
    return str(getattr(call, "name", "") or "")


def _args(call) -> dict:
    raw = call.get("args") if isinstance(call, dict) else getattr(call, "args", None)
    if isinstance(raw, str):
        try:
            raw = json.loads(raw)
        except json.JSONDecodeError:
            return {}
    return raw if isinstance(raw, dict) else {}


def _interval(current: dict, args: dict) -> tuple[dict | None, str]:
    period = str(args.get("period") or "").strip().lower()
    if period not in _INTERVALS:
        return None, "间隔只能改白天、傍晚、深夜或全屏。"
    label, low_key, high_key = _INTERVALS[period]
    low = _int(args.get("minimum_minutes"), 0, 24 * 60)
    high = _int(args.get("maximum_minutes"), 0, 24 * 60)
    if low is None or high is None:
        return None, f"{label}的最短和最长都要给，而且要在 0 到 1440 分钟里。"
    if low > high:
        return None, f"{label}的最短间隔不能大于最长间隔。"
    current[low_key] = low
    current[high_key] = high
    return (
        {"skill": "set_interval", "period": period, "minimum_minutes": low, "maximum_minutes": high},
        f"{label}改成 {low} 到 {high} 分钟。",
    )


def _bubble(current: dict, args: dict) -> tuple[dict | None, str]:
    seconds = _int(args.get("seconds"), 1, 120)
    if seconds is None:
        return None, "气泡要在 1 到 120 秒之间。"
    current["bubble_seconds"] = seconds
    return {"skill": "set_bubble", "seconds": seconds}, f"气泡改成 {seconds} 秒。"


def _speech(current: dict, args: dict) -> tuple[dict | None, str]:
    action: dict = {"skill": "set_speech"}
    notes: list[str] = []
    speed = _optional_float(args, "speed", 0.5, 2)
    temperature = _optional_float(args, "temperature", 0.2, 1.5)
    repetition = _optional_float(args, "repetition", 1, 2)
    top_k = _optional_int(args, "top_k", 1, 50)
    top_p = _optional_float(args, "top_p", 0.1, 1)
    checked = (speed, temperature, repetition, top_k, top_p)
    if any(item[0] == "bad" for item in checked):
        return None, "语音有一项超出范围，这项没改。"
    if speed[0] == "set":
        current["speech_speed"] = speed[1]
        action["speed"] = speed[1]
        notes.append(f"语速 {speed[1]}")
    if temperature[0] == "set":
        current["speech_temperature"] = temperature[1]
        action["temperature"] = temperature[1]
        notes.append(f"语气 {temperature[1]}")
    if repetition[0] == "set":
        current["speech_repetition"] = repetition[1]
        action["repetition"] = repetition[1]
        notes.append(f"重复 {repetition[1]}")
    if top_k[0] == "set":
        current["top_k"] = top_k[1]
        action["top_k"] = top_k[1]
        notes.append(f"采样个数 {top_k[1]}")
    if top_p[0] == "set":
        current["top_p"] = top_p[1]
        action["top_p"] = top_p[1]
        notes.append(f"采样范围 {top_p[1]}")
    if len(action) == 1:
        return None, "没有要改的语音项。"
    return action, "语音改了：" + "，".join(notes) + "。"


def _companion(current: dict, args: dict) -> tuple[dict | None, str]:
    action: dict = {"skill": "set_companion"}
    notes: list[str] = []
    if "scale" in args and args.get("scale") is not None:
        scale = _SCALES.get(str(args.get("scale")).strip())
        if scale is None:
            return None, "大小只能是小巧、刚刚好或大一点。"
        current["scale"] = scale
        action["scale"] = scale
        notes.append(f"大小 {scale}")
    if "always_on_top" in args and args.get("always_on_top") is not None:
        flag = args.get("always_on_top")
        if not isinstance(flag, bool):
            return None, "置顶要是是否。"
        current["always_on_top"] = flag
        action["always_on_top"] = flag
        notes.append("置顶开" if flag else "置顶关")
    if "animation_paused" in args and args.get("animation_paused") is not None:
        flag = args.get("animation_paused")
        if not isinstance(flag, bool):
            return None, "动画暂停要是是否。"
        current["animation_paused"] = flag
        action["animation_paused"] = flag
        notes.append("动画暂停" if flag else "动画继续")
    if "voice_enabled" in args and args.get("voice_enabled") is not None:
        flag = args.get("voice_enabled")
        if not isinstance(flag, bool):
            return None, "语音开关要是是否。"
        current["voice_enabled"] = flag
        action["voice_enabled"] = flag
        notes.append("语音开" if flag else "语音关")
    if len(action) == 1:
        return None, "没有要改的状态。"
    return action, "状态改了：" + "，".join(notes) + "。"


def _int(value, low: int, high: int) -> int | None:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return None
    number = int(value)
    if number != value or number < low or number > high:
        return None
    return number


def _optional_int(args: dict, key: str, low: int, high: int) -> tuple[str, int]:
    if key not in args or args.get(key) is None:
        return "skip", 0
    number = _int(args.get(key), low, high)
    if number is None:
        return "bad", 0
    return "set", number


def _optional_float(args: dict, key: str, low: float, high: float) -> tuple[str, float]:
    if key not in args or args.get(key) is None:
        return "skip", 0
    value = args.get(key)
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        return "bad", 0
    number = float(value)
    if number < low or number > high:
        return "bad", 0
    return "set", number
