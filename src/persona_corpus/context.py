from __future__ import annotations

import calendar
import math
from dataclasses import dataclass
from datetime import datetime


EVENTS = frozenset({"tick", "app_start", "day_changed"})
DAYPARTS = frozenset({"morning", "noon", "afternoon", "evening", "late_night"})


class ContextError(ValueError):
    """A context value cannot be proved from the supplied timestamp/signals."""


def _aware(value: datetime) -> bool:
    return isinstance(value, datetime) and value.tzinfo is not None and value.utcoffset() is not None


def daypart_for(now: datetime) -> str:
    if not _aware(now):
        raise ContextError("now must be a timezone-aware datetime")
    if 6 <= now.hour < 11:
        return "morning"
    if 11 <= now.hour < 14:
        return "noon"
    if 14 <= now.hour < 18:
        return "afternoon"
    if 18 <= now.hour < 23:
        return "evening"
    return "late_night"


def _optional_boolean(name: str, value: object) -> None:
    if value is not None and not isinstance(value, bool):
        raise ContextError(f"{name} must be bool or None")


@dataclass(frozen=True, slots=True)
class PersonaContext:
    event: str
    daypart: str
    weekday: int
    is_weekend: bool
    holiday: str | None
    anniversary_days: int
    minutes_since_last_output: float
    ide_foreground: bool | None = None
    active_minutes: int | None = None
    idle_return: bool | None = None
    fullscreen: bool | None = None

    def __post_init__(self) -> None:
        if not isinstance(self.event, str) or self.event not in EVENTS:
            raise ContextError(f"event must be one of {sorted(EVENTS)!r}")
        if not isinstance(self.daypart, str) or self.daypart not in DAYPARTS:
            raise ContextError(f"daypart must be one of {sorted(DAYPARTS)!r}")
        if isinstance(self.weekday, bool) or not isinstance(self.weekday, int) or not 1 <= self.weekday <= 7:
            raise ContextError("weekday must be an integer in [1, 7]")
        if not isinstance(self.is_weekend, bool):
            raise ContextError("is_weekend must be bool")
        if self.holiday is not None and (
            not isinstance(self.holiday, str)
            or not self.holiday.strip()
            or self.holiday != self.holiday.strip()
        ):
            raise ContextError("holiday must be a non-blank trimmed string or None")
        if (
            isinstance(self.anniversary_days, bool)
            or not isinstance(self.anniversary_days, int)
            or self.anniversary_days < 0
        ):
            raise ContextError("anniversary_days must be a non-negative integer")
        if (
            isinstance(self.minutes_since_last_output, bool)
            or not isinstance(self.minutes_since_last_output, (int, float))
            or not math.isfinite(float(self.minutes_since_last_output))
            or float(self.minutes_since_last_output) < 0
        ):
            raise ContextError("minutes_since_last_output must be a finite non-negative number")
        _optional_boolean("ide_foreground", self.ide_foreground)
        _optional_boolean("idle_return", self.idle_return)
        _optional_boolean("fullscreen", self.fullscreen)
        if self.active_minutes is not None and (
            isinstance(self.active_minutes, bool)
            or not isinstance(self.active_minutes, int)
            or self.active_minutes < 0
        ):
            raise ContextError("active_minutes must be a non-negative integer or None")

    @classmethod
    def from_datetime(
        cls,
        now: datetime,
        *,
        event: str = "tick",
        holiday: str | None = None,
        anniversary_days: int = 0,
        minutes_since_last_output: float = 60,
        ide_foreground: bool | None = None,
        active_minutes: int | None = None,
        idle_return: bool | None = None,
        fullscreen: bool | None = None,
    ) -> PersonaContext:
        if not _aware(now):
            raise ContextError("now must be a timezone-aware datetime")
        return cls(
            event=event,
            daypart=daypart_for(now),
            weekday=now.isoweekday(),
            is_weekend=now.isoweekday() >= 6,
            holiday=holiday,
            anniversary_days=anniversary_days,
            minutes_since_last_output=minutes_since_last_output,
            ide_foreground=ide_foreground,
            active_minutes=active_minutes,
            idle_return=idle_return,
            fullscreen=fullscreen,
        )

    def validate_for(self, now: datetime) -> None:
        if not _aware(now):
            raise ContextError("now must be a timezone-aware datetime")
        if self.daypart != daypart_for(now):
            raise ContextError("daypart does not match now")
        if self.weekday != now.isoweekday():
            raise ContextError("weekday does not match now")
        if self.is_weekend != (now.isoweekday() >= 6):
            raise ContextError("is_weekend does not match now")

    def controlled_tokens(self, now: datetime) -> set[str]:
        self.validate_for(now)
        tokens = {
            "day:weekend" if self.is_weekend else "day:weekday",
            f"time:{self.daypart}",
        }
        if 4 <= now.hour < 6:
            tokens.add("time:dawn")
        season = (
            "spring"
            if now.month in {3, 4, 5}
            else "summer"
            if now.month in {6, 7, 8}
            else "autumn"
            if now.month in {9, 10, 11}
            else "winter"
        )
        tokens.add(f"season:{season}")
        if self.event == "app_start":
            tokens.add("app_started")
        if self.holiday is not None:
            tokens.update({"holiday", "date:holiday"})
        if self.anniversary_days > 0:
            tokens.add("anniversary")
        if self.ide_foreground is True:
            tokens.add("ide_foreground")
        if self.active_minutes is not None and self.active_minutes >= 90:
            tokens.add("active_90m")
        if self.idle_return is True:
            tokens.add("idle_return")
        if self.fullscreen is False:
            tokens.add("not_fullscreen")
        if now.day in {1, calendar.monthrange(now.year, now.month)[1]}:
            tokens.add("date:month_boundary")
        return tokens
