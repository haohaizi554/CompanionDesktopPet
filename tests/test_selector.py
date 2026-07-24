from __future__ import annotations

import json
import os
import random
import tempfile
import unittest
from collections import Counter
from dataclasses import replace
from datetime import UTC, datetime, timedelta
from pathlib import Path
from zoneinfo import ZoneInfo

from src.persona_corpus.context import ContextError, PersonaContext
from src.persona_corpus.contract import EXPANDED_RUNTIME_ROWS
from src.persona_corpus.history import HistoryFormatError, HistoryRecord, SelectionHistory
from src.persona_corpus.loader import load_v2
from src.persona_corpus.models import CorpusLine
from src.persona_corpus.selector import (
    DEFAULT_SCHEDULER_CONFIG,
    SchedulerConfig,
    SelectorConfigError,
    load_scheduler_config,
    prepare_corpus,
    select_line,
)
from src.persona_corpus.surface_exposure import surface_exposure


SHANGHAI = ZoneInfo("Asia/Shanghai")
ROOT = Path(__file__).resolve().parents[1]
CONFIG_PATH = ROOT / "config/persona-scheduler.json"
CORPUS_PATH = ROOT / "data/optimized/persona-corpus-v2.tsv"
NOW = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)


def corpus_line(**overrides: object) -> CorpusLine:
    values: dict[str, object] = {
        "id": "line-a",
        "category": "Life",
        "category_group": "character_life",
        "topic_id": "life.breakfast",
        "semantic_group": "life.breakfast.observation",
        "output_mode": "self_talk",
        "trigger": "any",
        "required_context": "none",
        "tone": "warm",
        "interrupt_cost": 0,
        "cooldown_hours": 1.0,
        "semantic_cooldown_hours": 1.0,
        "max_per_day": 1,
        "weight": 1.0,
        "requires_reply": False,
        "enabled": True,
        "text": "早饭的热气很适合慢慢醒神。",
        "source_kind": "catalog",
        "source_reference": "catalog:test",
        "rewrite_reason": "test fixture",
    }
    values.update(overrides)
    return CorpusLine(**values)  # type: ignore[arg-type]


def history_record(
    row: CorpusLine,
    played_at: datetime,
    **overrides: object,
) -> HistoryRecord:
    values: dict[str, object] = {
        "selected_id": row.id,
        "played_at": played_at,
        "category": row.category,
        "category_group": row.category_group,
        "semantic_group": row.semantic_group,
        "output_mode": row.output_mode,
        "trigger": row.trigger,
        "interrupt_cost": row.interrupt_cost,
        "was_seasoning": False,
    }
    values.update(overrides)
    return HistoryRecord(**values)  # type: ignore[arg-type]


def context_at(now: datetime = NOW, **overrides: object) -> PersonaContext:
    values: dict[str, object] = {"minutes_since_last_output": 600}
    values.update(overrides)
    return PersonaContext.from_datetime(now, **values)  # type: ignore[arg-type]


class PersonaContextTests(unittest.TestCase):
    def test_from_datetime_derives_calendar_and_time_tokens(self) -> None:
        now = datetime(2026, 3, 1, 4, 30, tzinfo=SHANGHAI)

        context = PersonaContext.from_datetime(
            now,
            event="day_changed",
            holiday="元旦补休",
            anniversary_days=7,
            minutes_since_last_output=180,
        )

        self.assertEqual("late_night", context.daypart)
        self.assertEqual(7, context.weekday)
        self.assertTrue(context.is_weekend)
        self.assertEqual(
            {
                "day:weekend",
                "time:dawn",
                "time:late_night",
                "season:spring",
                "holiday",
                "date:holiday",
                "anniversary",
                "date:month_boundary",
            },
            context.controlled_tokens(now),
        )

    def test_nullable_future_signals_are_not_assumed(self) -> None:
        now = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)
        context = PersonaContext.from_datetime(now)

        tokens = context.controlled_tokens(now)

        self.assertNotIn("ide_foreground", tokens)
        self.assertNotIn("active_90m", tokens)
        self.assertNotIn("idle_return", tokens)
        self.assertNotIn("not_fullscreen", tokens)

    def test_future_signals_only_emit_demonstrably_true_tokens(self) -> None:
        now = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)
        context = PersonaContext.from_datetime(
            now,
            ide_foreground=True,
            active_minutes=90,
            idle_return=True,
            fullscreen=False,
        )

        tokens = context.controlled_tokens(now)

        self.assertTrue(
            {"ide_foreground", "active_90m", "idle_return", "not_fullscreen"}
            <= tokens
        )

    def test_malformed_context_is_rejected(self) -> None:
        now = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)
        with self.assertRaises(ContextError):
            PersonaContext.from_datetime(now.replace(tzinfo=None))
        with self.assertRaises(ContextError):
            PersonaContext.from_datetime(now, event="surprise")
        with self.assertRaises(ContextError):
            PersonaContext.from_datetime(now, active_minutes=True)
        with self.assertRaises(ContextError):
            PersonaContext.from_datetime(now, holiday="  ")

    def test_controlled_tokens_reject_context_inconsistent_with_now(self) -> None:
        source = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)
        context = PersonaContext.from_datetime(source)

        with self.assertRaises(ContextError):
            context.controlled_tokens(source.replace(hour=19))


class SelectionHistoryTests(unittest.TestCase):
    def record(self, *, selected_id: str = "line-a", played_at: datetime | None = None) -> HistoryRecord:
        return HistoryRecord(
            selected_id=selected_id,
            played_at=played_at or datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI),
            category="Life",
            category_group="character_life",
            semantic_group="life.breakfast",
            output_mode="self_talk",
            trigger="morning",
            interrupt_cost=1,
        )

    def test_json_round_trip_preserves_exact_history_fields(self) -> None:
        records = (
            self.record(),
            self.record(
                selected_id="line-b",
                played_at=datetime(2026, 7, 22, 11, 0, 0, 123456, tzinfo=SHANGHAI),
            ),
        )
        history = SelectionHistory(records)

        payload = history.to_json()
        restored = SelectionHistory.from_json(
            payload, now=datetime(2026, 7, 22, 12, 0, tzinfo=SHANGHAI)
        )

        self.assertEqual(records, restored.records)
        self.assertEqual(payload, restored.to_json())
        self.assertTrue(payload.endswith("\n"))
        self.assertEqual(
            {
                "selected_id",
                "played_at",
                "category",
                "category_group",
                "semantic_group",
                "output_mode",
                "trigger",
                "interrupt_cost",
                "was_dry_sharp",
                "was_seasoning",
                "surface_opening",
                "surface_ending",
                "surface_template",
            },
            set(json.loads(payload)["records"][0]),
        )

    def test_old_history_without_playback_exposure_fields_remains_readable(self) -> None:
        payload = json.loads(SelectionHistory([self.record()]).to_json())
        for key in (
            "was_dry_sharp",
            "was_seasoning",
            "surface_opening",
            "surface_ending",
            "surface_template",
        ):
            del payload["records"][0][key]

        restored = SelectionHistory.from_json(json.dumps(payload))

        record = restored.records[0]
        self.assertFalse(record.was_dry_sharp)
        self.assertIsNone(record.was_seasoning)
        self.assertEqual("", record.surface_template)

    def test_json_schema_and_key_order_are_deterministic(self) -> None:
        history = SelectionHistory([self.record()])

        first = history.to_json()
        second = history.to_json()

        self.assertEqual(first, second)
        self.assertEqual(1, json.loads(first)["schema_version"])

    def test_ambiguous_dst_timestamp_round_trip_uses_canonical_instant_equality(self) -> None:
        eastern = ZoneInfo("America/New_York")
        for fold in (0, 1):
            ambiguous = datetime(2026, 11, 1, 1, 30, tzinfo=eastern, fold=fold)
            record = self.record(played_at=ambiguous)
            history = SelectionHistory([record])

            restored = SelectionHistory.from_json(
                history.to_json(), now=datetime(2026, 11, 1, 8, 0, tzinfo=UTC)
            )

            with self.subTest(fold=fold):
                self.assertEqual(ambiguous.astimezone(UTC), record.played_at)
                self.assertEqual(history.records, restored.records)

    def test_from_json_rejects_bad_version_unknown_or_missing_keys(self) -> None:
        good = json.loads(SelectionHistory([self.record()]).to_json())
        fixtures = []
        for version in (2, 1.0, True):
            bad_version = dict(good)
            bad_version["schema_version"] = version
            fixtures.append(bad_version)
        extra_root = dict(good)
        extra_root["extra"] = True
        fixtures.append(extra_root)
        missing_record_key = json.loads(json.dumps(good))
        del missing_record_key["records"][0]["trigger"]
        fixtures.append(missing_record_key)
        extra_record_key = json.loads(json.dumps(good))
        extra_record_key["records"][0]["extra"] = 1
        fixtures.append(extra_record_key)

        for fixture in fixtures:
            with self.subTest(fixture=fixture), self.assertRaises(HistoryFormatError):
                SelectionHistory.from_json(json.dumps(fixture))

    def test_from_json_rejects_duplicate_keys_nonfinite_and_non_object_root(self) -> None:
        fixtures = (
            '{"schema_version":1,"schema_version":1,"records":[]}',
            '{"schema_version":1,"records":[],"x":NaN}',
            "[]",
        )
        for payload in fixtures:
            with self.subTest(payload=payload), self.assertRaises(HistoryFormatError):
                SelectionHistory.from_json(payload)

    def test_from_json_rejects_naive_invalid_and_future_timestamps(self) -> None:
        base = json.loads(SelectionHistory([self.record()]).to_json())
        now = datetime(2026, 7, 22, 12, 0, tzinfo=SHANGHAI)
        timestamps = (
            "2026-07-22T10:00:00",
            "not-a-date",
            (now + timedelta(microseconds=1)).isoformat(),
        )
        for timestamp in timestamps:
            fixture = json.loads(json.dumps(base))
            fixture["records"][0]["played_at"] = timestamp
            with self.subTest(timestamp=timestamp), self.assertRaises(HistoryFormatError):
                SelectionHistory.from_json(json.dumps(fixture), now=now)

    def test_from_json_normalizes_all_bad_record_types_to_history_errors(self) -> None:
        base = json.loads(SelectionHistory([self.record()]).to_json())
        mutations = (
            ("interrupt_cost", True),
            ("selected_id", 7),
            ("category_group", []),
            ("output_mode", "broadcast"),
        )
        for key, value in mutations:
            fixture = json.loads(json.dumps(base))
            fixture["records"][0][key] = value
            with self.subTest(key=key), self.assertRaises(HistoryFormatError):
                SelectionHistory.from_json(json.dumps(fixture))

    def test_record_values_and_chronological_order_are_strict(self) -> None:
        with self.assertRaises(HistoryFormatError):
            HistoryRecord(
                selected_id="",
                played_at=datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI),
                category="Life",
                category_group="character_life",
                semantic_group="life.breakfast",
                output_mode="self_talk",
                trigger="any",
                interrupt_cost=0,
            )
        with self.assertRaises(HistoryFormatError):
            HistoryRecord(
                selected_id="a",
                played_at=datetime(2026, 7, 22, 10, 0),
                category="Life",
                category_group="character_life",
                semantic_group="life.breakfast",
                output_mode="self_talk",
                trigger="any",
                interrupt_cost=0,
            )
        with self.assertRaises(HistoryFormatError):
            SelectionHistory(
                [
                    self.record(played_at=datetime(2026, 7, 22, 11, 0, tzinfo=SHANGHAI)),
                    self.record(played_at=datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)),
                ]
            )

    def test_append_updates_once_and_rejects_time_reversal(self) -> None:
        history = SelectionHistory([self.record()])
        later = self.record(
            selected_id="line-b",
            played_at=datetime(2026, 7, 22, 11, 0, tzinfo=SHANGHAI),
        )

        history.append(later)

        self.assertEqual((self.record(), later), history.records)
        with self.assertRaises(HistoryFormatError):
            history.append(
                self.record(
                    selected_id="line-c",
                    played_at=datetime(2026, 7, 22, 10, 30, tzinfo=SHANGHAI),
                )
            )


class SchedulerConfigTests(unittest.TestCase):
    def test_repository_config_loads_as_immutable_typed_values(self) -> None:
        config = load_scheduler_config(CONFIG_PATH)

        self.assertIsInstance(config, SchedulerConfig)
        self.assertAlmostEqual(1.0, sum(config.category_group_weights.values()))
        self.assertEqual(8, config.minimum_interval_minutes)
        with self.assertRaises(TypeError):
            config.category_group_weights["technical"] = 0.99  # type: ignore[index]

    def test_bad_config_fails_at_loader_and_fails_safe_at_selection(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "bad.json"
            path.write_text('{"schema_version":1}', encoding="utf-8")
            with self.assertRaises(SelectorConfigError):
                load_scheduler_config(path)

        history = SelectionHistory()
        result = select_line(
            [corpus_line()],
            context_at(),
            history,
            NOW,
            seed=1,
            scheduler_config={"schema_version": 1},
        )
        self.assertIsNone(result)
        self.assertEqual((), history.records)

    def test_float_schema_version_and_malformed_typed_config_fail_safe(self) -> None:
        raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
        raw["schema_version"] = 1.0
        with self.assertRaises(SelectorConfigError):
            SchedulerConfig.from_mapping(raw)

        malformed_configs = (
            replace(
                DEFAULT_SCHEDULER_CONFIG,
                category_group_weights={"character_life": "bad"},  # type: ignore[dict-item]
            ),
            replace(
                DEFAULT_SCHEDULER_CONFIG,
                interrupt_cost_minimum_intervals_minutes=[],  # type: ignore[arg-type]
            ),
        )
        for malformed in malformed_configs:
            history = SelectionHistory()
            with self.subTest(config=malformed):
                self.assertIsNone(
                    select_line(
                        [corpus_line()],
                        context_at(),
                        history,
                        NOW,
                        scheduler_config=malformed,
                    )
                )
                self.assertEqual((), history.records)

    def test_default_config_has_no_current_working_directory_dependency(self) -> None:
        history = SelectionHistory()
        previous = Path.cwd()
        with tempfile.TemporaryDirectory() as directory:
            try:
                os.chdir(directory)
                selected = select_line([corpus_line()], context_at(), history, NOW, seed=1)
            finally:
                os.chdir(previous)

        self.assertIsNotNone(selected)
        self.assertIsInstance(DEFAULT_SCHEDULER_CONFIG, SchedulerConfig)


class TriggerAndContextSelectionTests(unittest.TestCase):
    def assert_trigger(
        self,
        trigger: str,
        now: datetime,
        *,
        matches: dict[str, object],
        misses: dict[str, object],
    ) -> None:
        row = corpus_line(trigger=trigger)
        self.assertIsNotNone(
            select_line([row], context_at(now, **matches), SelectionHistory(), now, seed=0),
            trigger,
        )
        self.assertIsNone(
            select_line([row], context_at(now, **misses), SelectionHistory(), now, seed=0),
            trigger,
        )

    def test_any_and_event_triggers_match_exactly(self) -> None:
        self.assertIsNotNone(
            select_line([corpus_line(trigger="any")], context_at(), SelectionHistory(), NOW, seed=0)
        )
        self.assert_trigger(
            "app_start", NOW, matches={"event": "app_start"}, misses={"event": "tick"}
        )
        self.assert_trigger(
            "day_changed", NOW, matches={"event": "day_changed"}, misses={"event": "tick"}
        )

    def test_daypart_triggers_match_derived_local_time(self) -> None:
        cases = {
            "morning": 8,
            "noon": 12,
            "afternoon": 15,
            "evening": 20,
            "late_night": 23,
        }
        for trigger, hour in cases.items():
            now = NOW.replace(hour=hour)
            row = corpus_line(trigger=trigger)
            with self.subTest(trigger=trigger):
                self.assertIsNotNone(
                    select_line([row], context_at(now), SelectionHistory(), now, seed=0)
                )
                other = NOW.replace(hour=12 if trigger != "noon" else 8)
                self.assertIsNone(
                    select_line([row], context_at(other), SelectionHistory(), other, seed=0)
                )

    def test_weekday_weekend_holiday_anniversary_and_long_silence_triggers(self) -> None:
        weekday = datetime(2026, 7, 22, 10, 0, tzinfo=SHANGHAI)
        weekend = datetime(2026, 7, 25, 10, 0, tzinfo=SHANGHAI)
        self.assertIsNotNone(
            select_line(
                [corpus_line(trigger="weekday")], context_at(weekday), SelectionHistory(), weekday
            )
        )
        self.assertIsNone(
            select_line(
                [corpus_line(trigger="weekday")], context_at(weekend), SelectionHistory(), weekend
            )
        )
        self.assertIsNotNone(
            select_line(
                [corpus_line(trigger="weekend")], context_at(weekend), SelectionHistory(), weekend
            )
        )
        self.assert_trigger(
            "holiday", weekday, matches={"holiday": "中秋"}, misses={"holiday": None}
        )
        self.assert_trigger(
            "anniversary",
            weekday,
            matches={"anniversary_days": 1},
            misses={"anniversary_days": 0},
        )
        self.assert_trigger(
            "long_silence",
            weekday,
            matches={"minutes_since_last_output": 180},
            misses={"minutes_since_last_output": 179.999},
        )

    def test_future_signal_triggers_never_guess_unknown_values(self) -> None:
        cases = (
            ("ide_foreground", {"ide_foreground": True}, {"ide_foreground": None}),
            ("long_active", {"active_minutes": 90}, {"active_minutes": None}),
            ("idle_return", {"idle_return": True}, {"idle_return": None}),
        )
        for trigger, matches, misses in cases:
            with self.subTest(trigger=trigger):
                self.assert_trigger(trigger, NOW, matches=matches, misses=misses)
        self.assertIsNone(
            select_line(
                [corpus_line(trigger="story_timer")], context_at(), SelectionHistory(), NOW
            )
        )

    def test_required_context_is_an_and_of_demonstrable_tokens(self) -> None:
        row = corpus_line(
            required_context="time:morning,day:weekday,ide_foreground,not_fullscreen"
        )
        matching = context_at(ide_foreground=True, fullscreen=False)
        unknown = context_at(ide_foreground=None, fullscreen=None)

        self.assertIsNotNone(
            select_line([row], matching, SelectionHistory(), NOW, seed=0)
        )
        self.assertIsNone(select_line([row], unknown, SelectionHistory(), NOW, seed=0))
        self.assertIsNone(
            select_line(
                [replace(row, required_context="time:morning,unknown_token")],
                matching,
                SelectionHistory(),
                NOW,
                seed=0,
            )
        )

    def test_inconsistent_context_or_naive_now_fails_without_mutation(self) -> None:
        history = SelectionHistory()
        wrong = context_at(NOW.replace(hour=19))
        self.assertIsNone(select_line([corpus_line()], wrong, history, NOW, seed=0))
        self.assertIsNone(
            select_line([corpus_line()], context_at(), history, NOW.replace(tzinfo=None), seed=0)
        )
        self.assertEqual((), history.records)


class SelectorFilterTests(unittest.TestCase):
    def select(
        self,
        row: CorpusLine,
        history: SelectionHistory | None = None,
        *,
        now: datetime = NOW,
        minutes: float | None = None,
    ):
        history = history or SelectionHistory()
        if minutes is None:
            minutes = (
                (now.astimezone(UTC) - history.records[-1].played_at.astimezone(UTC)).total_seconds()
                / 60
                if history.records
                else 600
            )
        return select_line(
            [row], context_at(now, minutes_since_last_output=minutes), history, now, seed=0
        )

    def test_disabled_rows_are_removed_first(self) -> None:
        history = SelectionHistory()
        self.assertIsNone(self.select(corpus_line(enabled=False), history))
        self.assertEqual((), history.records)

    def test_id_cooldown_blocks_inside_and_allows_exact_boundary(self) -> None:
        row = corpus_line(cooldown_hours=2.0, semantic_cooldown_hours=2.0, max_per_day=2)
        other = corpus_line(
            id="other", semantic_group="other.semantic", category_group="growth"
        )
        inside = SelectionHistory(
            [history_record(row, NOW - timedelta(minutes=119)), history_record(other, NOW - timedelta(minutes=60))]
        )
        boundary = SelectionHistory(
            [history_record(row, NOW - timedelta(minutes=120)), history_record(other, NOW - timedelta(minutes=60))]
        )

        self.assertIsNone(self.select(row, inside, minutes=60))
        self.assertIsNotNone(self.select(row, boundary, minutes=60))

    def test_semantic_cooldown_blocks_across_ids_and_allows_exact_boundary(self) -> None:
        previous = corpus_line(id="previous", semantic_cooldown_hours=2.0)
        row = corpus_line(
            id="candidate", cooldown_hours=1.0, semantic_cooldown_hours=2.0
        )
        other = corpus_line(
            id="other", semantic_group="other.semantic", category_group="growth"
        )
        inside = SelectionHistory(
            [history_record(previous, NOW - timedelta(minutes=119)), history_record(other, NOW - timedelta(minutes=60))]
        )
        boundary = SelectionHistory(
            [history_record(previous, NOW - timedelta(minutes=120)), history_record(other, NOW - timedelta(minutes=60))]
        )

        self.assertIsNone(self.select(row, inside, minutes=60))
        self.assertIsNotNone(self.select(row, boundary, minutes=60))

    def test_max_per_day_uses_now_local_date_across_timestamp_offsets(self) -> None:
        now = datetime(2026, 7, 23, 6, 10, tzinfo=SHANGHAI)
        row = corpus_line(cooldown_hours=1.0, semantic_cooldown_hours=1.0)
        other = corpus_line(
            id="other", semantic_group="other.semantic", category_group="growth"
        )
        intervening = history_record(other, datetime(2026, 7, 23, 5, 50, tzinfo=SHANGHAI))
        same_local_day = SelectionHistory(
            [history_record(row, datetime(2026, 7, 22, 16, 0, tzinfo=UTC)), intervening]
        )
        previous_local_day = SelectionHistory(
            [history_record(row, datetime(2026, 7, 22, 15, 50, tzinfo=UTC)), intervening]
        )

        self.assertIsNone(self.select(row, same_local_day, now=now, minutes=20))
        self.assertIsNotNone(self.select(row, previous_local_day, now=now, minutes=20))

    def test_minimum_interval_uses_real_history_and_allows_exact_eight_minutes(self) -> None:
        prior = corpus_line(id="prior", category_group="growth", semantic_group="prior")
        inside = SelectionHistory([history_record(prior, NOW - timedelta(minutes=7, seconds=59))])
        boundary = SelectionHistory([history_record(prior, NOW - timedelta(minutes=8))])

        self.assertIsNone(self.select(corpus_line(), inside, minutes=600))
        self.assertIsNotNone(self.select(corpus_line(), boundary, minutes=8))
        self.assertIsNotNone(self.select(corpus_line(), SelectionHistory(), minutes=0))

    def test_first_selection_has_no_adjacent_interrupt_gap_to_enforce(self) -> None:
        self.assertIsNotNone(
            self.select(corpus_line(interrupt_cost=5), SelectionHistory(), minutes=0)
        )

    def test_rolling_hour_budget_excludes_exactly_sixty_minutes_old(self) -> None:
        first = corpus_line(id="first", category_group="growth", semantic_group="first")
        second = corpus_line(id="second", category_group="career", semantic_group="second")
        blocked = SelectionHistory(
            [history_record(first, NOW - timedelta(minutes=59)), history_record(second, NOW - timedelta(minutes=30))]
        )
        boundary = SelectionHistory(
            [history_record(first, NOW - timedelta(minutes=60)), history_record(second, NOW - timedelta(minutes=30))]
        )

        self.assertIsNone(self.select(corpus_line(), blocked, minutes=30))
        self.assertIsNotNone(self.select(corpus_line(), boundary, minutes=30))

    def test_late_night_budget_rolls_across_midnight_and_allows_boundary(self) -> None:
        now = datetime(2026, 7, 23, 0, 10, tzinfo=SHANGHAI)
        prior = corpus_line(id="prior", category_group="growth", semantic_group="prior")
        blocked = SelectionHistory([history_record(prior, now - timedelta(minutes=40))])
        boundary = SelectionHistory([history_record(prior, now - timedelta(minutes=60))])

        self.assertIsNone(self.select(corpus_line(), blocked, now=now, minutes=40))
        self.assertIsNotNone(self.select(corpus_line(), boundary, now=now, minutes=60))

    def test_interrupt_cost_gap_is_candidate_specific_and_boundary_is_allowed(self) -> None:
        prior = corpus_line(id="prior", category_group="growth", semantic_group="prior")
        row = corpus_line(interrupt_cost=5)
        inside = SelectionHistory([history_record(prior, NOW - timedelta(minutes=59))])
        boundary = SelectionHistory([history_record(prior, NOW - timedelta(minutes=60))])

        self.assertIsNone(self.select(row, inside, minutes=59))
        self.assertIsNotNone(self.select(row, boundary, minutes=60))

    def test_adjacent_semantic_group_is_always_blocked(self) -> None:
        previous = corpus_line(id="previous", semantic_cooldown_hours=1.0)
        row = corpus_line(id="candidate", semantic_cooldown_hours=1.0)
        history = SelectionHistory([history_record(previous, NOW - timedelta(hours=2))])

        self.assertIsNone(self.select(row, history, minutes=120))

    def test_adjacent_special_category_groups_are_blocked(self) -> None:
        for group in ("technical", "daily_care", "emotional_reflection"):
            previous = corpus_line(
                id=f"previous-{group}", category_group=group, semantic_group=f"previous.{group}"
            )
            row = corpus_line(
                id=f"candidate-{group}", category_group=group, semantic_group=f"candidate.{group}"
            )
            history = SelectionHistory([history_record(previous, NOW - timedelta(hours=2))])
            with self.subTest(group=group):
                self.assertIsNone(self.select(row, history, minutes=120))

    def test_technical_quota_counts_candidate_in_most_recent_five(self) -> None:
        groups = ("technical", "growth", "technical", "career")
        records = []
        for index, group in enumerate(groups):
            item = corpus_line(
                id=f"h-{index}", category_group=group, semantic_group=f"h.{index}"
            )
            records.append(history_record(item, NOW - timedelta(minutes=350 - index * 70)))
        history = SelectionHistory(records)
        candidate = corpus_line(category_group="technical", semantic_group="candidate.tech")

        self.assertIsNone(self.select(candidate, history, minutes=140))

    def test_technical_quota_drops_matches_older_than_candidate_aware_window(self) -> None:
        groups = ("technical", "technical", "growth", "career", "growth")
        records = []
        for index, group in enumerate(groups):
            item = corpus_line(
                id=f"old-{index}", category_group=group, semantic_group=f"old.{index}"
            )
            records.append(
                history_record(item, NOW - timedelta(minutes=(5 - index) * 70))
            )
        history = SelectionHistory(records)
        candidate = corpus_line(category_group="technical", semantic_group="candidate.tech")

        self.assertIsNotNone(self.select(candidate, history, minutes=70))

    def test_user_direct_quota_counts_candidate_in_most_recent_ten(self) -> None:
        records = []
        for index in range(9):
            mode = "user_direct" if index in {2, 6} else "self_talk"
            item = corpus_line(
                id=f"h-{index}",
                category_group="growth",
                semantic_group=f"h.{index}",
                output_mode=mode,
            )
            records.append(history_record(item, NOW - timedelta(minutes=(9 - index) * 70)))
        history = SelectionHistory(records)

        self.assertIsNone(
            self.select(corpus_line(output_mode="user_direct"), history, minutes=70)
        )

    def test_easter_egg_quota_is_candidate_aware_and_oldest_entry_falls_out(self) -> None:
        def records(count: int, egg_at: int) -> list[HistoryRecord]:
            result = []
            for index in range(count):
                group = "easter_egg" if index == egg_at else "character_life"
                item = corpus_line(
                    id=f"h-{count}-{index}",
                    category_group=group,
                    semantic_group=f"h.{count}.{index}",
                )
                result.append(
                    history_record(item, NOW - timedelta(minutes=(count - index) * 70))
                )
            return result

        candidate = corpus_line(category_group="easter_egg", semantic_group="egg.new", weight=0.1)
        blocked = SelectionHistory(records(9, 0))
        oldest_falls_out = SelectionHistory(records(10, 0))

        self.assertIsNone(self.select(candidate, blocked, minutes=70))
        self.assertIsNotNone(self.select(candidate, oldest_falls_out, minutes=70))

    def test_future_history_fails_safe_without_mutation(self) -> None:
        future = corpus_line(id="future", category_group="growth", semantic_group="future")
        history = SelectionHistory([history_record(future, NOW + timedelta(seconds=1))])
        before = history.records

        self.assertIsNone(self.select(corpus_line(), history, minutes=600))
        self.assertEqual(before, history.records)

    def test_dst_fallback_uses_absolute_elapsed_time_and_exact_hour_boundary(self) -> None:
        eastern = ZoneInfo("America/New_York")
        prior_at = datetime(2026, 11, 1, 1, 3, tzinfo=eastern, fold=0)
        now = datetime(2026, 11, 1, 1, 3, tzinfo=eastern, fold=1)
        prior = corpus_line(id="prior", category_group="growth", semantic_group="prior")
        history = SelectionHistory([history_record(prior, prior_at)])

        self.assertIsNotNone(self.select(corpus_line(), history, now=now, minutes=60))

    def test_dst_spring_and_fall_short_absolute_gaps_cannot_bypass_budgets(self) -> None:
        eastern = ZoneInfo("America/New_York")
        cases = (
            (
                datetime(2026, 3, 8, 1, 55, tzinfo=eastern),
                datetime(2026, 3, 8, 3, 5, tzinfo=eastern),
                10,
                1,
            ),
            (
                datetime(2026, 11, 1, 1, 55, tzinfo=eastern, fold=0),
                datetime(2026, 11, 1, 1, 15, tzinfo=eastern, fold=1),
                20,
                4,
            ),
        )
        for prior_at, now, elapsed, cost in cases:
            prior = corpus_line(id="prior", category_group="growth", semantic_group="prior")
            history = SelectionHistory([history_record(prior, prior_at)])
            candidate = corpus_line(interrupt_cost=cost)
            with self.subTest(now=now):
                self.assertIsNone(
                    self.select(candidate, history, now=now, minutes=elapsed)
                )


class SelectorScoringAndMutationTests(unittest.TestCase):
    def test_character_life_preference_emerges_from_config_weight(self) -> None:
        character = corpus_line(id="character", category_group="character_life")
        technical = corpus_line(
            id="technical", category_group="technical", semantic_group="technical.one"
        )

        selected = select_line(
            [technical, character], context_at(), SelectionHistory(), NOW, seed=0
        )

        self.assertIsNotNone(selected)
        self.assertEqual("character", selected.row.id)
        self.assertTrue(any(reason.startswith("group_deficit=") for reason in selected.reasons))

    def test_group_deficit_and_output_mode_deficit_drive_scoring(self) -> None:
        records = []
        for index in range(8):
            item = corpus_line(id=f"old-{index}", semantic_group=f"old.{index}")
            records.append(history_record(item, NOW - timedelta(minutes=(8 - index) * 70)))
        group_history = SelectionHistory(records)
        technical = corpus_line(
            id="technical", category_group="technical", semantic_group="technical.one"
        )
        character = corpus_line(id="character", semantic_group="character.new")
        chosen_group = select_line(
            [character, technical],
            context_at(minutes_since_last_output=70),
            group_history,
            NOW,
            seed=0,
        )
        self.assertEqual("technical", chosen_group.row.id if chosen_group else None)

        mode_records = []
        for index in range(8):
            item = corpus_line(id=f"mode-{index}", semantic_group=f"mode.{index}")
            mode_records.append(history_record(item, NOW - timedelta(minutes=(8 - index) * 70)))
        mode_history = SelectionHistory(mode_records)
        self_talk = corpus_line(id="self", semantic_group="self.new", output_mode="self_talk")
        observe = corpus_line(
            id="observe", semantic_group="observe.new", output_mode="system_observe"
        )
        chosen_mode = select_line(
            [self_talk, observe],
            context_at(minutes_since_last_output=70),
            mode_history,
            NOW,
            seed=0,
        )
        self.assertEqual("observe", chosen_mode.row.id if chosen_mode else None)

    def test_seed_is_input_order_invariant_and_does_not_touch_global_random(self) -> None:
        rows = [
            corpus_line(id="c", semantic_group="same.c"),
            corpus_line(id="a", semantic_group="same.a"),
            corpus_line(id="b", semantic_group="same.b"),
        ]
        random.seed(9182)
        before = random.getstate()

        first = select_line(rows, context_at(), SelectionHistory(), NOW, seed=44)
        second = select_line(list(reversed(rows)), context_at(), SelectionHistory(), NOW, seed=44)

        self.assertEqual(first.row.id if first else None, second.row.id if second else None)
        self.assertEqual(before, random.getstate())

    def test_different_seeds_reach_weighted_alternatives_in_highest_band(self) -> None:
        high = corpus_line(id="high", semantic_group="choice.high", weight=1.0)
        low = corpus_line(id="low", semantic_group="choice.low", weight=0.5)
        counts = {"high": 0, "low": 0}
        for seed in range(200):
            selected = select_line([low, high], context_at(), SelectionHistory(), NOW, seed=seed)
            self.assertIsNotNone(selected)
            counts[selected.row.id] += 1

        self.assertGreater(counts["high"], counts["low"])
        self.assertGreater(counts["low"], 0)

    def test_low_score_rows_never_leak_into_highest_score_band(self) -> None:
        high = corpus_line(id="high", category_group="character_life")
        low = corpus_line(
            id="low",
            category_group="easter_egg",
            semantic_group="egg.low",
            weight=0.1,
        )
        selected_ids = {
            select_line([low, high], context_at(), SelectionHistory(), NOW, seed=seed).row.id
            for seed in range(50)
        }
        self.assertEqual({"high"}, selected_ids)

    def test_success_appends_exactly_one_complete_record(self) -> None:
        row = corpus_line(trigger="morning", interrupt_cost=1)
        history = SelectionHistory()

        selected = select_line([row], context_at(), history, NOW, seed=0)

        self.assertIsNotNone(selected)
        self.assertEqual(1, len(history.records))
        surface = surface_exposure(row.text)
        self.assertEqual(
            HistoryRecord(
                selected_id=row.id,
                played_at=NOW,
                category=row.category,
                category_group=row.category_group,
                semantic_group=row.semantic_group,
                output_mode=row.output_mode,
                trigger=row.trigger,
                interrupt_cost=row.interrupt_cost,
                was_dry_sharp=False,
                was_seasoning=False,
                surface_opening=surface.opening,
                surface_ending=surface.ending,
                surface_template=surface.template,
            ),
            history.records[0],
        )
        self.assertIsInstance(selected.score, float)
        self.assertIsInstance(selected.score_band, int)

    def test_none_leaves_history_byte_identical(self) -> None:
        history = SelectionHistory()
        before = history.to_json()

        selected = select_line(
            [corpus_line(enabled=False)], context_at(), history, NOW, seed=0
        )

        self.assertIsNone(selected)
        self.assertEqual(before, history.to_json())

    def test_real_expanded_corpus_smoke_and_repeat_are_deterministic(self) -> None:
        rows = load_v2(CORPUS_PATH)
        self.assertGreaterEqual(
            len(rows), EXPANDED_RUNTIME_ROWS[0]
        )
        self.assertLessEqual(
            len(rows), EXPANDED_RUNTIME_ROWS[1]
        )

        first = select_line(prepare_corpus(rows), context_at(), SelectionHistory(), NOW, seed=2026)
        second = select_line(
            prepare_corpus(list(reversed(rows))),
            context_at(),
            SelectionHistory(),
            NOW,
            seed=2026,
        )

        self.assertIsNotNone(first)
        self.assertEqual(first.row.id, second.row.id if second else None)
        self.assertTrue(first.row.enabled)

    def test_real_corpus_multistep_sequence_is_order_invariant_and_not_technical_dominated(self) -> None:
        rows = load_v2(CORPUS_PATH)
        shuffled = list(rows)
        random.Random(73).shuffle(shuffled)

        def run(order: list[CorpusLine]) -> tuple[list[str], Counter[str]]:
            prepared = prepare_corpus(order)
            history = SelectionHistory()
            ids: list[str] = []
            groups: Counter[str] = Counter()
            start = datetime(2026, 1, 1, 8, 0, tzinfo=SHANGHAI)
            for index in range(60):
                now = start + timedelta(minutes=70 * index)
                selected = select_line(
                    prepared,
                    context_at(now, minutes_since_last_output=600 if index == 0 else 70),
                    history,
                    now,
                    seed=10_000 + index,
                )
                self.assertIsNotNone(selected)
                ids.append(selected.row.id)
                groups[selected.row.category_group] += 1
            return ids, groups

        forward_ids, groups = run(rows)
        reverse_ids, _ = run(list(reversed(rows)))
        shuffled_ids, _ = run(shuffled)

        self.assertEqual(forward_ids, reverse_ids)
        self.assertEqual(forward_ids, shuffled_ids)
        self.assertGreater(groups["character_life"], groups["technical"])


if __name__ == "__main__":
    unittest.main()
