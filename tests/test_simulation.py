from __future__ import annotations

import hashlib
import json
import random
import tempfile
import unittest
from copy import deepcopy
from dataclasses import replace
from datetime import datetime, timedelta, timezone
from pathlib import Path

from src.persona_corpus.builder import serialize_v2
from src.persona_corpus.context import PersonaContext
from src.persona_corpus.contract import PERSONA_CONTRACT
from src.persona_corpus.loader import load_v2
from src.persona_corpus.simulation import (
    SUBSEED_DERIVATION_VERSION,
    CandidateIndex,
    DistributionTolerance,
    SimulationAttempt,
    SimulationError,
    analyze_constraints,
    build_scenario_coverage,
    combine_hard_violations,
    derive_subseed,
    derive_distribution_policy,
    derive_dry_sharp_policy,
    probe_inventory_coverage,
    render_simulation_report,
    run_adversarial_suite,
    simulate,
    write_editorial_reports,
)
from src.persona_corpus.selector import SchedulerConfig
from src.persona_corpus.simulation_core.report import analyze_simulation
from src.persona_corpus.validation import (
    CATCHPHRASES,
    load_json_object,
    scheduler_config_sha256,
)
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
    "tone_counts",
    "tone_ratio",
    "dry_sharp_policy",
    "dry_sharp_ratio",
    "dry_sharp_recent_violations",
    "dry_sharp_forbidden_hits",
    "enabled_corpus_count",
    "dry_sharp_inventory_count",
    "dry_sharp_inventory_ratio",
    "dry_sharp_inventory_enforced",
    "dry_sharp_playback_enforced",
    "dry_sharp_inventory_bootstrap_gap",
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
    "subseed_derivation_version",
    "distribution_policy",
    "scenario_coverage",
    "inventory_coverage",
    "natural_hard_violations",
    "adversarial_hard_violations",
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
        self.assertEqual([], report.natural_hard_violations)
        self.assertEqual([], report.adversarial_hard_violations)
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
        self.assertEqual(SUBSEED_DERIVATION_VERSION, self.report.subseed_derivation_version)
        self.assertEqual(108, self.report.scenario_coverage.nullable_signal_combinations)
        self.assertTrue(self.report.inventory_coverage.trigger_hits)
        self.assertEqual((), self.report.inventory_coverage.trigger_misses)
        self.assertEqual((), self.report.inventory_coverage.context_misses)
        self.assertEqual((), self.report.inventory_coverage.unreachable_pairs)

    def test_combined_hard_status_requires_natural_and_adversarial_success(self) -> None:
        self.assertEqual([], combine_hard_violations((), ()))
        self.assertEqual(
            ["natural_failure"],
            combine_hard_violations(("natural_failure",), ()),
        )
        self.assertEqual(
            ["adversarial_failure"],
            combine_hard_violations((), ("adversarial_failure",)),
        )
        self.assertEqual(
            ["adversarial_failure", "natural_failure"],
            combine_hard_violations(
                ("natural_failure",),
                ("adversarial_failure",),
            ),
        )

    def test_combined_care_adjacency_counts_cross_group_pairs(self) -> None:
        care_groups = {"daily_care", "emotional_reflection"}
        expected = 0
        same_group = 0
        for seed in self.report.seeds:
            outputs = [
                attempt
                for attempt in self.report.attempts
                if attempt.seed == seed and attempt.row is not None
            ]
            for previous, current in zip(outputs, outputs[1:]):
                assert previous.row is not None and current.row is not None
                if (
                    previous.row.category_group in care_groups
                    and current.row.category_group in care_groups
                ):
                    expected += 1
                    if previous.row.category_group == current.row.category_group:
                        same_group += 1

        self.assertGreater(expected, same_group)
        self.assertEqual(expected, self.report.adjacent_care)
        self.assertEqual(
            same_group,
            self.report.adjacent_daily_care
            + self.report.adjacent_emotional_reflection,
        )

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
        self.assertIn("Subseed derivation", first_markdown)
        self.assertIn("Natural hard violations", first_markdown)
        self.assertIn("Adversarial hard violations", first_markdown)
        self.assertIn("Selector decision", first_markdown)
        self.assertIn("Inventory trigger misses", first_markdown)
        self.assertIn("dry_sharp playback", first_markdown)
        self.assertIn("dry_sharp inventory", first_markdown)
        first_events = self.report.to_validation_json()
        second_events = self.report.to_validation_json()
        self.assertEqual(first_events, second_events)
        self.assertTrue(first_events.endswith(b"\n"))
        self.assertNotIn(b"\r", first_events)
        self.assertEqual(EVENT_KEYS, set(json.loads(first_events)))

    def test_dry_sharp_report_discloses_inventory_playback_and_constraint_evidence(self) -> None:
        enabled = [row for row in self.corpus if row.enabled]
        inventory_count = sum(row.tone == "dry_sharp" for row in enabled)
        playback_count = sum(
            attempt.row is not None and attempt.row.tone == "dry_sharp"
            for attempt in self.report.attempts
        )
        policy = derive_dry_sharp_policy()

        self.assertEqual(inventory_count, self.report.dry_sharp_inventory_count)
        self.assertAlmostEqual(
            inventory_count / len(enabled) if enabled else 0.0,
            self.report.dry_sharp_inventory_ratio,
        )
        self.assertEqual(playback_count, self.report.tone_counts["dry_sharp"])
        self.assertAlmostEqual(
            playback_count / self.report.output_count if self.report.output_count else 0.0,
            self.report.dry_sharp_ratio,
        )
        self.assertEqual(
            len(enabled) >= policy.inventory_enforcement_minimum_rows,
            self.report.dry_sharp_inventory_enforced,
        )
        self.assertEqual(
            inventory_count < policy.bootstrap_minimum_rows,
            self.report.dry_sharp_inventory_bootstrap_gap,
        )
        self.assertEqual(
            sum(self.report.tone_counts.values()),
            self.report.output_count,
        )
        self.assertEqual(
            sum(self.report.tone_ratio.values()),
            1.0 if self.report.output_count else 0.0,
        )

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

    def test_subseed_v2_is_bound_to_corpus_config_version_and_scenario(self) -> None:
        corpus_sha = "1" * 64
        config_sha = "2" * 64
        baseline = derive_subseed(
            seed=7,
            day_index=3,
            slot_index=2,
            corpus_sha256=corpus_sha,
            scheduler_config_sha256=config_sha,
            scenario="natural:morning",
        )

        self.assertEqual(
            baseline,
            derive_subseed(
                seed=7,
                day_index=3,
                slot_index=2,
                corpus_sha256=corpus_sha,
                scheduler_config_sha256=config_sha,
                scenario="natural:morning",
            ),
        )
        variants = {
            derive_subseed(
                seed=7,
                day_index=3,
                slot_index=2,
                corpus_sha256="3" * 64,
                scheduler_config_sha256=config_sha,
                scenario="natural:morning",
            ),
            derive_subseed(
                seed=7,
                day_index=3,
                slot_index=2,
                corpus_sha256=corpus_sha,
                scheduler_config_sha256="4" * 64,
                scenario="natural:morning",
            ),
            derive_subseed(
                seed=7,
                day_index=3,
                slot_index=2,
                corpus_sha256=corpus_sha,
                scheduler_config_sha256=config_sha,
                scenario="coverage:dawn",
            ),
            derive_subseed(
                seed=7,
                day_index=3,
                slot_index=2,
                corpus_sha256=corpus_sha,
                scheduler_config_sha256=config_sha,
                scenario="natural:morning",
                derivation_version="persona-simulation-v3-test",
            ),
        }
        self.assertEqual(4, len(variants))
        self.assertNotIn(baseline, variants)
        self.assertEqual("persona-simulation-v2", SUBSEED_DERIVATION_VERSION)

    def _constraint_attempt(
        self,
        *,
        when: datetime,
        row_index: int,
        elapsed_minutes: float,
        interrupt_cost: int = 0,
        category_group: str = "character_life",
        output_mode: str = "self_talk",
        tone: str = "calm",
    ) -> SimulationAttempt:
        source = self.corpus[row_index % len(self.corpus)]
        row = replace(
            source,
            id=f"constraint-{row_index}",
            category=f"constraint-category-{row_index}",
            category_group=category_group,
            semantic_group=f"constraint-semantic-{row_index}",
            output_mode=output_mode,
            tone=tone,
            trigger="any",
            required_context="none",
            interrupt_cost=interrupt_cost,
            cooldown_hours=0.01,
            semantic_cooldown_hours=0.01,
            max_per_day=20,
            requires_reply=False,
            enabled=True,
            text=f"constraint fixture {row_index}",
        )
        context = PersonaContext.from_datetime(
            when,
            minutes_since_last_output=elapsed_minutes,
        )
        return SimulationAttempt(seed=0, attempted_at=when, context=context, row=row)

    def test_constraint_checker_enforces_7m59s_but_allows_exact_8m_boundary(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        first = self._constraint_attempt(
            when=start,
            row_index=0,
            elapsed_minutes=1440,
        )
        below = self._constraint_attempt(
            when=start + timedelta(minutes=7, seconds=59),
            row_index=1,
            elapsed_minutes=7 + 59 / 60,
        )
        exact = self._constraint_attempt(
            when=start + timedelta(minutes=8),
            row_index=2,
            elapsed_minutes=8,
        )

        self.assertIn(
            "minimum_interval_violation",
            analyze_constraints((first, below), scheduler).codes,
        )
        self.assertNotIn(
            "minimum_interval_violation",
            analyze_constraints((first, exact), scheduler).codes,
        )

    def test_constraint_checker_uses_each_interrupt_cost_exact_boundary(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        for cost, required in sorted(
            scheduler.interrupt_cost_minimum_intervals_minutes.items()
        ):
            if cost == 0:
                continue
            with self.subTest(cost=cost, required=required):
                first = self._constraint_attempt(
                    when=start,
                    row_index=cost * 10,
                    elapsed_minutes=1440,
                )
                below = self._constraint_attempt(
                    when=start + timedelta(minutes=required, seconds=-1),
                    row_index=cost * 10 + 1,
                    elapsed_minutes=required - 1 / 60,
                    interrupt_cost=cost,
                )
                exact = self._constraint_attempt(
                    when=start + timedelta(minutes=required),
                    row_index=cost * 10 + 2,
                    elapsed_minutes=required,
                    interrupt_cost=cost,
                )
                self.assertIn(
                    "interrupt_budget_violation",
                    analyze_constraints((first, below), scheduler).codes,
                )
                self.assertNotIn(
                    "interrupt_budget_violation",
                    analyze_constraints((first, exact), scheduler).codes,
                )

    def test_constraint_checker_honours_any_configured_blocked_group(self) -> None:
        scheduler = replace(
            SchedulerConfig.from_mapping(self.config),
            block_adjacent_category_groups=frozenset({"character_life"}),
        )
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        attempts = (
            self._constraint_attempt(
                when=start,
                row_index=70,
                elapsed_minutes=1440,
                category_group="character_life",
            ),
            self._constraint_attempt(
                when=start + timedelta(minutes=8),
                row_index=71,
                elapsed_minutes=8,
                category_group="character_life",
            ),
        )

        result = analyze_constraints(attempts, scheduler)
        self.assertIn("adjacent_group_violation:character_life", result.codes)

    def test_constraint_checker_detects_rolling_hour_and_late_night_budgets(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        start = datetime(2026, 7, 1, 1, 0, tzinfo=timezone(timedelta(hours=8)))
        attempts = tuple(
            self._constraint_attempt(
                when=start + timedelta(minutes=8 * index),
                row_index=100 + index,
                elapsed_minutes=1440 if index == 0 else 8,
            )
            for index in range(3)
        )

        codes = analyze_constraints(attempts, scheduler).codes
        self.assertIn("hourly_budget_violation", codes)
        self.assertIn("late_night_budget_violation", codes)

        forged = tuple(
            replace(attempt, context=replace(attempt.context, daypart="morning"))
            for attempt in attempts
        )
        self.assertIn(
            "late_night_budget_violation",
            analyze_constraints(forged, scheduler).codes,
        )

    def test_constraint_checker_detects_daily_and_recent_window_quotas(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))

        first = self._constraint_attempt(
            when=start,
            row_index=200,
            elapsed_minutes=1440,
        )
        assert first.row is not None
        repeated = replace(
            self._constraint_attempt(
                when=start + timedelta(minutes=8),
                row_index=201,
                elapsed_minutes=8,
            ),
            row=replace(first.row, max_per_day=1),
        )
        first = replace(first, row=replace(first.row, max_per_day=1))
        self.assertIn(
            "max_per_day_violation",
            analyze_constraints((first, repeated), scheduler).codes,
        )

        groups = (
            "technical",
            "character_life",
            "technical",
            "character_life",
            "technical",
        )
        technical = tuple(
            self._constraint_attempt(
                when=start + timedelta(minutes=61 * index),
                row_index=220 + index,
                elapsed_minutes=1440 if index == 0 else 61,
                category_group=group,
            )
            for index, group in enumerate(groups)
        )
        self.assertIn(
            "recent_technical_violation",
            analyze_constraints(technical, scheduler).codes,
        )

        modes = ("user_direct", "ambient", "user_direct", "ambient", "user_direct")
        directed = tuple(
            self._constraint_attempt(
                when=start + timedelta(minutes=61 * index),
                row_index=240 + index,
                elapsed_minutes=1440 if index == 0 else 61,
                output_mode=mode,
            )
            for index, mode in enumerate(modes)
        )
        self.assertIn(
            "recent_user_direct_violation",
            analyze_constraints(directed, scheduler).codes,
        )

        easter = tuple(
            self._constraint_attempt(
                when=start + timedelta(minutes=61 * index),
                row_index=260 + index,
                elapsed_minutes=1440 if index == 0 else 61,
                category_group=group,
            )
            for index, group in enumerate(
                ("easter_egg", "character_life", "easter_egg")
            )
        )
        self.assertIn(
            "recent_easter_egg_violation",
            analyze_constraints(easter, scheduler).codes,
        )

    def test_constraint_checker_enforces_dry_sharp_recent_window(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        policy = derive_dry_sharp_policy()
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        attempts = tuple(
            self._constraint_attempt(
                when=start + timedelta(minutes=61 * index),
                row_index=300 + index,
                elapsed_minutes=1440 if index == 0 else 61,
                tone="dry_sharp" if index in {0, policy.recent_window - 1} else "calm",
            )
            for index in range(policy.recent_window)
        )

        self.assertIn(
            "recent_dry_sharp_violation",
            analyze_constraints(attempts, scheduler).codes,
        )
        self.assertNotIn(
            "recent_dry_sharp_violation",
            analyze_constraints(attempts[1:], scheduler).codes,
        )

    def test_constraint_checker_enforces_dry_sharp_metadata_policy(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        policy = derive_dry_sharp_policy()
        when = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        allowed = self._constraint_attempt(
            when=when,
            row_index=330,
            elapsed_minutes=1440,
            tone="dry_sharp",
        )
        self.assertNotIn(
            "dry_sharp_forbidden_metadata_violation",
            analyze_constraints((allowed,), scheduler).codes,
        )

        assert allowed.row is not None
        for group in sorted(policy.forbidden_category_groups):
            with self.subTest(category_group=group):
                attempt = replace(allowed, row=replace(allowed.row, category_group=group))
                self.assertIn(
                    "dry_sharp_forbidden_metadata_violation",
                    analyze_constraints((attempt,), scheduler).codes,
                )
        for trigger in sorted(policy.forbidden_triggers):
            with self.subTest(trigger=trigger):
                attempt = replace(allowed, row=replace(allowed.row, trigger=trigger))
                self.assertIn(
                    "dry_sharp_forbidden_metadata_violation",
                    analyze_constraints((attempt,), scheduler).codes,
                )
        for token in sorted(policy.forbidden_context_tokens):
            with self.subTest(required_context=token):
                attempt = replace(
                    allowed,
                    row=replace(allowed.row, required_context=token),
                )
                self.assertIn(
                    "dry_sharp_forbidden_metadata_violation",
                    analyze_constraints((attempt,), scheduler).codes,
                )

    def test_constraint_checker_retains_context_question_and_cooldown_gates(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        first = self._constraint_attempt(
            when=start,
            row_index=280,
            elapsed_minutes=1440,
        )
        assert first.row is not None
        invalid = self._constraint_attempt(
            when=start + timedelta(minutes=8),
            row_index=281,
            elapsed_minutes=8,
        )
        assert invalid.row is not None
        invalid = replace(
            invalid,
            row=replace(
                invalid.row,
                id=first.row.id,
                trigger="afternoon",
                required_context="ide_foreground",
                cooldown_hours=24,
                text="question fixture?",
            ),
        )

        codes = analyze_constraints((first, invalid), scheduler).codes
        self.assertIn("context_or_trigger_violation", codes)
        self.assertIn("question_or_reply_violation", codes)
        self.assertIn("id_cooldown_violation", codes)

    def test_adversarial_suite_proves_every_configured_constraint_boundary(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        result = run_adversarial_suite(scheduler)
        by_name = {case.name: case for case in result.cases}

        self.assertEqual((), result.hard_violations)
        self.assertTrue(all(case.selector_checked for case in result.cases))
        self.assertTrue(
            all(
                case.selector_selected is case.selector_expected_selected
                for case in result.cases
            )
        )
        self.assertIn("minimum_interval:7m59s:reject", by_name)
        self.assertIn("minimum_interval:8m00s:allow", by_name)
        for cost, minutes in sorted(
            scheduler.interrupt_cost_minimum_intervals_minutes.items()
        ):
            if cost == 0:
                continue
            self.assertIn(f"interrupt_cost:{cost}:{minutes}m:reject_below", by_name)
            self.assertIn(f"interrupt_cost:{cost}:{minutes}m:allow_exact", by_name)
        self.assertIn("rolling_hour:max:reject", by_name)
        self.assertIn("late_night:max:reject", by_name)
        self.assertIn("max_per_day:reject", by_name)
        self.assertIn("recent:technical:reject", by_name)
        self.assertIn("recent:user_direct:reject", by_name)
        self.assertIn("recent:easter_egg:reject", by_name)
        for group in scheduler.block_adjacent_category_groups:
            self.assertIn(f"adjacent_group:{group}:reject", by_name)

    def test_scenario_matrix_covers_calendar_dayparts_and_nullable_signals(self) -> None:
        coverage = build_scenario_coverage()

        self.assertEqual(("spring", "summer", "autumn", "winter"), coverage.seasons)
        self.assertEqual(
            ("late_night", "morning", "noon", "afternoon", "evening"),
            coverage.dayparts,
        )
        self.assertTrue(coverage.dawn)
        self.assertEqual(("tick", "app_start", "day_changed"), coverage.events)
        self.assertEqual((False, True), coverage.weekend_values)
        self.assertTrue(coverage.holiday)
        self.assertTrue(coverage.anniversary)
        self.assertTrue(coverage.month_boundary)
        self.assertEqual((None, False, True), coverage.ide_foreground_values)
        self.assertEqual((None, 89, 90, 91), coverage.active_minutes_values)
        self.assertEqual((None, False, True), coverage.idle_return_values)
        self.assertEqual((None, False, True), coverage.fullscreen_values)
        self.assertEqual(108, coverage.nullable_signal_combinations)

    def test_candidate_index_builds_once_and_is_reused_by_queries(self) -> None:
        class CountingRows:
            def __init__(self, rows):
                self.rows = rows
                self.yield_count = 0

            def __iter__(self):
                for row in self.rows:
                    self.yield_count += 1
                    yield row

        scheduler = SchedulerConfig.from_mapping(self.config)
        source = CountingRows(self.corpus)
        index = CandidateIndex.build(source)
        self.assertEqual(len(self.corpus), source.yield_count)

        now = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))
        context = PersonaContext.from_datetime(
            now,
            minutes_since_last_output=1440,
            ide_foreground=True,
            active_minutes=91,
            idle_return=True,
            fullscreen=False,
        )
        first = index.candidates_for(context, now, scheduler)
        second = index.candidates_for(context, now, scheduler)

        self.assertTrue(first)
        self.assertEqual(first, second)
        self.assertEqual(len(self.corpus), source.yield_count)

    def test_inventory_coverage_selects_every_stocked_trigger_and_context(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        reachable = tuple(
            replace(row, trigger="late_night")
            if row.required_context == "time:dawn"
            else row
            for row in self.corpus
        )

        coverage = probe_inventory_coverage(reachable, scheduler)

        self.assertEqual((), coverage.trigger_misses)
        self.assertEqual((), coverage.context_misses)
        self.assertEqual(
            {row.trigger for row in reachable if row.enabled},
            set(coverage.trigger_hits),
        )
        self.assertEqual(
            {row.required_context for row in reachable if row.enabled},
            set(coverage.context_hits),
        )

    def test_distribution_bounds_follow_scheduler_weights_with_one_tolerance(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        tolerance = DistributionTolerance(absolute=0.05)
        policy = derive_distribution_policy(scheduler, tolerance)

        acceptance = PERSONA_CONTRACT.scheduler["acceptance"]
        self.assertEqual(
            tuple(acceptance["technical_playback_ratio"]),
            (policy.technical.minimum, policy.technical.maximum),
        )
        self.assertEqual(
            tuple(acceptance["easter_egg_playback_ratio"]),
            (policy.easter_egg.minimum, policy.easter_egg.maximum),
        )
        self.assertAlmostEqual(0.65, policy.self_ambient.minimum)
        self.assertAlmostEqual(0.15, policy.user_direct.maximum)

        injected = derive_distribution_policy(
            scheduler,
            tolerance,
            acceptance={
                "technical_playback_ratio": (0.11, 0.22),
                "easter_egg_playback_ratio": (0.09, 0.13),
                "self_talk_ambient_minimum": 0.66,
                "user_direct_maximum": 0.16,
            },
        )
        self.assertEqual((0.09, 0.13), (injected.easter_egg.minimum, injected.easter_egg.maximum))

    def test_dry_sharp_policy_is_loaded_from_shared_contract(self) -> None:
        policy = derive_dry_sharp_policy()
        contract = PERSONA_CONTRACT.dry_sharp

        self.assertEqual(contract["inventory_target"], policy.inventory_target)
        self.assertEqual(tuple(contract["inventory_acceptance"]), policy.inventory_acceptance)
        self.assertEqual(
            contract["inventory_enforcement_minimum_rows"],
            policy.inventory_enforcement_minimum_rows,
        )
        self.assertEqual(contract["bootstrap_minimum_rows"], policy.bootstrap_minimum_rows)
        self.assertEqual(contract["playback_target"], policy.playback_target)
        self.assertEqual(tuple(contract["playback_acceptance"]), policy.playback_acceptance)
        self.assertEqual(contract["recent_window"], policy.recent_window)
        self.assertEqual(contract["recent_max"], policy.recent_max)
        self.assertEqual(
            frozenset(contract["forbidden_category_groups"]),
            policy.forbidden_category_groups,
        )
        self.assertEqual(
            frozenset(contract["forbidden_triggers"]),
            policy.forbidden_triggers,
        )
        self.assertEqual(
            frozenset(contract["forbidden_context_tokens"]),
            policy.forbidden_context_tokens,
        )

    def test_dry_sharp_inventory_threshold_controls_hard_enforcement(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        base = simulate(self.corpus, self.config, days=1, seeds=(0,))

        def report_for(enabled_rows: int):
            return analyze_simulation(
                corpus_sha256=base.corpus_sha256,
                enabled_corpus_count=enabled_rows,
                dry_sharp_inventory_count=0,
                config_sha256=base.scheduler_config_sha256,
                config=scheduler,
                days=30,
                seeds=base.seeds,
                attempts=base.attempts,
                distribution_tolerance=DistributionTolerance(),
                scenario_coverage=base.scenario_coverage,
                inventory_coverage=base.inventory_coverage,
                adversarial_result=base.adversarial_result,
            )

        minimum = derive_dry_sharp_policy().inventory_enforcement_minimum_rows
        bootstrap = report_for(minimum - 1)
        enforced = report_for(minimum)

        self.assertFalse(bootstrap.dry_sharp_inventory_enforced)
        self.assertNotIn(
            "dry_sharp_inventory_ratio_out_of_bounds",
            bootstrap.natural_hard_violations,
        )
        self.assertNotIn(
            "dry_sharp_ratio_out_of_bounds",
            bootstrap.natural_hard_violations,
        )
        self.assertTrue(enforced.dry_sharp_inventory_enforced)
        self.assertIn(
            "dry_sharp_inventory_ratio_out_of_bounds",
            enforced.natural_hard_violations,
        )
        self.assertIn(
            "dry_sharp_ratio_out_of_bounds",
            enforced.natural_hard_violations,
        )
        self.assertIn(
            "seed_0:dry_sharp_ratio_out_of_bounds",
            enforced.natural_hard_violations,
        )

    def test_playback_catchphrase_hard_limit_allows_ten_percent_only(self) -> None:
        scheduler = SchedulerConfig.from_mapping(self.config)
        base = simulate(self.corpus, self.config, days=1, seeds=(0,))
        start = datetime(2026, 7, 1, 8, 0, tzinfo=timezone(timedelta(hours=8)))

        def report_for(catchphrase_outputs: int):
            attempts = []
            for index in range(10):
                attempt = self._constraint_attempt(
                    when=start + timedelta(minutes=61 * index),
                    row_index=380 + index,
                    elapsed_minutes=1440 if index == 0 else 61,
                )
                assert attempt.row is not None
                text = (
                    f"{CATCHPHRASES[1]} playback fixture {index}"
                    if index < catchphrase_outputs
                    else f"plain playback fixture {index}"
                )
                attempts.append(replace(attempt, row=replace(attempt.row, text=text)))
            return analyze_simulation(
                corpus_sha256=base.corpus_sha256,
                enabled_corpus_count=len(self.corpus),
                dry_sharp_inventory_count=0,
                config_sha256=base.scheduler_config_sha256,
                config=scheduler,
                days=30,
                seeds=(0,),
                attempts=tuple(attempts),
                distribution_tolerance=DistributionTolerance(),
                scenario_coverage=base.scenario_coverage,
                inventory_coverage=base.inventory_coverage,
                adversarial_result=base.adversarial_result,
            )

        exact = report_for(1)
        above = report_for(2)

        self.assertAlmostEqual(0.10, exact.catchphrase_ratio)
        self.assertNotIn(
            "catchphrase_ratio_above_limit",
            exact.natural_hard_violations,
        )
        self.assertAlmostEqual(0.20, above.catchphrase_ratio)
        self.assertIn(
            "catchphrase_ratio_above_limit",
            above.natural_hard_violations,
        )

    def test_seed_order_is_canonical_but_corpus_order_changes_replay_anchor(self) -> None:
        forward = simulate(self.corpus, self.config, days=1, seeds=(9, 3))
        reverse = simulate(self.corpus, self.config, days=1, seeds=(3, 9))
        reordered = simulate(tuple(reversed(self.corpus)), self.config, days=1, seeds=(3, 9))
        self.assertEqual((3, 9), forward.seeds)
        self.assertEqual((3, 9), reverse.seeds)
        forward_payload = forward.to_validation_payload()
        reverse_payload = reverse.to_validation_payload()
        self.assertEqual(forward_payload["attempts"], reverse_payload["attempts"])
        self.assertNotEqual(forward.corpus_sha256, reordered.corpus_sha256)

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
