"""Coverage validation for already parsed simulation attempts."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
from typing import Iterable, Mapping, Protocol

from ..trigger_matching import time_context_token_for_hour
from .common import IssueSink


_SEASONS = ("spring", "summer", "autumn", "winter")
_SIGNAL_BOUNDARIES: tuple[tuple[str, tuple[object, ...]], ...] = (
    ("ide_foreground", (None, False, True)),
    ("active_minutes", (None, 89, 90, 91)),
    ("idle_return", (None, False, True)),
    ("fullscreen", (None, False, True)),
)


@dataclass(frozen=True, slots=True)
class SimulationCoverage:
    seasons: tuple[str, ...]
    dawn: bool
    ide_foreground_values: tuple[bool | None, ...]
    active_minutes_values: tuple[int | None, ...]
    idle_return_values: tuple[bool | None, ...]
    fullscreen_values: tuple[bool | None, ...]


class SimulationCoverageAttempt(Protocol):
    attempted_at: datetime
    context: Mapping[str, object]


def _season_for(month: int) -> str:
    if month in {3, 4, 5}:
        return "spring"
    if month in {6, 7, 8}:
        return "summer"
    if month in {9, 10, 11}:
        return "autumn"
    return "winter"


def _covered_values(
    required: tuple[object, ...],
    observed: set[object],
) -> tuple[object, ...]:
    return tuple(value for value in required if value in observed)


def validate_simulation_coverage(
    attempts: Iterable[SimulationCoverageAttempt],
    issues: IssueSink,
) -> SimulationCoverage:
    """Derive attempt coverage and report missing required boundaries."""

    seasons: set[str] = set()
    signal_values = {name: set() for name, _ in _SIGNAL_BOUNDARIES}
    dawn = False

    for attempt in attempts:
        attempted_at = attempt.attempted_at
        context = attempt.context
        seasons.add(_season_for(attempted_at.month))
        dawn = dawn or time_context_token_for_hour(attempted_at.hour) == "time:dawn"
        for name in signal_values:
            signal_values[name].add(context[name])

    coverage = SimulationCoverage(
        seasons=tuple(name for name in _SEASONS if name in seasons),
        dawn=dawn,
        ide_foreground_values=_covered_values(
            (None, False, True), signal_values["ide_foreground"]
        ),
        active_minutes_values=_covered_values(
            (None, 89, 90, 91), signal_values["active_minutes"]
        ),
        idle_return_values=_covered_values(
            (None, False, True), signal_values["idle_return"]
        ),
        fullscreen_values=_covered_values(
            (None, False, True), signal_values["fullscreen"]
        ),
    )

    missing_seasons = [name for name in _SEASONS if name not in seasons]
    if missing_seasons:
        issues.error(
            "simulation_season_coverage",
            "simulation attempts must cover every season; "
            f"missing seasons={missing_seasons!r}",
        )
    if not dawn:
        issues.error(
            "simulation_dawn_coverage",
            "simulation attempts must include a dawn event between 04:00 and 05:59",
        )
    for name, required in _SIGNAL_BOUNDARIES:
        missing = [value for value in required if value not in signal_values[name]]
        if missing:
            issues.error(
                "simulation_signal_coverage",
                f"simulation attempts must cover {name} boundary values "
                f"{list(required)!r}; missing={missing!r}",
            )

    return coverage


__all__ = (
    "SimulationCoverage",
    "SimulationCoverageAttempt",
    "validate_simulation_coverage",
)
