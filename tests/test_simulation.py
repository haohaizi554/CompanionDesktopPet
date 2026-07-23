from __future__ import annotations

import hashlib
import json
import random
import tempfile
import unittest
from copy import deepcopy
from dataclasses import replace
from pathlib import Path

from src.persona_corpus.builder import serialize_v2
from src.persona_corpus.loader import load_v2
from src.persona_corpus.simulation import (
    SimulationError,
    render_simulation_report,
    simulate,
    write_editorial_reports,
)
from src.persona_corpus.validation import load_json_object, scheduler_config_sha256
from src.persona_corpus.validation import validate_corpus
from tools.simulate_persona import seed_sequence_from_count


ROOT = Path(__file__).resolve().parents[1]
CORPUS_PATH = ROOT / "data" / "optimized" / "persona-corpus-v2.tsv"
CONFIG_PATH = ROOT / "config" / "persona-scheduler.json"
ARCHIVE_PATH = ROOT / "data" / "optimized" / "persona-corpus-archive.tsv"
REVIEW_PATH = ROOT / "data" / "optimized" / "persona-corpus-review.tsv"
SOURCE_PATH = ROOT / "data" / "source" / "persona-corpus.original.tsv"
PII_PATH = ROOT / "reports" / "pii-review.tsv"

METRIC_ATTRIBUTES = (
    "total_attempts",
    "output_count",
    "none_count",
    "average_outputs_per_day",
    "max_outputs_per_hour",
    "group_ratio",
    "mode_ratio",
    "technical_ratio",
    "easter_egg_ratio",
    "user_direct_ratio",
    "id_cooldown_repeats",
    "semantic_cooldown_repeats",
    "adjacent_same_category_group",
    "adjacent_technical",
    "adjacent_care",
    "average_text_length",
    "length_distribution",
    "common_openings",
    "common_endings",
    "catchphrase_ratio",
    "question_count",
    "unmet_context_count",
    "per_seed_anomalies",
)

EVENT_KEYS = {
    "schema_version",
    "corpus_sha256",
    "scheduler_config_sha256",
    "days",
    "seeds",
    "attempts",
}
ATTEMPT_KEYS = {"seed", "attempted_at", "context", "selected_id"}
CONTEXT_KEYS = {
    "event",
    "daypart",
    "weekday",
    "is_weekend",
    "holiday",
    "anniversary_days",
    "minutes_since_last_output",
    "ide_foreground",
    "active_minutes",
    "idle_return",
    "fullscreen",
}


class SimulationIntegrationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.corpus = load_v2(CORPUS_PATH)
        cls.config = load_json_object(CONFIG_PATH)
        cls.report = simulate(cls.corpus, cls.config, days=30, seeds=range(10))

    def test_thirty_days_ten_seeds_have_no_hard_violations(self) -> None:
        report = self.report
        self.assertEqual([], report.hard_violations)
        self.assertGreaterEqual(report.group_ratio["technical"], 0.10)
        self.assertLessEqual(report.group_ratio["technical"], 0.20)
        self.assertGreaterEqual(
            report.mode_ratio["self_talk"] + report.mode_ratio["ambient"],
            0.65,
        )
        self.assertLessEqual(report.mode_ratio["user_direct"], 0.15)
        self.assertLessEqual(report.group_ratio["easter_egg"], 0.02)
        self.assertEqual(0, report.id_cooldown_repeats)
        self.assertEqual(0, report.semantic_cooldown_repeats)
        self.assertEqual(0, report.unmet_context_count)
        self.assertEqual(0, report.adjacent_technical)
        self.assertEqual(0, report.adjacent_daily_care)
        self.assertEqual(0, report.adjacent_emotional_reflection)

    def test_report_exposes_every_approved_metric(self) -> None:
        for name in METRIC_ATTRIBUTES:
            with self.subTest(metric=name):
                self.assertTrue(hasattr(self.report, name), name)
        self.assertEqual(self.report.total_attempts, self.report.output_count + self.report.none_count)
        self.assertEqual(10, len(self.report.per_seed_anomalies))
        self.assertEqual(set(range(10)), set(self.report.per_seed_anomalies))

    def test_validation_event_payload_is_exact_hash_bound_and_context_complete(self) -> None:
        payload = self.report.to_validation_payload()
        self.assertEqual(EVENT_KEYS, set(payload))
        self.assertEqual(1, payload["schema_version"])
        self.assertEqual(30, payload["days"])
        self.assertEqual(list(range(10)), payload["seeds"])
        self.assertEqual(
            hashlib.sha256(serialize_v2(self.corpus)).hexdigest(),
            payload["corpus_sha256"],
        )
        self.assertEqual(scheduler_config_sha256(self.config), payload["scheduler_config_sha256"])

        attempts = payload["attempts"]
        self.assertTrue(attempts)
        self.assertTrue(all(set(attempt) == ATTEMPT_KEYS for attempt in attempts))
        self.assertTrue(all(set(attempt["context"]) == CONTEXT_KEYS for attempt in attempts))
        contexts = [attempt["context"] for attempt in attempts]
        self.assertEqual(
            {"morning", "noon", "afternoon", "evening", "late_night"},
            {context["daypart"] for context in contexts},
        )
        self.assertTrue(any(context["event"] == "app_start" for context in contexts))
        self.assertTrue(any(context["event"] == "day_changed" for context in contexts))
        self.assertTrue(any(context["is_weekend"] for context in contexts))
        self.assertTrue(any(not context["is_weekend"] for context in contexts))
        self.assertTrue(any(context["holiday"] is not None for context in contexts))
        self.assertTrue(any(context["anniversary_days"] > 0 for context in contexts))
        long_silence = self.config["runtime_limits"]["long_silence_minutes"]
        self.assertTrue(
            any(context["minutes_since_last_output"] >= long_silence for context in contexts)
        )
        for context in contexts:
            self.assertIsNone(context["ide_foreground"])
            self.assertIsNone(context["active_minutes"])
            self.assertIsNone(context["idle_return"])
            self.assertIsNone(context["fullscreen"])

    def test_markdown_and_event_json_are_stable_lf_artifacts(self) -> None:
        first_markdown = render_simulation_report(self.report)
        second_markdown = render_simulation_report(self.report)
        self.assertEqual(first_markdown, second_markdown)
        self.assertTrue(first_markdown.endswith("\n"))
        self.assertNotIn("\r", first_markdown)
        first_events = self.report.to_validation_json()
        second_events = self.report.to_validation_json()
        self.assertEqual(first_events, second_events)
        self.assertTrue(first_events.endswith(b"\n"))
        self.assertNotIn(b"\r", first_events)
        self.assertEqual(EVENT_KEYS, set(json.loads(first_events)))

    def test_per_seed_anomalies_disclose_zero_frequency_groups_without_making_them_hard(self) -> None:
        self.assertEqual([], self.report.hard_violations)
        if self.report.easter_egg_ratio == 0:
            self.assertTrue(
                all(
                    "easter_egg_not_observed" in anomalies
                    for anomalies in self.report.per_seed_anomalies.values()
                )
            )
        if self.report.user_direct_ratio == 0:
            self.assertTrue(
                all(
                    "user_direct_not_observed" in anomalies
                    for anomalies in self.report.per_seed_anomalies.values()
                )
            )

    def test_task_four_independently_accepts_and_detects_tampered_events(self) -> None:
        payload = self.report.to_validation_payload()
        validation = validate_corpus(
            self.corpus,
            self.config,
            {"exceptions": []},
            simulation_result=payload,
        )
        self.assertFalse(validation.errors)

        tampered = deepcopy(payload)
        first_selected = next(
            attempt for attempt in tampered["attempts"] if attempt["selected_id"] is not None
        )
        first_selected["selected_id"] = "not-a-real-enabled-id"
        rejected = validate_corpus(
            self.corpus,
            self.config,
            {"exceptions": []},
            simulation_result=tampered,
        )
        self.assertIn("simulation_unknown_line", {issue.code for issue in rejected.errors})


class SimulationUnitTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.corpus = load_v2(CORPUS_PATH)
        cls.config = load_json_object(CONFIG_PATH)

    def test_small_fixed_seed_run_is_reproducible(self) -> None:
        first = simulate(self.corpus, self.config, days=2, seeds=(7,))
        second = simulate(self.corpus, self.config, days=2, seeds=(7,))
        self.assertEqual(first.to_validation_json(), second.to_validation_json())
        self.assertEqual(render_simulation_report(first), render_simulation_report(second))

    def test_seed_order_and_corpus_order_do_not_change_selected_event_stream(self) -> None:
        forward = simulate(self.corpus, self.config, days=1, seeds=(9, 3))
        reverse = simulate(tuple(reversed(self.corpus)), self.config, days=1, seeds=(3, 9))
        self.assertEqual((3, 9), forward.seeds)
        self.assertEqual((3, 9), reverse.seeds)
        forward_payload = forward.to_validation_payload()
        reverse_payload = reverse.to_validation_payload()
        self.assertEqual(forward_payload["attempts"], reverse_payload["attempts"])

    def test_simulation_does_not_mutate_global_random_state(self) -> None:
        random.seed(20260723)
        before = random.getstate()
        simulate(self.corpus, self.config, days=1, seeds=(0,))
        self.assertEqual(before, random.getstate())

    def test_zero_output_report_has_zero_filled_groups_and_no_division_error(self) -> None:
        disabled = [replace(self.corpus[0], enabled=False)]
        report = simulate(disabled, self.config, days=1, seeds=(0,))
        self.assertEqual(0, report.output_count)
        self.assertEqual(
            {
                "technical",
                "growth",
                "career",
                "daily_care",
                "emotional_reflection",
                "character_life",
                "easter_egg",
                "system_ambient",
            },
            set(report.group_ratio),
        )
        self.assertTrue(all(value == 0 for value in report.group_ratio.values()))
        self.assertEqual(
            {"self_talk", "ambient", "user_direct", "system_observe"},
            set(report.mode_ratio),
        )
        self.assertIn("zero_outputs", report.hard_violations)

    def test_invalid_duration_seeds_and_config_fail_closed(self) -> None:
        with self.assertRaises(SimulationError):
            simulate(self.corpus, self.config, days=0, seeds=(0,))
        with self.assertRaises(SimulationError):
            simulate(self.corpus, self.config, days=1, seeds=())
        with self.assertRaises(SimulationError):
            simulate(self.corpus, self.config, days=1, seeds=(1, 1))
        with self.assertRaises(SimulationError):
            simulate(self.corpus, {"schema_version": 1}, days=1, seeds=(0,))

    def test_cli_seed_count_means_range(self) -> None:
        self.assertEqual(tuple(range(10)), seed_sequence_from_count(10))
        with self.assertRaises(ValueError):
            seed_sequence_from_count(0)
        with self.assertRaises(ValueError):
            seed_sequence_from_count(True)

    def test_editorial_reports_use_real_traceable_evidence_and_meet_minimums(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            summary = write_editorial_reports(
                corpus=self.corpus,
                source_path=SOURCE_PATH,
                archive_path=ARCHIVE_PATH,
                review_path=REVIEW_PATH,
                pii_path=PII_PATH,
                audit_after_path=output / "corpus-audit-after.md",
                rewrite_summary_path=output / "corpus-rewrite-summary.md",
                manual_review_path=output / "corpus-manual-review.md",
            )
            self.assertGreaterEqual(summary.general_rewrite_examples, 50)
            self.assertGreaterEqual(summary.disabled_examples, 20)
            self.assertGreaterEqual(summary.tone_fix_examples, 20)
            self.assertGreaterEqual(summary.fake_context_examples, 20)
            self.assertEqual(3265 + 1248, summary.manual_review_items)
            for path in (
                output / "corpus-audit-after.md",
                output / "corpus-rewrite-summary.md",
                output / "corpus-manual-review.md",
            ):
                payload = path.read_bytes()
                self.assertTrue(payload.endswith(b"\n"))
                self.assertNotIn(b"\r", payload)
                self.assertNotIn(b"TODO: generated report placeholder", payload)
            rewrite = (output / "corpus-rewrite-summary.md").read_text(encoding="utf-8")
            self.assertIn("source_line", rewrite)
            self.assertIn("topic-level rewritten outcome", rewrite)

    def test_editorial_tsv_error_names_path_and_one_based_line(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory)
            bad_archive = output / "bad-archive.tsv"
            bad_archive.write_bytes(b"wrong\theader\n")
            with self.assertRaisesRegex(
                SimulationError,
                rf"{bad_archive.name}.*line 1",
            ):
                write_editorial_reports(
                    corpus=self.corpus,
                    source_path=SOURCE_PATH,
                    archive_path=bad_archive,
                    review_path=REVIEW_PATH,
                    pii_path=PII_PATH,
                    audit_after_path=output / "after.md",
                    rewrite_summary_path=output / "rewrite.md",
                    manual_review_path=output / "manual.md",
                )


if __name__ == "__main__":
    unittest.main()
