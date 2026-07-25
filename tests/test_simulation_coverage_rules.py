from __future__ import annotations

import unittest
from dataclasses import dataclass, replace
from datetime import datetime, timezone
from typing import Mapping

from src.persona_corpus.validation_rules.core import _Issues
from src.persona_corpus.validation_rules.simulation_coverage_rules import (
    validate_simulation_coverage,
)


@dataclass(frozen=True, slots=True)
class _Attempt:
    attempted_at: datetime
    context: Mapping[str, object]


def _attempt(
    month: int,
    hour: int,
    *,
    ide_foreground: bool | None,
    active_minutes: int | None,
    idle_return: bool | None,
    fullscreen: bool | None,
) -> _Attempt:
    return _Attempt(
        attempted_at=datetime(2026, month, 4, hour, tzinfo=timezone.utc),
        context={
            "ide_foreground": ide_foreground,
            "active_minutes": active_minutes,
            "idle_return": idle_return,
            "fullscreen": fullscreen,
        },
    )


def _complete_attempts() -> tuple[_Attempt, ...]:
    return (
        _attempt(
            3,
            5,
            ide_foreground=None,
            active_minutes=None,
            idle_return=None,
            fullscreen=None,
        ),
        _attempt(
            6,
            7,
            ide_foreground=False,
            active_minutes=89,
            idle_return=False,
            fullscreen=False,
        ),
        _attempt(
            9,
            7,
            ide_foreground=True,
            active_minutes=90,
            idle_return=True,
            fullscreen=True,
        ),
        _attempt(
            12,
            7,
            ide_foreground=True,
            active_minutes=91,
            idle_return=True,
            fullscreen=True,
        ),
    )


def _validate(attempts: tuple[_Attempt, ...]):
    issues = _Issues()
    coverage = validate_simulation_coverage(attempts, issues)
    return coverage, issues.report().errors


class SimulationCoverageRulesTests(unittest.TestCase):
    def test_complete_attempts_derive_every_required_coverage_boundary(self) -> None:
        coverage, errors = _validate(_complete_attempts())

        self.assertEqual((), errors)
        self.assertEqual(
            ("spring", "summer", "autumn", "winter"), coverage.seasons
        )
        self.assertTrue(coverage.dawn)
        self.assertEqual(
            (None, False, True), coverage.ide_foreground_values
        )
        self.assertEqual(
            (None, 89, 90, 91), coverage.active_minutes_values
        )
        self.assertEqual((None, False, True), coverage.idle_return_values)
        self.assertEqual((None, False, True), coverage.fullscreen_values)

    def test_missing_each_season_is_a_hard_coverage_error(self) -> None:
        replacements = {3: 6, 6: 9, 9: 12, 12: 3}
        names = {3: "spring", 6: "summer", 9: "autumn", 12: "winter"}

        for missing_month, replacement_month in replacements.items():
            with self.subTest(season=names[missing_month]):
                attempts = tuple(
                    replace(
                        attempt,
                        attempted_at=attempt.attempted_at.replace(
                            month=replacement_month
                        ),
                    )
                    if attempt.attempted_at.month == missing_month
                    else attempt
                    for attempt in _complete_attempts()
                )

                _, errors = _validate(attempts)

                self.assertEqual(
                    ["simulation_season_coverage"],
                    [error.code for error in errors],
                )
                self.assertIn(names[missing_month], errors[0].message)

    def test_six_oclock_does_not_count_as_dawn_coverage(self) -> None:
        attempts = tuple(
            replace(
                attempt,
                attempted_at=attempt.attempted_at.replace(hour=6),
            )
            if attempt.attempted_at.hour == 5
            else attempt
            for attempt in _complete_attempts()
        )

        coverage, errors = _validate(attempts)

        self.assertFalse(coverage.dawn)
        self.assertEqual(
            ["simulation_dawn_coverage"],
            [error.code for error in errors],
        )

    def test_missing_each_nullable_signal_boundary_is_a_hard_error(self) -> None:
        cases = (
            ("ide_foreground", None, False),
            ("ide_foreground", False, None),
            ("ide_foreground", True, None),
            ("active_minutes", None, 89),
            ("active_minutes", 89, None),
            ("active_minutes", 90, None),
            ("active_minutes", 91, None),
            ("idle_return", None, False),
            ("idle_return", False, None),
            ("idle_return", True, None),
            ("fullscreen", None, False),
            ("fullscreen", False, None),
            ("fullscreen", True, None),
        )

        for signal, missing_value, replacement_value in cases:
            with self.subTest(signal=signal, missing_value=missing_value):
                attempts = tuple(
                    replace(
                        attempt,
                        context={
                            **attempt.context,
                            signal: replacement_value,
                        },
                    )
                    if (
                        type(attempt.context[signal]) is type(missing_value)
                        and attempt.context[signal] == missing_value
                    )
                    else attempt
                    for attempt in _complete_attempts()
                )

                _, errors = _validate(attempts)

                self.assertEqual(
                    ["simulation_signal_coverage"],
                    [error.code for error in errors],
                )
                self.assertIn(signal, errors[0].message)
                self.assertIn(repr(missing_value), errors[0].message)


if __name__ == "__main__":
    unittest.main()
