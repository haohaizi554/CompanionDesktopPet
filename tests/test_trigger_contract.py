from __future__ import annotations

import importlib
import importlib.util
import json
import subprocess
import sys
import unittest
from datetime import datetime
from pathlib import Path
from zoneinfo import ZoneInfo

from src.persona_corpus.context import PersonaContext
from src.persona_corpus.selector import (
    _scheduler_config_mapping,
    _trigger_matches as selector_trigger_matches,
    load_scheduler_config,
)
from src.persona_corpus.simulation_core.scenarios import (
    _trigger_matches as scenario_trigger_matches,
)
from src.persona_corpus.validation_rules.simulation_rules import (
    _simulation_context_token_matches,
    _simulation_trigger_matches,
)


ROOT = Path(__file__).resolve().parents[1]
SHANGHAI = ZoneInfo("Asia/Shanghai")
CONFIG_PATH = ROOT / "config" / "persona-scheduler.json"


def context_payload(context: PersonaContext) -> dict[str, object]:
    return {
        "event": context.event,
        "daypart": context.daypart,
        "weekday": context.weekday,
        "is_weekend": context.is_weekend,
        "holiday": context.holiday,
        "anniversary_days": context.anniversary_days,
        "minutes_since_last_output": context.minutes_since_last_output,
        "ide_foreground": context.ide_foreground,
        "active_minutes": context.active_minutes,
        "idle_return": context.idle_return,
        "fullscreen": context.fullscreen,
    }


class SharedTriggerContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.config = load_scheduler_config(CONFIG_PATH)
        cls.now = datetime(2026, 7, 22, 8, 0, tzinfo=SHANGHAI)
        cls.context = PersonaContext.from_datetime(
            cls.now,
            event="app_start",
            holiday="fixture-holiday",
            anniversary_days=1,
            minutes_since_last_output=180,
            ide_foreground=True,
            active_minutes=90,
            idle_return=True,
            fullscreen=False,
        )

    def shared_matcher(self):
        module_name = "src.persona_corpus.trigger_matching"
        self.assertIsNotNone(
            importlib.util.find_spec(module_name),
            "trigger matching must have one shared implementation module",
        )
        module = importlib.import_module(module_name)
        matcher = getattr(module, "trigger_matches", None)
        self.assertTrue(callable(matcher), "shared trigger_matches must be callable")
        return matcher

    def test_all_three_paths_delegate_to_one_table_driven_matcher(self) -> None:
        shared = self.shared_matcher()
        callbacks = (
            selector_trigger_matches,
            scenario_trigger_matches,
            _simulation_trigger_matches,
        )
        for callback in callbacks:
            self.assertIs(shared, callback)

        payload = context_payload(self.context)
        epsilon = 1e-9
        cases = (
            ("any", 0.0, True),
            ("app_start", 0.0, True),
            ("day_changed", 0.0, False),
            ("morning", 0.0, True),
            ("weekday", 0.0, True),
            ("weekend", 0.0, False),
            ("holiday", 0.0, True),
            ("anniversary", 0.0, True),
            ("long_silence", self.config.long_silence_minutes - epsilon / 2, True),
            ("long_silence", self.config.long_silence_minutes - epsilon * 2, False),
            ("ide_foreground", 0.0, True),
            ("long_active", 0.0, True),
            ("idle_return", 0.0, True),
            ("story_timer", 10_000.0, False),
            ("unknown_trigger", 10_000.0, False),
        )
        for trigger, elapsed_minutes, expected in cases:
            for callback, context in (
                (selector_trigger_matches, self.context),
                (scenario_trigger_matches, self.context),
                (_simulation_trigger_matches, payload),
            ):
                with self.subTest(
                    trigger=trigger,
                    elapsed_minutes=elapsed_minutes,
                    path=callback.__name__,
                    context_type=type(context).__name__,
                ):
                    self.assertIs(
                        expected,
                        callback(
                            trigger,
                            context,
                            elapsed_minutes,
                            self.config.long_silence_minutes,
                        ),
                    )

    def test_dawn_and_late_night_tokens_are_non_overlapping_at_boundaries(self) -> None:
        cases = (
            ((3, 59), "time:late_night"),
            ((4, 0), "time:dawn"),
            ((5, 59), "time:dawn"),
            ((6, 0), "time:morning"),
            ((22, 59), "time:evening"),
            ((23, 0), "time:late_night"),
        )
        controlled_time_tokens = {
            "time:dawn",
            "time:late_night",
            "time:morning",
            "time:noon",
            "time:afternoon",
            "time:evening",
        }
        for (hour, minute), expected_token in cases:
            now = datetime(2026, 7, 22, hour, minute, tzinfo=SHANGHAI)
            context = PersonaContext.from_datetime(now)
            payload = context_payload(context)
            with self.subTest(now=now.isoformat(), layer="context"):
                self.assertEqual(
                    {expected_token},
                    context.controlled_tokens(now) & controlled_time_tokens,
                )
            for token in sorted(controlled_time_tokens):
                with self.subTest(now=now.isoformat(), layer="validator", token=token):
                    self.assertIs(
                        token == expected_token,
                        _simulation_context_token_matches(token, payload, now),
                    )

    def test_plain_selector_import_does_not_read_files(self) -> None:
        script = """
import sys
from pathlib import Path

def forbidden_read(*args, **kwargs):
    raise AssertionError(f\"selector import read a file: {args[0]}\")

Path.read_text = forbidden_read
import src.persona_corpus.selector

assert "src.persona_corpus.contract" not in sys.modules
assert "src.persona_corpus.lexical" not in sys.modules
"""
        completed = subprocess.run(
            [sys.executable, "-c", script],
            cwd=ROOT,
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)

    def test_typed_scheduler_round_trip_preserves_validated_provenance(self) -> None:
        raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        typed = load_scheduler_config(CONFIG_PATH)

        self.assertEqual(raw["$schema"], getattr(typed, "schema_reference", None))
        self.assertEqual(raw["derived_from"], dict(getattr(typed, "derived_from", {})))
        normalized = _scheduler_config_mapping(typed)
        self.assertEqual(raw["$schema"], normalized.get("$schema"))
        self.assertEqual(raw["derived_from"], normalized.get("derived_from"))


if __name__ == "__main__":
    unittest.main()
