from __future__ import annotations

import hashlib
import json
import math
import subprocess
import sys
import tempfile
import unittest
from dataclasses import replace
from pathlib import Path

from src.persona_corpus.builder import serialize_v2
from src.persona_corpus.loader import load_v2
from src.persona_corpus.models import CorpusLine
from src.persona_corpus.schema import V2_HEADER
from src.persona_corpus.validation import (
    VALIDATION_GROUPS,
    ValidationInputError,
    load_json_object,
    normalized_text_sha256,
    validate_config,
    validate_corpus,
    validate_file,
)


ROOT = Path(__file__).resolve().parents[1]
CORPUS_PATH = ROOT / "data/optimized/persona-corpus-v2.tsv"
CONFIG_PATH = ROOT / "config/persona-scheduler.json"
ALLOWLIST_PATH = ROOT / "config/persona-review-allowlist.json"

GROUP_WEIGHTS = {
    "technical": 0.18,
    "growth": 0.10,
    "career": 0.07,
    "daily_care": 0.10,
    "emotional_reflection": 0.08,
    "character_life": 0.35,
    "easter_egg": 0.02,
    "system_ambient": 0.10,
}

OUTPUT_MODE_TARGETS = {
    "self_talk": 0.45,
    "ambient": 0.25,
    "user_direct": 0.10,
    "system_observe": 0.20,
}

CONTEXT_TOKENS = [
    "none",
    "app_started",
    "holiday",
    "anniversary",
    "ide_foreground",
    "active_90m",
    "idle_return",
    "not_fullscreen",
    "day:weekday",
    "day:weekend",
    "time:dawn",
    "time:morning",
    "time:noon",
    "time:afternoon",
    "time:evening",
    "time:late_night",
    "season:spring",
    "season:summer",
    "season:autumn",
    "season:winter",
    "date:holiday",
    "date:month_boundary",
]


def valid_config(**weight_overrides: float) -> dict[str, object]:
    weights = dict(GROUP_WEIGHTS)
    weights.update(weight_overrides)
    return {
        "schema_version": 1,
        "category_group_weights": weights,
        "output_mode_targets": dict(OUTPUT_MODE_TARGETS),
        "runtime_limits": {
            "minimum_interval_minutes": 8,
            "max_outputs_per_hour": 2,
            "late_night_max_outputs_per_hour": 1,
            "semantic_group_no_repeat": True,
            "block_adjacent_category_groups": [
                "technical",
                "daily_care",
                "emotional_reflection",
            ],
            "technical_recent_window": 5,
            "technical_recent_max": 2,
            "user_direct_recent_window": 10,
            "user_direct_recent_max": 2,
            "easter_egg_recent_window": 50,
            "easter_egg_recent_max": 1,
            "interrupt_cost_minimum_intervals_minutes": {
                "0": 8,
                "1": 12,
                "2": 16,
                "3": 24,
                "4": 40,
                "5": 60,
            },
        },
        "context_tokens": list(CONTEXT_TOKENS),
        "mvp_triggers": [
            "any",
            "app_start",
            "morning",
            "noon",
            "afternoon",
            "evening",
            "late_night",
            "day_changed",
            "weekday",
            "weekend",
            "holiday",
            "anniversary",
            "long_silence",
        ],
        "future_triggers": [
            "ide_foreground",
            "long_active",
            "idle_return",
            "story_timer",
        ],
    }


def valid_line(**overrides: object) -> CorpusLine:
    values: dict[str, object] = {
        "id": "v2_fixture_001",
        "category": "WanderingLife",
        "category_group": "character_life",
        "topic_id": "fixture.window",
        "semantic_group": "character_life.fixture.window",
        "output_mode": "self_talk",
        "trigger": "any",
        "required_context": "none",
        "tone": "gentle",
        "interrupt_cost": 0,
        "cooldown_hours": 24.0,
        "semantic_cooldown_hours": 48.0,
        "max_per_day": 1,
        "weight": 1.0,
        "requires_reply": False,
        "enabled": True,
        "text": "窗边的风慢慢绕过书页，房间也安静下来。",
        "source_kind": "curated_standalone",
        "source_reference": "catalog:test-fixture",
        "rewrite_reason": "no_rewrite",
    }
    values.update(overrides)
    return CorpusLine(**values)  # type: ignore[arg-type]


def issue_codes(report) -> set[str]:
    return {issue.code for issue in report.errors}


def bound_allowlist(
    rows: list[CorpusLine] | tuple[CorpusLine, ...],
    exceptions: list[dict[str, str]] | None = None,
) -> dict[str, object]:
    return {
        "schema_version": 1,
        "corpus_sha256": hashlib.sha256(serialize_v2(rows)).hexdigest(),
        "exceptions": [] if exceptions is None else exceptions,
    }


def clean_simulation() -> dict[str, object]:
    groups = (
        ["technical"] * 15
        + ["easter_egg"]
        + ["character_life"] * 34
        + ["growth"] * 10
        + ["career"] * 8
        + ["daily_care"] * 10
        + ["emotional_reflection"] * 8
        + ["system_ambient"] * 14
    )
    modes = (
        ["self_talk"] * 48
        + ["ambient"] * 24
        + ["user_direct"] * 10
        + ["system_observe"] * 18
    )
    plays = [
        {
            "seed": index % 10,
            "category_group": groups[index],
            "output_mode": modes[index],
            "question": False,
            "required_context_violation": False,
            "id_cooldown_violation": False,
            "semantic_cooldown_violation": False,
            "adjacent_group_violation": False,
        }
        for index in range(100)
    ]
    return {
        "days": 30,
        "seeds": list(range(10)),
        "hard_violations": [],
        "plays": plays,
        "metrics": {
            "actual_output_count": 100,
            "category_group_ratio": {
                "technical": 0.15,
                "growth": 0.10,
                "career": 0.08,
                "daily_care": 0.10,
                "emotional_reflection": 0.08,
                "character_life": 0.34,
                "easter_egg": 0.01,
                "system_ambient": 0.14,
            },
            "output_mode_ratio": {
                "self_talk": 0.48,
                "ambient": 0.24,
                "user_direct": 0.10,
                "system_observe": 0.18,
            },
            "id_cooldown_violations": 0,
            "semantic_cooldown_violations": 0,
            "required_context_violations": 0,
            "adjacent_technical": 0,
            "adjacent_daily_care": 0,
            "adjacent_emotional_reflection": 0,
            "question_count": 0,
        },
    }


class ValidationContractTests(unittest.TestCase):
    def test_all_twenty_seven_authoritative_groups_are_named_once(self) -> None:
        self.assertEqual(27, len(VALIDATION_GROUPS))
        self.assertEqual(tuple(range(1, 28)), tuple(number for number, _ in VALIDATION_GROUPS))
        self.assertEqual(27, len({name for _, name in VALIDATION_GROUPS}))

    def test_validator_rejects_question_fake_context_and_duplicate(self) -> None:
        rows = [
            valid_line(id="a", text="你现在累不累？"),
            valid_line(id="b", text="你现在累不累？"),
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue({"question", "fake_context", "duplicate_text"} <= codes)

    def test_exact_and_nfkc_punctuation_normalized_duplicates_are_distinct_gates(self) -> None:
        rows = [
            valid_line(id="a", text="慢慢来，事情总会清楚。"),
            valid_line(id="b", text="慢慢来, 事情总会清楚!"),
            valid_line(id="c", text="慢慢来，事情总会清楚。"),
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertIn("duplicate_text", codes)
        self.assertIn("duplicate_normalized_text", codes)

    def test_normalization_casefolds_and_removes_zero_width_format_characters(self) -> None:
        rows = [
            valid_line(id="a", text="API\u200b 排查要留痕。"),
            valid_line(id="b", text="api排查要留痕!"),
            valid_line(id="empty", text="。？！\u200b"),
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertIn("duplicate_normalized_text", codes)
        self.assertIn("normalized_text_empty", codes)

    def test_ids_and_required_fields_are_strict(self) -> None:
        rows = [valid_line(id="same"), valid_line(id="same", semantic_group="")]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertIn("duplicate_id", codes)
        self.assertIn("required_field", codes)

    def test_output_trigger_tone_group_and_source_kind_enums_are_strict(self) -> None:
        rows = [
            valid_line(
                output_mode="chat",
                trigger="sometimes",
                tone="loud",
                category_group="misc",
                source_kind="generated",
            )
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue(
            {
                "invalid_output_mode",
                "invalid_trigger",
                "invalid_tone",
                "invalid_category_group",
                "invalid_source_kind",
            }
            <= codes
        )

    def test_numeric_ranges_reject_bool_nan_infinity_and_oversized_weights(self) -> None:
        rows = [
            valid_line(
                interrupt_cost=True,
                cooldown_hours=math.nan,
                semantic_cooldown_hours=math.inf,
                max_per_day=False,
                weight=20.0,
            )
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue(
            {
                "invalid_interrupt_cost",
                "invalid_cooldown",
                "invalid_semantic_cooldown",
                "invalid_max_per_day",
                "invalid_weight",
            }
            <= codes
        )

    def test_cooldown_daily_weight_and_boolean_bounds(self) -> None:
        rows = [
            valid_line(
                cooldown_hours=0,
                semantic_cooldown_hours=0,
                max_per_day=3,
                weight=0,
                requires_reply=True,
                enabled="true",
            )
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue(
            {
                "invalid_cooldown",
                "invalid_semantic_cooldown",
                "invalid_max_per_day",
                "invalid_weight",
                "invalid_boolean",
            }
            <= codes
        )

    def test_enabled_reply_requirement_and_both_question_marks_are_rejected(self) -> None:
        rows = [
            valid_line(id="a", requires_reply=True, text="要回答吗。"),
            valid_line(id="b", text="真的可以?"),
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertIn("requires_reply", codes)
        self.assertIn("question", codes)

    def test_original_text_control_characters_are_rejected_before_normalization(self) -> None:
        rows = [valid_line(text="一行里不能有\t制表符和\n换行。")]
        self.assertIn(
            "control_character",
            issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []})),
        )

    def test_required_context_uses_comma_tokens_grammar_and_whitelist(self) -> None:
        rows = [
            valid_line(id="a", required_context="time:morning,not_fullscreen"),
            valid_line(id="b", required_context="none,holiday"),
            valid_line(id="c", required_context="__import__('os')"),
            valid_line(id="d", required_context="secret_signal"),
        ]
        errors = validate_corpus(rows, valid_config(), {"exceptions": []}).errors
        bad_ids = {issue.line_id for issue in errors if issue.code == "invalid_required_context"}
        self.assertEqual({"b", "c", "d"}, bad_ids)

    def test_trigger_and_context_cannot_make_mutually_exclusive_time_claims(self) -> None:
        rows = [
            valid_line(id="bad-time", trigger="morning", required_context="time:evening"),
            valid_line(id="bad-day", trigger="weekday", required_context="day:weekend"),
            valid_line(id="good", trigger="morning", required_context="time:morning"),
        ]
        conflict_ids = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "trigger_context_conflict"
        }
        self.assertEqual({"bad-time", "bad-day"}, conflict_ids)

    def test_user_direct_state_assertion_requires_a_real_context_gate(self) -> None:
        unsafe = valid_line(output_mode="user_direct", text="你现在该停下来了。")
        safe = replace(
            unsafe,
            id="safe",
            required_context="active_90m",
            trigger="long_active",
        )
        unsafe_codes = issue_codes(validate_corpus([unsafe], valid_config(), {"exceptions": []}))
        safe_codes = issue_codes(validate_corpus([safe], valid_config(), {"exceptions": []}))
        self.assertTrue({"fake_context", "user_direct_context"} <= unsafe_codes)
        self.assertNotIn("user_direct_context", safe_codes)

    def test_enabled_pii_phone_identity_name_location_and_income_are_rejected(self) -> None:
        rows = [
            valid_line(id="phone", text="联系号码是 13800138000。"),
            valid_line(id="id", text="证件号是 11010519491231002X。"),
            valid_line(id="name", text="雷琳玥以前在广东打零工，月薪两三千。"),
        ]
        pii_ids = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "pii_enabled"
        }
        self.assertEqual({"phone", "id", "name"}, pii_ids)

    def test_generic_hunan_food_and_salary_field_are_not_personal_pii(self) -> None:
        rows = [
            valid_line(id="food", text="我偶尔会想吃湖南菜，那股辣味很有层次。"),
            valid_line(id="field", text="工资字段最好统一用整数分保存。"),
        ]
        pii_ids = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "pii_enabled"
        }
        self.assertEqual(set(), pii_ids)

    def test_easter_egg_requires_long_cooldown_one_per_day_and_low_row_weight(self) -> None:
        row = valid_line(
            category="EasterEgg",
            category_group="easter_egg",
            cooldown_hours=24,
            semantic_cooldown_hours=24,
            max_per_day=2,
            weight=1.0,
        )
        codes = issue_codes(validate_corpus([row], valid_config(), {"exceptions": []}))
        self.assertTrue(
            {"easter_egg_cooldown", "easter_egg_daily_limit", "easter_egg_row_weight"}
            <= codes
        )

    def test_rare_easter_egg_requires_720_hours(self) -> None:
        row = valid_line(
            category="EasterEgg",
            category_group="easter_egg",
            semantic_group="easter_egg.rare_memory",
            cooldown_hours=168,
            semantic_cooldown_hours=168,
            max_per_day=1,
            weight=0.1,
        )
        self.assertIn(
            "easter_egg_cooldown",
            issue_codes(validate_corpus([row], valid_config(), {"exceptions": []})),
        )

    def test_high_interrupt_and_strong_emotion_cannot_have_high_weight(self) -> None:
        rows = [
            valid_line(id="alert", interrupt_cost=4, weight=1.0),
            valid_line(
                id="emotion",
                category="EmotionalSupport",
                category_group="emotional_reflection",
                tone="intimate",
                text="我会永远陪着你，绝对不会离开。",
                weight=1.0,
            ),
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue({"high_cost_weight", "high_emotion_weight"} <= codes)

    def test_technical_current_object_phrases_are_fake_context(self) -> None:
        rows = [
            valid_line(
                id="lower",
                category="Debugging",
                category_group="technical",
                text="这个 bug 先看调用链。",
            ),
            valid_line(
                id="upper",
                category="Debugging",
                category_group="technical",
                text="这个 BUG 先看调用链。",
            ),
        ]
        offenders = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "technical_fake_context"
        }
        self.assertEqual({"lower", "upper"}, offenders)

    def test_explicit_prefix_core_suffix_lineage_is_rejected(self) -> None:
        row = valid_line(source_reference="prefix:p1;core:c1;suffix:s1")
        self.assertIn(
            "cartesian_signature",
            issue_codes(validate_corpus([row], valid_config(), {"exceptions": []})),
        )

    def test_complete_text_cartesian_grid_is_rejected(self) -> None:
        rows = []
        for p_index, prefix in enumerate(("清晨想想", "午后想想", "夜里想想")):
            for s_index, suffix in enumerate(("也挺好呀。", "就够啦。", "别着急。")):
                rows.append(
                    valid_line(
                        id=f"grid_{p_index}_{s_index}",
                        topic_id="fixture.grid",
                        semantic_group=f"fixture.grid.{p_index}.{s_index}",
                        text=f"{prefix}窗外的云{suffix}",
                    )
                )
        self.assertIn(
            "cartesian_signature",
            issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []})),
        )

    def test_minimum_two_by_two_by_two_cartesian_grid_is_rejected(self) -> None:
        rows = []
        for p_index, prefix in enumerate(("清晨", "夜里")):
            for c_index, core in enumerate(("看看云", "听听雨")):
                for s_index, suffix in enumerate(("也挺好。", "就够啦。")):
                    rows.append(
                        valid_line(
                            id=f"cube_{p_index}_{c_index}_{s_index}",
                            topic_id="fixture.cube",
                            semantic_group=f"fixture.cube.{p_index}.{c_index}.{s_index}",
                            text=f"{prefix}{core}{suffix}",
                        )
                    )
        self.assertIn(
            "cartesian_signature",
            issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []})),
        )

    def test_natural_shared_ending_without_product_is_not_cartesian(self) -> None:
        rows = [
            valid_line(
                id=f"natural_{index}",
                topic_id="fixture.natural",
                semantic_group=f"fixture.natural.{index}",
                text=text,
            )
            for index, text in enumerate(
                (
                    "雨停之后街灯显得更亮，也挺好。",
                    "旧书翻到熟悉的一页，慢一点也挺好。",
                    "茶凉之前记完这句话，安静些也挺好。",
                    "窗帘被风吹起一角，坐一会儿也挺好。",
                    "云从楼顶慢慢挪开，看着也挺好。",
                    "鞋带重新系紧再出门，稳一点也挺好。",
                    "桌面收出一小块空地，清爽些也挺好。",
                    "晚饭只做简单一碗面，热乎就挺好。",
                )
            )
        ]
        self.assertNotIn(
            "cartesian_signature",
            issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []})),
        )

    def test_catchphrase_frequency_is_corpus_level_not_a_single_row_false_positive(self) -> None:
        rows = [
            valid_line(
                id=f"line_{index}",
                text=(f"我丢，今天记下第{index}个小发现。" if index < 3 else f"窗台第{index}片光慢慢挪开了。"),
                semantic_group=f"fixture.catch.{index}",
            )
            for index in range(20)
        ]
        self.assertIn(
            "catchphrase_frequency",
            issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []})),
        )

    def test_opening_ending_and_length_distribution_are_enforced_on_real_samples(self) -> None:
        rows = [
            valid_line(
                id=f"line_{index}",
                text=f"总之第{index}句呀。",
                semantic_group=f"fixture.distribution.{index}",
            )
            for index in range(100)
        ]
        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))
        self.assertTrue(
            {"opening_frequency", "ending_frequency", "length_distribution"} <= codes
        )


class AllowlistTests(unittest.TestCase):
    def setUp(self) -> None:
        self.row = valid_line(
            category="Debugging",
            category_group="technical",
            text="这个 bug 先看调用链。",
        )

    def entry(self, **overrides: str) -> dict[str, str]:
        result = {
            "line_id": self.row.id,
            "normalized_text_sha256": normalized_text_sha256(self.row.text),
            "reason": "这是无第二人称的通用工程术语，不表示看到了用户代码。",
        }
        result.update(overrides)
        return result

    def test_exact_hash_bound_exception_suppresses_only_heuristic_and_becomes_warning(self) -> None:
        report = validate_corpus(
            [self.row], valid_config(), {"exceptions": [self.entry()]}
        )
        self.assertNotIn("technical_fake_context", issue_codes(report))
        self.assertIn(
            "allowlisted_technical_fake_context",
            {issue.code for issue in report.warnings},
        )

    def test_unknown_hash_mismatch_duplicate_and_stale_entries_are_hard_errors(self) -> None:
        cases = {
            "allowlist_unknown_line": self.entry(line_id="missing"),
            "allowlist_hash_mismatch": self.entry(normalized_text_sha256="0" * 64),
            "allowlist_stale": {
                "line_id": valid_line(id="safe").id,
                "normalized_text_sha256": normalized_text_sha256(valid_line(id="safe").text),
                "reason": "旧规则留下的例外。",
            },
        }
        rows = [self.row, valid_line(id="safe")]
        for expected, entry in cases.items():
            with self.subTest(expected=expected):
                self.assertIn(
                    expected,
                    issue_codes(validate_corpus(rows, valid_config(), {"exceptions": [entry]})),
                )

        duplicate = {"exceptions": [self.entry(), self.entry(reason="重复例外。")]}
        self.assertIn(
            "allowlist_duplicate",
            issue_codes(validate_corpus([self.row], valid_config(), duplicate)),
        )

    def test_allowlist_schema_is_exact_and_reason_is_required(self) -> None:
        entry = self.entry(reason="")
        entry["rule_code"] = "technical_fake_context"
        self.assertIn(
            "allowlist_format",
            issue_codes(validate_corpus([self.row], valid_config(), {"exceptions": [entry]})),
        )

    def test_allowlist_root_is_bound_to_schema_and_exact_corpus_sha256(self) -> None:
        entry = self.entry()
        valid = bound_allowlist([self.row], [entry])
        report = validate_corpus([self.row], valid_config(), valid)
        self.assertNotIn("technical_fake_context", issue_codes(report))

        stale = dict(valid)
        stale["corpus_sha256"] = "f" * 64
        self.assertIn(
            "allowlist_corpus_hash_mismatch",
            issue_codes(validate_corpus([self.row], valid_config(), stale)),
        )


class SchedulerConfigTests(unittest.TestCase):
    def test_weights_and_enums_are_strict(self) -> None:
        bad = valid_config(technical=0.30, easter_egg=0.03)
        codes = issue_codes(validate_config(bad))
        self.assertTrue({"technical_weight", "easter_egg_config_weight", "group_weight_sum"} <= codes)

    def test_exact_eight_groups_finite_non_bool_and_unique_highest_character_life(self) -> None:
        bad = valid_config()
        weights = bad["category_group_weights"]
        assert isinstance(weights, dict)
        weights["technical"] = math.nan
        weights["character_life"] = 0.18
        weights["extra"] = True
        codes = issue_codes(validate_config(bad))
        self.assertTrue({"group_weights", "character_life_weight", "group_weight_sum"} <= codes)

    def test_output_mode_targets_enforce_sum_and_playback_acceptance(self) -> None:
        bad = valid_config()
        bad["output_mode_targets"] = {
            "self_talk": 0.20,
            "ambient": 0.20,
            "user_direct": 0.30,
            "system_observe": 0.30,
        }
        self.assertIn("output_mode_targets", issue_codes(validate_config(bad)))

    def test_runtime_limits_enforce_every_task_five_minimum(self) -> None:
        bad = valid_config()
        limits = bad["runtime_limits"]
        assert isinstance(limits, dict)
        limits.update(
            {
                "minimum_interval_minutes": 7,
                "max_outputs_per_hour": 3,
                "late_night_max_outputs_per_hour": 2,
                "semantic_group_no_repeat": False,
                "technical_recent_max": 3,
                "user_direct_recent_max": 3,
                "easter_egg_recent_max": 2,
            }
        )
        self.assertIn("runtime_limits", issue_codes(validate_config(bad)))

    def test_context_token_and_trigger_contracts_reject_duplicates_unknowns_and_expressions(self) -> None:
        bad = valid_config()
        bad["context_tokens"] = ["none", "none", "x > 3"]
        bad["future_triggers"] = ["any"]
        codes = issue_codes(validate_config(bad))
        self.assertTrue({"context_tokens", "trigger_partition"} <= codes)


class SimulationGateTests(unittest.TestCase):
    def test_absent_simulation_is_an_explained_warning_until_task_six(self) -> None:
        report = validate_corpus([valid_line()], valid_config(), {"exceptions": []})
        missing = [issue for issue in report.warnings if issue.code == "simulation_missing"]
        self.assertEqual(1, len(missing))
        self.assertIn("Task 6", missing[0].message)

    def test_clean_structured_thirty_day_ten_seed_simulation_passes(self) -> None:
        report = validate_corpus(
            [valid_line()],
            valid_config(),
            {"exceptions": []},
            simulation_result=clean_simulation(),
        )
        self.assertFalse(report.errors)
        self.assertNotIn("simulation_missing", {issue.code for issue in report.warnings})

    def test_short_or_too_few_seed_simulation_is_rejected(self) -> None:
        simulation = clean_simulation()
        simulation["days"] = 29
        simulation["seeds"] = list(range(9))
        codes = issue_codes(
            validate_corpus(
                [valid_line()], valid_config(), {"exceptions": []}, simulation_result=simulation
            )
        )
        self.assertTrue({"simulation_duration", "simulation_seed_count"} <= codes)

    def test_supplied_hard_violations_and_acceptance_metrics_are_hard_errors(self) -> None:
        simulation = clean_simulation()
        simulation["hard_violations"] = [
            {"code": "adjacent_technical", "seed": 3, "detail": "two adjacent rows"}
        ]
        metrics = simulation["metrics"]
        assert isinstance(metrics, dict)
        metrics["category_group_ratio"] = {"technical": 0.25, "easter_egg": 0.03}
        codes = issue_codes(
            validate_corpus(
                [valid_line()], valid_config(), {"exceptions": []}, simulation_result=simulation
            )
        )
        self.assertTrue({"simulation_hard_violation", "simulation_metric"} <= codes)

    def test_simulation_recomputes_aggregates_and_rejects_zero_outputs_duplicates_and_unknown_keys(self) -> None:
        aggregate_mismatch = clean_simulation()
        metrics = aggregate_mismatch["metrics"]
        assert isinstance(metrics, dict)
        metrics["actual_output_count"] = 99
        metrics["category_group_ratio"] = {"technical": 0.10, "easter_egg": 0.01}
        self.assertIn(
            "simulation_aggregate_mismatch",
            issue_codes(
                validate_corpus(
                    [valid_line()],
                    valid_config(),
                    {"exceptions": []},
                    simulation_result=aggregate_mismatch,
                )
            ),
        )

        zero = clean_simulation()
        zero["plays"] = []
        self.assertIn(
            "simulation_zero_outputs",
            issue_codes(
                validate_corpus(
                    [valid_line()], valid_config(), {"exceptions": []}, simulation_result=zero
                )
            ),
        )

        duplicate_seed = clean_simulation()
        duplicate_seed["seeds"] = list(range(10)) + [0]
        self.assertIn(
            "simulation_seed_count",
            issue_codes(
                validate_corpus(
                    [valid_line()],
                    valid_config(),
                    {"exceptions": []},
                    simulation_result=duplicate_seed,
                )
            ),
        )

        unknown = clean_simulation()
        unknown["trusted"] = True
        self.assertIn(
            "simulation_format",
            issue_codes(
                validate_corpus(
                    [valid_line()], valid_config(), {"exceptions": []}, simulation_result=unknown
                )
            ),
        )

    def test_simulation_requires_complete_finite_probability_distributions(self) -> None:
        missing = clean_simulation()
        metrics = missing["metrics"]
        assert isinstance(metrics, dict)
        group_ratio = metrics["category_group_ratio"]
        assert isinstance(group_ratio, dict)
        del group_ratio["growth"]
        self.assertIn(
            "simulation_metric",
            issue_codes(
                validate_corpus(
                    [valid_line()], valid_config(), {"exceptions": []}, simulation_result=missing
                )
            ),
        )

        negative = clean_simulation()
        metrics = negative["metrics"]
        assert isinstance(metrics, dict)
        group_ratio = metrics["category_group_ratio"]
        assert isinstance(group_ratio, dict)
        group_ratio["easter_egg"] = -0.01
        self.assertIn(
            "simulation_metric",
            issue_codes(
                validate_corpus(
                    [valid_line()], valid_config(), {"exceptions": []}, simulation_result=negative
                )
            ),
        )


class FileAndCliTests(unittest.TestCase):
    def write_json(self, path: Path, value: object) -> None:
        path.write_text(
            json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n"
        )

    def test_loader_and_schema_share_one_authoritative_v2_header(self) -> None:
        from src.persona_corpus import loader, schema

        self.assertIs(schema.V2_HEADER, loader.V2_HEADER)

    def test_validate_file_reports_exact_header_and_physical_row_width_as_input_errors(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            config_path = root / "config.json"
            allowlist_path = root / "allowlist.json"
            self.write_json(config_path, valid_config())
            self.write_json(allowlist_path, {"exceptions": []})

            bad_header = root / "bad-header.tsv"
            bad_header.write_text("id\ttext\n", encoding="utf-8", newline="\n")
            with self.assertRaisesRegex(ValidationInputError, "exact v2 header"):
                validate_file(bad_header, config_path, allowlist_path)

            bad_width = root / "bad-width.tsv"
            bad_width.write_text("\t".join(V2_HEADER) + "\nonly-one-column\n", encoding="utf-8", newline="\n")
            with self.assertRaisesRegex(ValidationInputError, "line 2"):
                validate_file(bad_width, config_path, allowlist_path)

    def test_json_loader_rejects_duplicate_keys_nan_and_non_object_roots(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            fixtures = {
                "duplicate.json": '{"x": 1, "x": 2}\n',
                "nan.json": '{"x": NaN}\n',
                "array.json": '[]\n',
            }
            for name, payload in fixtures.items():
                path = root / name
                path.write_text(payload, encoding="utf-8", newline="\n")
                with self.subTest(name=name), self.assertRaises(ValidationInputError):
                    load_json_object(path)

    def test_tsv_reader_rejects_trailing_tab_middle_bom_nul_invalid_utf8_and_quoted_controls(self) -> None:
        good = serialize_v2([valid_line()])
        header, row, _ = good.split(b"\n")
        fixtures = {
            "trailing-tab.tsv": header + b"\n" + row + b"\t\n",
            "middle-bom.tsv": header + b"\n" + row.replace(b"catalog:test-fixture", b"catalog:\xef\xbb\xbftest") + b"\n",
            "nul.tsv": header + b"\n" + row.replace(b"catalog:test-fixture", b"catalog:\x00test") + b"\n",
            "invalid-utf8.tsv": header + b"\n" + row + b"\n" + b"\xff\tbad\n",
            "quoted-tab.tsv": header + b"\n" + row.replace(b"catalog:test-fixture", b'"catalog:\ttest"') + b"\n",
            "quoted-newline.tsv": header + b"\n" + row.replace(b"catalog:test-fixture", b'"catalog:\ntest"') + b"\n",
        }
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            for name, payload in fixtures.items():
                path = root / name
                path.write_bytes(payload)
                with self.subTest(name=name), self.assertRaisesRegex(
                    ValidationInputError, r"line (2|3)"
                ):
                    validate_file(path, CONFIG_PATH, ALLOWLIST_PATH)

    def test_cli_uses_exit_one_for_quality_and_two_for_input_format(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            config_path = root / "config.json"
            allowlist_path = root / "allowlist.json"
            self.write_json(config_path, valid_config())

            unsafe_path = root / "unsafe.tsv"
            unsafe_path.write_bytes(serialize_v2([valid_line(text="你现在累不累？")]))
            self.write_json(
                allowlist_path,
                {
                    "schema_version": 1,
                    "corpus_sha256": hashlib.sha256(unsafe_path.read_bytes()).hexdigest(),
                    "exceptions": [],
                },
            )
            command = [
                sys.executable,
                "tools/validate_corpus_v2.py",
                "--corpus",
                str(unsafe_path),
                "--config",
                str(config_path),
                "--allowlist",
                str(allowlist_path),
            ]
            quality = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(1, quality.returncode, quality.stdout + quality.stderr)

            malformed = root / "malformed.tsv"
            malformed.write_text("wrong\theader\n", encoding="utf-8", newline="\n")
            command[3] = str(malformed)
            format_failure = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
            self.assertEqual(2, format_failure.returncode, format_failure.stdout + format_failure.stderr)

    def test_issue_order_is_deterministic_across_input_order(self) -> None:
        rows = [
            valid_line(id="z", text="你现在累不累？"),
            valid_line(id="a", text="这个 bug 先看调用链。", category="Debugging", category_group="technical"),
        ]
        first = validate_corpus(rows, valid_config(), {"exceptions": []})
        second = validate_corpus(list(reversed(rows)), valid_config(), {"exceptions": []})
        signature = lambda report: [
            (issue.code, issue.line_id, issue.message) for issue in report.errors + report.warnings
        ]
        self.assertEqual(signature(first), signature(second))

    def test_current_full_corpus_has_zero_hard_errors(self) -> None:
        report = validate_file(CORPUS_PATH, CONFIG_PATH, ALLOWLIST_PATH)
        self.assertEqual([], list(report.errors))
        self.assertTrue(report.warnings)
        self.assertTrue(all(issue.message.strip() for issue in report.warnings))

    def test_cli_current_corpus_is_deterministic_and_successful(self) -> None:
        command = [
            sys.executable,
            "tools/validate_corpus_v2.py",
            "--corpus",
            str(CORPUS_PATH),
            "--config",
            str(CONFIG_PATH),
            "--allowlist",
            str(ALLOWLIST_PATH),
        ]
        runs = [
            subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8")
            for _ in range(2)
        ]
        self.assertTrue(all(run.returncode == 0 for run in runs), runs[0].stdout + runs[0].stderr)
        self.assertEqual(runs[0].stdout, runs[1].stdout)
        self.assertIn("0 hard errors", runs[0].stdout)


if __name__ == "__main__":
    unittest.main()
