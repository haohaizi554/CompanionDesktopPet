from __future__ import annotations

import hashlib
import json
import math
import copy
import subprocess
import sys
import tempfile
import unittest
from dataclasses import replace
from datetime import datetime, timedelta, timezone
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
    scheduler_config_sha256,
    validate_config,
    validate_corpus,
    validate_file,
)
from src.persona_corpus.validation_rules.lineage_rules import build_repository_registry
from src.persona_corpus.validation_rules.editorial_rules import (
    validate_dry_sharp_contract,
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
    "emotional_reflection": 0.10,
    "character_life": 0.27,
    "easter_egg": 0.10,
    "system_ambient": 0.08,
}

OUTPUT_MODE_TARGETS = {
    "self_talk": 0.82,
    "ambient": 0.10,
    "user_direct": 0.0,
    "system_observe": 0.08,
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
                "easter_egg",
            ],
            "technical_recent_window": 5,
            "technical_recent_max": 2,
            "user_direct_recent_window": 10,
            "user_direct_recent_max": 2,
            "easter_egg_recent_window": 10,
            "easter_egg_recent_max": 1,
            "long_silence_minutes": 180,
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
        "source_reference": "catalog:test-fixture;variant:fixture.window.01",
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


def simulation_rows() -> list[CorpusLine]:
    specifications = (
        ("tech_a", "technical", "self_talk", "通用日志先按时间顺序排查。"),
        ("tech_b", "technical", "self_talk", "边界条件值得单独做一轮验证。"),
        ("life_a", "character_life", "self_talk", "窗边的光今天落得很安静。"),
        ("life_b", "character_life", "self_talk", "风从窗缝绕了一圈才离开。"),
        ("life_c", "character_life", "self_talk", "桌角那点光慢慢移到书页上。"),
        ("growth", "growth", "self_talk", "慢一点也算是在认真往前走。"),
        ("career", "career", "self_talk", "把今天学会的东西记下来挺好。"),
        ("care", "daily_care", "ambient", "偶尔停一会儿也不算耽误。"),
        ("emotion", "emotional_reflection", "self_talk", "有些心事放一晚会轻一点。"),
        ("system", "system_ambient", "system_observe", "时间又悄悄翻过了一页。"),
        ("egg", "easter_egg", "self_talk", "这一小段光只在偶然时出现。"),
    )
    rows: list[CorpusLine] = []
    category_by_group = {
        "technical": "Debugging",
        "growth": "Study",
        "career": "Career",
        "daily_care": "DailyCare",
        "emotional_reflection": "EmotionalSupport",
        "character_life": "WanderingLife",
        "system_ambient": "SystemAmbient",
        "easter_egg": "EasterEgg",
    }
    for suffix, group, mode, text in specifications:
        rows.append(
            valid_line(
                id=f"sim_{suffix}",
                category=category_by_group[group],
                category_group=group,
                topic_id=f"simulation.{suffix}",
                semantic_group=f"simulation.{suffix}",
                output_mode=mode,
                cooldown_hours=168.0 if group == "easter_egg" else 1.0,
                semantic_cooldown_hours=168.0 if group == "easter_egg" else 1.0,
                max_per_day=1,
                weight=0.1 if group == "easter_egg" else 0.5,
                source_kind="preserved_easter_egg" if group == "easter_egg" else "curated_standalone",
                source_reference=(
                    f"legacy:1;topic:simulation.{suffix};variant:simulation.{suffix}.01"
                    if group == "easter_egg"
                    else f"catalog:simulation-{suffix};variant:simulation.{suffix}.01"
                ),
                text=text,
            )
        )
    return rows


def simulation_context(played_at: datetime, **overrides: object) -> dict[str, object]:
    hour = played_at.hour
    if 6 <= hour < 11:
        daypart = "morning"
    elif 11 <= hour < 14:
        daypart = "noon"
    elif 14 <= hour < 18:
        daypart = "afternoon"
    elif 18 <= hour < 23:
        daypart = "evening"
    else:
        daypart = "late_night"
    result: dict[str, object] = {
        "event": "tick",
        "daypart": daypart,
        "weekday": played_at.isoweekday(),
        "is_weekend": played_at.isoweekday() >= 6,
        "holiday": None,
        "anniversary_days": 0,
        "minutes_since_last_output": 1440,
        "ide_foreground": None,
        "active_minutes": None,
        "idle_return": None,
        "fullscreen": None,
    }
    result.update(overrides)
    return result


def rebind_simulation(
    simulation: dict[str, object], rows: list[CorpusLine], config: dict[str, object]
) -> None:
    simulation["corpus_sha256"] = hashlib.sha256(serialize_v2(rows)).hexdigest()
    simulation["scheduler_config_sha256"] = scheduler_config_sha256(config)


def clean_simulation() -> tuple[list[CorpusLine], dict[str, object], dict[str, object]]:
    rows = simulation_rows()
    config = valid_config()
    base_ids = (
        "sim_life_a",
        "sim_growth",
        "sim_life_b",
        "sim_career",
        "sim_care",
        "sim_life_c",
        "sim_emotion",
        "sim_life_b",
        "sim_system",
    )
    attempts: list[dict[str, object]] = []
    start = datetime(2026, 1, 1, 12, 0, tzinfo=timezone(timedelta(hours=8)))
    for seed in range(10):
        technical_days = {0, 6, 12, 18, 24} if seed < 5 else {0, 7, 14, 21}
        technical_index = 0
        for day in range(30):
            played_at = start + timedelta(days=day)
            if day in technical_days:
                selected_id = "sim_tech_a" if technical_index % 2 == 0 else "sim_tech_b"
                technical_index += 1
            elif day in {3, 13, 23}:
                selected_id = "sim_egg"
            else:
                selected_id = base_ids[day % len(base_ids)]
            attempts.append(
                {
                    "seed": seed,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(played_at),
                    "selected_id": selected_id,
                }
            )
    simulation: dict[str, object] = {
        "schema_version": 1,
        "corpus_sha256": "",
        "scheduler_config_sha256": "",
        "days": 30,
        "seeds": list(range(10)),
        "attempts": attempts,
    }
    rebind_simulation(simulation, rows, config)
    return rows, config, simulation


class ValidationContractTests(unittest.TestCase):
    def test_category_group_contract_applies_to_enabled_and_disabled_rows(self) -> None:
        rows = [
            valid_line(id="enabled", category="Career", category_group="technical"),
            valid_line(
                id="disabled",
                category="Study",
                category_group="career",
                enabled=False,
                text="把笔记按主题收好，之后复习会轻松些。",
            ),
        ]

        report = validate_corpus(rows, valid_config(), {"exceptions": []})

        mismatches = [issue for issue in report.errors if issue.code == "category_group_mismatch"]
        self.assertEqual({"enabled", "disabled"}, {issue.line_id for issue in mismatches})

    def test_semantic_group_requires_identical_runtime_metadata_including_weight(self) -> None:
        first = valid_line(id="first", semantic_group="shared.semantic", weight=0.5)
        second = valid_line(
            id="second",
            semantic_group="shared.semantic",
            category="CharacterLife",
            output_mode="ambient",
            tone="dry_sharp",
            cooldown_hours=48.0,
            semantic_cooldown_hours=72.0,
            weight=0.25,
            requires_reply=True,
            enabled=False,
            text="风从窗边绕开，屋里只剩一点轻响。",
        )

        report = validate_corpus([first, second], valid_config(), {"exceptions": []})

        matching = [
            issue for issue in report.errors if issue.code == "semantic_group_inconsistent"
        ]
        self.assertEqual(1, len(matching))
        issue = matching[0]
        self.assertIn("output_mode", issue.message)
        self.assertIn("cooldown_hours", issue.message)
        self.assertIn("weight", issue.message)
        self.assertIn("category", issue.message)
        self.assertIn("tone", issue.message)
        self.assertIn("requires_reply", issue.message)
        self.assertIn("enabled", issue.message)

    def test_semantic_group_may_span_lineage_topics(self) -> None:
        first = valid_line(
            id="first-topic",
            topic_id="fixture.topic.one",
            semantic_group="shared.semantic.scene",
            source_reference="catalog:test-fixture;variant:fixture.topic.one",
        )
        second = valid_line(
            id="second-topic",
            topic_id="fixture.topic.two",
            semantic_group="shared.semantic.scene",
            source_reference="catalog:test-fixture;variant:fixture.topic.two",
            text="把一小段笔记整理好，之后回看会轻松一些。",
        )

        report = validate_corpus([first, second], valid_config(), {"exceptions": []})

        self.assertNotIn("semantic_group_inconsistent", issue_codes(report))

    def test_disabled_rows_receive_full_safety_preflight(self) -> None:
        row = valid_line(
            id="disabled-risk",
            enabled=False,
            requires_reply=True,
            text="雷琳玥，你现在很累吗？我永远陪着你。",
        )

        codes = issue_codes(validate_corpus([row], valid_config(), {"exceptions": []}))

        self.assertTrue(
            {"requires_reply", "question", "fake_context", "pii_enabled", "unsafe_emotional_claim"}
            <= codes
        )

    def test_normalized_text_uniqueness_includes_disabled_rows(self) -> None:
        rows = [
            valid_line(id="enabled", text="慢慢来，事情总会清楚。"),
            valid_line(id="disabled", enabled=False, text="慢慢来, 事情总会清楚!"),
        ]

        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))

        self.assertIn("duplicate_normalized_text", codes)

    def test_source_reference_grammar_bounds_and_topic_binding_are_strict(self) -> None:
        rows = [
            valid_line(id="grammar", source_reference="catalog:missing-variant"),
            valid_line(
                id="bounds",
                topic_id="topic_study_deadbeef0000",
                source_kind="rewritten_topic",
                source_reference=(
                    "legacy:75376;topic:topic_study_deadbeef0000;variant:fixture.bounds"
                ),
                text="复习计划拆到每天，执行时会少一点犹豫。",
            ),
            valid_line(
                id="topic",
                topic_id="topic_study_deadbeef0000",
                source_kind="rewritten_topic",
                source_reference="legacy:1;topic:other_topic;variant:fixture.topic",
                text="把错题归到原因下面，比只抄答案更有用。",
            ),
        ]

        codes = issue_codes(validate_corpus(rows, valid_config(), {"exceptions": []}))

        self.assertTrue(
            {"invalid_source_reference", "legacy_line_out_of_range", "lineage_topic_mismatch"}
            <= codes
        )

    def test_repository_lineage_registry_rejects_dangling_in_both_directions(self) -> None:
        row = load_v2(CORPUS_PATH)[0]
        broken = replace(
            row,
            id="broken-lineage",
            source_reference=row.source_reference.rsplit(";variant:", 1)[0]
            + ";variant:missing.variant",
            text="这条血缘故意指向不存在的变体。",
        )

        report = validate_corpus(
            [broken],
            valid_config(),
            {"exceptions": []},
            lineage_registry=build_repository_registry(),
        )
        codes = issue_codes(report)

        self.assertIn("dangling_lineage_variant", codes)
        self.assertIn("unmaterialized_catalog_variant", codes)

    def test_dawn_context_is_compatible_with_the_late_night_daypart_trigger(self) -> None:
        report = validate_corpus(
            [
                valid_line(
                    trigger="late_night",
                    required_context="time:dawn",
                    text="天快亮时，窗沿先收住一小片浅色。",
                )
            ],
            valid_config(),
            {"exceptions": []},
        )

        self.assertNotIn("trigger_context_conflict", issue_codes(report))

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

    def test_non_string_unhashable_enum_values_are_reported_without_crashing(self) -> None:
        row = valid_line(
            category_group=[],
            output_mode={},
            trigger=[],
            tone={},
            source_kind=[],
        )
        codes = issue_codes(validate_corpus([row], valid_config(), {"exceptions": []}))
        self.assertTrue(
            {
                "invalid_category_group",
                "invalid_output_mode",
                "invalid_trigger",
                "invalid_tone",
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
            valid_line(id="generic_name", text="张伟昨天来办公室。"),
        ]
        pii_ids = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "pii_enabled"
        }
        self.assertEqual({"phone", "id", "name", "generic_name"}, pii_ids)

    def test_generic_hunan_food_and_salary_field_are_not_personal_pii(self) -> None:
        rows = [
            valid_line(id="food", text="我偶尔会想吃湖南菜，那股辣味很有层次。"),
            valid_line(id="field", text="工资字段最好统一用整数分保存。"),
            valid_line(id="task", text="任务今天完成了。"),
            valid_line(id="program", text="程序今天工作正常。"),
            valid_line(id="game", text="王者今天更新了。"),
        ]
        pii_ids = {
            issue.line_id
            for issue in validate_corpus(rows, valid_config(), {"exceptions": []}).errors
            if issue.code == "pii_enabled"
        }
        self.assertEqual(set(), pii_ids)

    def test_identity_pii_requires_an_exact_editorial_adjudication(self) -> None:
        from src.persona_corpus.editorial import EDITORIAL_MANIFEST

        item = next(
            entry
            for entry in EDITORIAL_MANIFEST.identity_easter_eggs.values()
            if entry.allowed_markers == ("雷琳玥",)
        )
        row = valid_line(
            id=item.line_id,
            category="EasterEgg",
            category_group="easter_egg",
            topic_id=item.topic_id,
            semantic_group="easter_egg.editorial_identity.full_name",
            tone="playful",
            cooldown_hours=720.0,
            semantic_cooldown_hours=720.0,
            max_per_day=1,
            weight=0.1,
            text=item.text,
            source_kind="curated_standalone",
            source_reference=item.source_reference,
        )

        exact = validate_corpus([row], valid_config(), bound_allowlist([row]))
        wrong_id = replace(row, id="v2_unreviewed_identity")
        forbidden_combo = replace(row, text=f"{row.text} 湖南。")
        punctuation_edit = replace(row, text=row.text[:-1] + "！")
        phone_append = replace(row, text=f"{row.text} 13800138000")
        wrong_topic = replace(row, topic_id="easter_egg.editorial_identity.wrong")

        self.assertNotIn("pii_enabled", issue_codes(exact))
        self.assertIn(
            "pii_enabled",
            issue_codes(
                validate_corpus(
                    [wrong_id], valid_config(), bound_allowlist([wrong_id])
                )
            ),
        )
        for changed in (punctuation_edit, phone_append, wrong_topic):
            changed_report = validate_corpus(
                [changed], valid_config(), bound_allowlist([changed])
            )
            self.assertIn("pii_enabled", issue_codes(changed_report), changed.text)
        self.assertIn(
            "pii_enabled",
            issue_codes(
                validate_corpus(
                    [forbidden_combo],
                    valid_config(),
                    bound_allowlist([forbidden_combo]),
                )
            ),
        )

    def test_dry_sharp_tone_obeys_placement_contract(self) -> None:
        allowed = valid_line(tone="dry_sharp")
        forbidden_group = valid_line(
            id="dry_group",
            category="DailyCare",
            category_group="daily_care",
            tone="dry_sharp",
        )
        forbidden_trigger = valid_line(
            id="dry_trigger", tone="dry_sharp", trigger="late_night"
        )
        forbidden_context = valid_line(
            id="dry_context",
            tone="dry_sharp",
            trigger="holiday",
            required_context="holiday",
        )

        allowed_report = validate_corpus(
            [allowed], valid_config(), bound_allowlist([allowed])
        )
        self.assertNotIn("dry_sharp_placement", issue_codes(allowed_report))
        for row in (forbidden_group, forbidden_trigger, forbidden_context):
            report = validate_corpus([row], valid_config(), bound_allowlist([row]))
            self.assertIn("dry_sharp_placement", issue_codes(report), row.id)

    def test_dry_sharp_inventory_uses_scenes_and_never_row_variant_share(self) -> None:
        class Sink:
            def __init__(self) -> None:
                self.codes: list[str] = []

            def error(
                self,
                code: str,
                message: str,
                line_id: object = "",
                row_number: int | None = None,
            ) -> None:
                self.codes.append(code)

        rows = [
            valid_line(
                id=f"scene-{index}",
                semantic_group=f"fixture.scene.{index}",
                tone="dry_sharp" if index < 22 else "calm",
            )
            for index in range(500)
        ]
        expanded = rows + rows[:22] * 2300
        passing = Sink()

        validate_dry_sharp_contract(expanded, passing)

        self.assertNotIn("dry_sharp_scene_inventory_ratio", passing.codes)
        row_ratio = sum(row.tone == "dry_sharp" for row in expanded) / len(expanded)
        self.assertGreater(row_ratio, 0.40)

        failing = Sink()
        failing_rows = [
            replace(row, tone="calm") if index >= 19 else row
            for index, row in enumerate(rows)
        ]
        validate_dry_sharp_contract(failing_rows * 100, failing)
        self.assertIn("dry_sharp_scene_inventory_ratio", failing.codes)

    def test_category_group_output_mode_mapping_applies_to_enabled_and_disabled(self) -> None:
        rows = [
            valid_line(
                id="enabled-mode",
                category="DailyCare",
                category_group="daily_care",
                output_mode="self_talk",
            ),
            valid_line(
                id="disabled-mode",
                category="DailyCare",
                category_group="daily_care",
                output_mode="self_talk",
                enabled=False,
                text="风从杯沿绕开，桌面安静了一点。",
            ),
        ]

        report = validate_corpus(rows, valid_config(), {"exceptions": []})
        mismatches = {
            issue.line_id
            for issue in report.errors
            if issue.code == "category_group_output_mode_mismatch"
        }
        self.assertEqual({"enabled-mode", "disabled-mode"}, mismatches)

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
            "rule_code": "technical_fake_context",
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
                "rule_code": "technical_fake_context",
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
        self.assertIn(
            "allowlist_format",
            issue_codes(validate_corpus([self.row], valid_config(), {"exceptions": [entry]})),
        )

        unknown_rule = self.entry(rule_code="question")
        self.assertIn(
            "allowlist_format",
            issue_codes(validate_corpus([self.row], valid_config(), {"exceptions": [unknown_rule]})),
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

    def test_one_line_exception_suppresses_only_its_exact_rule_code(self) -> None:
        row = replace(self.row, text="你现在看这个 BUG，先停一下。")
        entry = {
            "rule_code": "technical_fake_context",
            "line_id": row.id,
            "normalized_text_sha256": normalized_text_sha256(row.text),
            "reason": "只确认其中一个启发式为误报。",
        }
        report = validate_corpus([row], valid_config(), {"exceptions": [entry]})
        self.assertNotIn("technical_fake_context", issue_codes(report))
        self.assertIn("fake_context", issue_codes(report))

    def test_same_line_can_have_distinct_explicit_rules_but_duplicate_tuple_is_rejected(self) -> None:
        row = replace(self.row, text="你现在看这个 BUG，先停一下。")
        technical = {
            "rule_code": "technical_fake_context",
            "line_id": row.id,
            "normalized_text_sha256": normalized_text_sha256(row.text),
            "reason": "技术短语人工确认。",
        }
        fake_context = dict(technical, rule_code="fake_context", reason="上下文短语人工确认。")
        report = validate_corpus(
            [row], valid_config(), {"exceptions": [technical, fake_context]}
        )
        self.assertNotIn("technical_fake_context", issue_codes(report))
        self.assertNotIn("fake_context", issue_codes(report))

        duplicate = validate_corpus(
            [row], valid_config(), {"exceptions": [technical, dict(technical)]}
        )
        self.assertIn("allowlist_duplicate", issue_codes(duplicate))


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

    def test_nested_wrong_json_types_become_config_format_errors_not_type_errors(self) -> None:
        bad = valid_config()
        limits = bad["runtime_limits"]
        assert isinstance(limits, dict)
        limits["block_adjacent_category_groups"] = [{}]
        self.assertIn("config_format", issue_codes(validate_config(bad)))


class SimulationGateTests(unittest.TestCase):
    def test_absent_simulation_is_an_explained_warning_until_task_six(self) -> None:
        report = validate_corpus([valid_line()], valid_config(), {"exceptions": []})
        missing = [issue for issue in report.warnings if issue.code == "simulation_missing"]
        self.assertEqual(1, len(missing))
        self.assertIn("Task 6", missing[0].message)

    def test_clean_bound_event_log_thirty_day_ten_seed_simulation_passes(self) -> None:
        rows, config, simulation = clean_simulation()
        report = validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        self.assertFalse(report.errors)
        self.assertNotIn("simulation_missing", {issue.code for issue in report.warnings})

    def test_schema_hashes_and_exact_keys_are_verified_not_trusted(self) -> None:
        rows, config, simulation = clean_simulation()
        simulation["corpus_sha256"] = "0" * 64
        simulation["scheduler_config_sha256"] = "f" * 64
        simulation["trusted"] = True
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue({"simulation_hash_mismatch", "simulation_format"} <= codes)

    def test_simulation_schema_version_rejects_float_one(self) -> None:
        rows, config, simulation = clean_simulation()
        simulation["schema_version"] = 1.0

        report = validate_corpus(
            rows,
            config,
            {"exceptions": []},
            simulation_result=simulation,
        )

        self.assertIn("simulation_format", issue_codes(report))

    def test_short_too_few_seed_and_incomplete_calendar_coverage_are_rejected(self) -> None:
        rows, config, simulation = clean_simulation()
        simulation["days"] = 29
        simulation["seeds"] = list(range(9))
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        simulation["attempts"] = [attempt for attempt in attempts if attempt["seed"] != 8]
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue(
            {"simulation_duration", "simulation_seed_count", "simulation_seed_coverage"}
            <= codes
        )

        rows, config, no_outputs_for_one_seed = clean_simulation()
        attempts = no_outputs_for_one_seed["attempts"]
        assert isinstance(attempts, list)
        for attempt in attempts:
            if attempt["seed"] == 9:
                attempt["selected_id"] = None
        self.assertIn(
            "simulation_seed_coverage",
            issue_codes(
                validate_corpus(
                    rows,
                    config,
                    {"exceptions": []},
                    simulation_result=no_outputs_for_one_seed,
                )
            ),
        )

    def test_event_schema_rejects_naive_timestamp_bad_context_unknown_and_disabled_ids(self) -> None:
        rows, config, simulation = clean_simulation()
        rows.append(valid_line(id="sim_disabled", enabled=False))
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        attempts[0]["attempted_at"] = "2026-01-01T12:00:00"
        attempts[1]["context"]["weekday"] = 9
        attempts[2]["selected_id"] = "sim_missing"
        attempts[3]["selected_id"] = "sim_disabled"
        rebind_simulation(simulation, rows, config)
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue({"simulation_format", "simulation_unknown_line"} <= codes)

    def test_trigger_and_required_context_are_recomputed_from_row_and_actual_context(self) -> None:
        rows, config, simulation = clean_simulation()
        rows.append(
            valid_line(
                id="sim_contextual",
                trigger="app_start",
                required_context="app_started,ide_foreground",
                cooldown_hours=1.0,
                semantic_cooldown_hours=1.0,
            )
        )
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        attempts[5]["selected_id"] = "sim_contextual"
        rebind_simulation(simulation, rows, config)
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue({"simulation_trigger_violation", "simulation_context_violation"} <= codes)

    def test_row_id_semantic_daily_interval_and_interrupt_constraints_are_recomputed(self) -> None:
        rows, config, simulation = clean_simulation()
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        first = attempts[0]
        duplicate = copy.deepcopy(first)
        duplicate["attempted_at"] = "2026-01-01T12:04:00+08:00"
        duplicate["context"]["minutes_since_last_output"] = 4
        attempts.append(duplicate)
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue(
            {
                "simulation_id_cooldown_violation",
                "simulation_semantic_cooldown_violation",
                "simulation_max_per_day_violation",
                "simulation_minimum_interval_violation",
                "simulation_interrupt_budget_violation",
                "simulation_adjacent_semantic_violation",
            }
            <= codes
        )

    def test_hourly_late_night_adjacent_group_and_recent_quotas_are_recomputed(self) -> None:
        rows, config, simulation = clean_simulation()
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        first_seed = [attempt for attempt in attempts if attempt["seed"] == 0]
        for index in range(20):
            first_seed[index]["selected_id"] = "sim_tech_a" if index % 2 == 0 else "sim_tech_b"
        base = datetime.fromisoformat(str(first_seed[10]["attempted_at"]))
        for minute, selected_id in ((20, "sim_growth"), (40, "sim_career")):
            played_at = base.replace(hour=12, minute=minute)
            attempts.append(
                {
                    "seed": 0,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(played_at, minutes_since_last_output=20),
                    "selected_id": selected_id,
                }
            )
        late_base = base.replace(day=base.day + 1, hour=23, minute=0)
        for minute, selected_id in ((0, "sim_growth"), (20, "sim_career")):
            played_at = late_base.replace(minute=minute)
            attempts.append(
                {
                    "seed": 0,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(played_at, minutes_since_last_output=20),
                    "selected_id": selected_id,
                }
            )
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue(
            {
                "simulation_hourly_budget_violation",
                "simulation_late_night_budget_violation",
                "simulation_adjacent_group_violation",
                "simulation_recent_technical_violation",
                "simulation_metric",
            }
            <= codes
        )

    def test_hourly_and_late_night_budgets_use_rolling_windows_across_clock_boundaries(self) -> None:
        rows, config, simulation = clean_simulation()
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        first_seed = [attempt for attempt in attempts if attempt["seed"] == 0]
        first_seed[10]["selected_id"] = None
        base = datetime.fromisoformat(str(first_seed[10]["attempted_at"]))
        for hour, minute, selected_id, elapsed in (
            (12, 30, "sim_life_a", 1470),
            (12, 50, "sim_growth", 20),
            (13, 10, "sim_career", 20),
        ):
            played_at = base.replace(hour=hour, minute=minute)
            attempts.append(
                {
                    "seed": 0,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(
                        played_at, minutes_since_last_output=elapsed
                    ),
                    "selected_id": selected_id,
                }
            )
        late_first = base.replace(hour=23, minute=50)
        late_second = late_first + timedelta(minutes=20)
        for played_at, selected_id, elapsed in (
            (late_first, "sim_life_b", 640),
            (late_second, "sim_system", 20),
        ):
            attempts.append(
                {
                    "seed": 0,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(
                        played_at, minutes_since_last_output=elapsed
                    ),
                    "selected_id": selected_id,
                }
            )
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue(
            {
                "simulation_hourly_budget_violation",
                "simulation_late_night_budget_violation",
            }
            <= codes
        )

        rows, config, boundary = clean_simulation()
        attempts = boundary["attempts"]
        assert isinstance(attempts, list)
        first_seed = [attempt for attempt in attempts if attempt["seed"] == 0]
        first_seed[10]["selected_id"] = None
        base = datetime.fromisoformat(str(first_seed[10]["attempted_at"]))
        for hour, minute, selected_id, elapsed in (
            (12, 10, "sim_life_a", 1450),
            (12, 30, "sim_growth", 20),
            (13, 10, "sim_career", 40),
        ):
            played_at = base.replace(hour=hour, minute=minute)
            attempts.append(
                {
                    "seed": 0,
                    "attempted_at": played_at.isoformat(),
                    "context": simulation_context(
                        played_at, minutes_since_last_output=elapsed
                    ),
                    "selected_id": selected_id,
                }
            )
        boundary_codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=boundary)
        )
        self.assertNotIn("simulation_hourly_budget_violation", boundary_codes)

    def test_recent_user_direct_easter_question_and_zero_output_checks_use_real_rows(self) -> None:
        rows, config, simulation = clean_simulation()
        user = valid_line(
            id="sim_user",
            output_mode="user_direct",
            required_context="ide_foreground",
            cooldown_hours=1.0,
            semantic_cooldown_hours=1.0,
        )
        question = valid_line(
            id="sim_question",
            text="这一句真的需要回答吗？",
            cooldown_hours=1.0,
            semantic_cooldown_hours=1.0,
        )
        rows.extend([user, question])
        attempts = simulation["attempts"]
        assert isinstance(attempts, list)
        first_seed = [attempt for attempt in attempts if attempt["seed"] == 0]
        for index in (0, 2, 4):
            first_seed[index]["selected_id"] = "sim_user"
            first_seed[index]["context"]["ide_foreground"] = True
        first_seed[8]["selected_id"] = "sim_egg"
        first_seed[12]["selected_id"] = "sim_egg"
        first_seed[20]["selected_id"] = "sim_question"
        rebind_simulation(simulation, rows, config)
        codes = issue_codes(
            validate_corpus(rows, config, {"exceptions": []}, simulation_result=simulation)
        )
        self.assertTrue(
            {
                "simulation_recent_user_direct_violation",
                "simulation_recent_easter_egg_violation",
                "simulation_question",
            }
            <= codes
        )

        zero = copy.deepcopy(simulation)
        zero_attempts = zero["attempts"]
        assert isinstance(zero_attempts, list)
        for attempt in zero_attempts:
            attempt["selected_id"] = None
        self.assertIn(
            "simulation_zero_outputs",
            issue_codes(validate_corpus(rows, config, {"exceptions": []}, simulation_result=zero)),
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

    def test_cli_nested_config_type_failure_is_exit_two_without_traceback(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            corpus_path = root / "corpus.tsv"
            corpus_path.write_bytes(serialize_v2([valid_line()]))
            config = valid_config()
            limits = config["runtime_limits"]
            assert isinstance(limits, dict)
            limits["block_adjacent_category_groups"] = [{}]
            config_path = root / "config.json"
            allowlist_path = root / "allowlist.json"
            self.write_json(config_path, config)
            self.write_json(
                allowlist_path,
                {
                    "schema_version": 1,
                    "corpus_sha256": hashlib.sha256(corpus_path.read_bytes()).hexdigest(),
                    "exceptions": [],
                },
            )
            completed = subprocess.run(
                [
                    sys.executable,
                    "tools/validate_corpus_v2.py",
                    "--corpus",
                    str(corpus_path),
                    "--config",
                    str(config_path),
                    "--allowlist",
                    str(allowlist_path),
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
            )
            self.assertEqual(2, completed.returncode, completed.stdout + completed.stderr)
            self.assertNotIn("Traceback", completed.stdout + completed.stderr)

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
