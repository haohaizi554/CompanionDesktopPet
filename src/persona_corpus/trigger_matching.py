from __future__ import annotations

import math
from collections.abc import Mapping


TRIGGER_EPSILON = 1e-9
_DAYPART_TRIGGERS = frozenset(
    {"morning", "noon", "afternoon", "evening", "late_night"}
)


def _context_value(context: object, name: str) -> object:
    if isinstance(context, Mapping):
        return context.get(name)
    return getattr(context, name, None)


def _finite_number(value: object) -> bool:
    return (
        not isinstance(value, bool)
        and isinstance(value, (int, float))
        and math.isfinite(float(value))
    )


def trigger_matches(
    trigger: object,
    context: object,
    elapsed_minutes: object,
    long_silence_minutes: object,
) -> bool:
    """Return whether one validated runtime context satisfies a trigger."""

    if not isinstance(trigger, str):
        return False
    if trigger == "any":
        return True
    if trigger == "app_start":
        return _context_value(context, "event") == "app_start"
    if trigger == "day_changed":
        return _context_value(context, "event") == "day_changed"
    if trigger in _DAYPART_TRIGGERS:
        return _context_value(context, "daypart") == trigger
    if trigger == "weekday":
        return _context_value(context, "is_weekend") is False
    if trigger == "weekend":
        return _context_value(context, "is_weekend") is True
    if trigger == "holiday":
        holiday = _context_value(context, "holiday")
        return isinstance(holiday, str) and bool(holiday.strip())
    if trigger == "anniversary":
        anniversary_days = _context_value(context, "anniversary_days")
        return type(anniversary_days) is int and anniversary_days > 0
    if trigger == "long_silence":
        return (
            _finite_number(elapsed_minutes)
            and _finite_number(long_silence_minutes)
            and float(elapsed_minutes) + TRIGGER_EPSILON
            >= float(long_silence_minutes)
        )
    if trigger == "ide_foreground":
        return _context_value(context, "ide_foreground") is True
    if trigger == "long_active":
        active_minutes = _context_value(context, "active_minutes")
        return type(active_minutes) is int and active_minutes >= 90
    if trigger == "idle_return":
        return _context_value(context, "idle_return") is True
    # story_timer has no documented MVP signal and therefore remains unreachable.
    return False


def time_context_token_for_hour(hour: object) -> str | None:
    """Return the one canonical, non-overlapping controlled time token."""

    if type(hour) is not int or not 0 <= hour < 24:
        return None
    if hour < 4 or hour >= 23:
        return "time:late_night"
    if hour < 6:
        return "time:dawn"
    if hour < 11:
        return "time:morning"
    if hour < 14:
        return "time:noon"
    if hour < 18:
        return "time:afternoon"
    return "time:evening"


def time_context_token_matches(token: object, hour: object) -> bool:
    return isinstance(token, str) and token == time_context_token_for_hour(hour)


__all__ = (
    "TRIGGER_EPSILON",
    "time_context_token_for_hour",
    "time_context_token_matches",
    "trigger_matches",
)
