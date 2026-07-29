from __future__ import annotations

import csv
import re
import unittest
from collections import Counter
from dataclasses import fields, replace
from pathlib import Path

from src.persona_corpus.loader import load_v2
from src.persona_corpus.models import CorpusLine
from src.persona_corpus.normalization import normalize_text
from src.persona_corpus.schema import ArchiveRow, SURFACE_MANIFEST_HEADER
from src.persona_corpus.surface_variants import (
    apply_dry_sharp_scene_dose,
    legacy_surface_line_id,
    materialize_legacy_surface_candidates,
    prepare_legacy_surface_candidates,
)


ROOT = Path(__file__).resolve().parents[1]
ARCHIVE_PATH = ROOT / "data/optimized/persona-corpus-archive.tsv"
V2_PATH = ROOT / "data/optimized/persona-corpus-v2.tsv"
V2_FIELDS = tuple(field.name for field in fields(CorpusLine))
SCHEDULING_FIELDS = (
    "category",
    "category_group",
    "semantic_group",
    "output_mode",
    "trigger",
    "required_context",
    "tone",
    "interrupt_cost",
    "cooldown_hours",
    "semantic_cooldown_hours",
    "max_per_day",
    "weight",
    "requires_reply",
    "enabled",
)


def metadata_summary(row: CorpusLine) -> tuple[object, ...]:
    return (
        row.category_group,
        row.output_mode,
        row.tone,
        row.interrupt_cost,
        row.cooldown_hours,
        row.weight,
    )


def archive_row(
    source_line: int,
    text: str,
    *,
    category: str = "Debugging",
    reason: str = "cartesian_duplicate",
    topic_id: str = "topic_debugging_fixture",
) -> ArchiveRow:
    return ArchiveRow(
        source_line=source_line,
        category=category,
        original_text=text,
        archive_reason=reason,
        topic_id=topic_id,
        suggested_rewrite="",
        can_recover=False,
    )


def corpus_row(
    line_id: str,
    text: str,
    *,
    source_reference: str = "catalog:fixture;variant:fixture",
) -> CorpusLine:
    return CorpusLine(
        id=line_id,
        category="EasterEgg",
        category_group="easter_egg",
        topic_id="fixture",
        semantic_group="fixture",
        output_mode="ambient",
        trigger="any",
        required_context="none",
        tone="playful",
        interrupt_cost=0,
        cooldown_hours=168.0,
        semantic_cooldown_hours=168.0,
        max_per_day=1,
        weight=0.1,
        requires_reply=False,
        enabled=True,
        relationship_profile="neutral",
        text=text,
        source_kind="preserved_easter_egg",
        source_reference=source_reference,
        rewrite_reason="fixture",
    )


def load_archive() -> list[ArchiveRow]:
    with ARCHIVE_PATH.open(encoding="utf-8", newline="") as stream:
        return [
            ArchiveRow(
                source_line=int(row["source_line"]),
                category=row["category"],
                original_text=row["original_text"],
                archive_reason=row["archive_reason"],
                topic_id=row["topic_id"],
                suggested_rewrite=row["suggested_rewrite"],
                can_recover=row["can_recover"] == "true",
            )
            for row in csv.DictReader(stream, delimiter="\t")
        ]


class LegacySurfaceCandidateTests(unittest.TestCase):
    def test_stable_id_uses_source_line_topic_and_normalized_digest(self) -> None:
        first = legacy_surface_line_id(42, "topic.debug", "先看日志。")
        punctuation_edit = legacy_surface_line_id(42, "topic.debug", "先看日志！")
        other_source = legacy_surface_line_id(43, "topic.debug", "先看日志。")
        other_topic = legacy_surface_line_id(42, "topic.other", "先看日志。")

        self.assertEqual(first, punctuation_edit)
        self.assertNotEqual(first, other_source)
        self.assertNotEqual(first, other_topic)
        self.assertRegex(first, r"^v2_surface_42_topic_debug_[0-9a-f]{12}$")

    def test_prepare_filters_every_runtime_safety_gate(self) -> None:
        rows = [
            archive_row(1, "先看完整堆栈，再决定从哪里下手。"),
            archive_row(2, "这个空指针先别脑补，完整堆栈更可靠。"),
            archive_row(3, "你今天是不是又忘记喝水了。", category="DailyCare"),
            archive_row(4, "要不要先看日志？"),
            archive_row(5, "雷琳玥今天说过这句话。"),
            archive_row(6, "必须马上重写整个模块。"),
            archive_row(7, "这句本来安全但长度会超过四十二个汉字，所以它不应该作为桌面气泡运行时语料进入候选，并且应继续留在归档里。"),
            archive_row(8, "能回复我一下吗。", category="ProactiveChat"),
            archive_row(
                9,
                "第七码头把一个小彩蛋藏进日志末尾。",
                category="EasterEgg",
                reason="low_information",
                topic_id="egg_safe",
            ),
            archive_row(
                10,
                "这条旧彩蛋经过编辑以后仍是同一来源。",
                category="EasterEgg",
                reason="low_information",
                topic_id="egg_existing",
            ),
            archive_row(
                11,
                "安全文本但归档原因不允许恢复。",
                reason="privacy_risk",
            ),
            archive_row(12, "手腕酸了就先停一下，别继续硬扛。", category="DailyCare"),
            archive_row(13, "这个接口偶尔超时，先把重试次数记下来。", category="Backend"),
            archive_row(14, "这个测试在你机器上过，环境差异要留意。", category="Debugging"),
        ]
        existing = [
            corpus_row(
                "v2_existing",
                "编辑后的彩蛋文本。",
                source_reference="legacy:10;topic:egg_existing;variant:existing",
            )
        ]

        prepared = prepare_legacy_surface_candidates(rows, existing)

        self.assertEqual([1, 9, 10], [row.source_line for row in prepared.candidates])
        self.assertEqual(1, prepared.cartesian_count)
        self.assertEqual(2, prepared.easter_egg_count)
        self.assertEqual(3, prepared.safety_marker_counts["technical_current_object"])
        self.assertTrue(
            all(row.source_kind == "legacy_surface_variant" for row in prepared.candidates)
        )
        self.assertRegex(
            prepared.candidates[0].source_reference,
            r"^legacy:1;topic:topic_debugging_fixture;variant:surface_1_[0-9a-f]{12}$",
        )
        self.assertEqual(
            {
                "archive_reason": 1,
                "fake_context": 3,
                "implicit_question": 1,
                "overly_commanding": 1,
                "pii": 1,
                "question_or_reply": 2,
                "too_long": 1,
                "unavailable_state": 1,
            },
            dict(prepared.rejection_counts),
        )

    def test_duplicate_normalized_text_is_kept_once_by_lowest_source_line(self) -> None:
        rows = [
            archive_row(20, "先看日志。"),
            archive_row(19, "先看日志！"),
        ]

        prepared = prepare_legacy_surface_candidates(rows, ())

        self.assertEqual([19], [row.source_line for row in prepared.candidates])
        self.assertEqual(1, prepared.rejection_counts["normalized_duplicate"])

    def test_materialize_inherits_existing_scene_policy_without_copying_text(self) -> None:
        existing = corpus_row("existing", "编辑过的场景文本。")
        existing = CorpusLine(
            **{
                **{field: getattr(existing, field) for field in V2_FIELDS},
                "category": "Debugging",
                "category_group": "technical",
                "topic_id": "topic_debug",
                "semantic_group": "debug.scene",
                "output_mode": "self_talk",
                "tone": "dry",
                "interrupt_cost": 1,
                "cooldown_hours": 120.0,
                "semantic_cooldown_hours": 120.0,
                "weight": 1.0,
            }
        )
        prepared = prepare_legacy_surface_candidates(
            [archive_row(31, "先看日志，再动实现。", topic_id="topic_debug")],
            [existing],
        )

        row = materialize_legacy_surface_candidates(prepared.candidates, [existing])[0]

        self.assertEqual("先看日志，再动实现。", row.text)
        self.assertEqual("debug.scene", row.semantic_group)
        for field in SCHEDULING_FIELDS:
            self.assertEqual(getattr(existing, field), getattr(row, field), field)
        self.assertEqual("legacy_surface_variant", row.source_kind)

    def test_materialize_assigns_explicit_defaults_to_new_legacy_topics(self) -> None:
        prepared = prepare_legacy_surface_candidates(
            [
                archive_row(41, "完整堆栈比猜测可靠。", topic_id="new_debug"),
                archive_row(
                    42,
                    "晚风路过时，脚步也可以慢一点。",
                    category="WanderingLife",
                    topic_id="new_wandering",
                ),
                archive_row(
                    43,
                    "第七码头藏着一个小彩蛋。",
                    category="EasterEgg",
                    reason="low_information",
                    topic_id="new_egg",
                ),
            ],
            (),
        )

        rows = materialize_legacy_surface_candidates(prepared.candidates, ())
        by_topic = {row.topic_id: row for row in rows}

        self.assertEqual(
            ("technical", "self_talk", "dry", 1, 120.0, 1.0),
            metadata_summary(by_topic["new_debug"]),
        )
        self.assertEqual(
            ("character_life", "self_talk", "nostalgic", 0, 144.0, 1.0),
            metadata_summary(by_topic["new_wandering"]),
        )
        self.assertEqual(
            ("easter_egg", "self_talk", "playful", 0, 720.0, 0.1),
            metadata_summary(by_topic["new_egg"]),
        )


class RealLegacySurfaceCandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.runtime = load_v2(V2_PATH)
        cls.surface_manifest = V2_PATH.with_name("persona-surface-manifest.tsv")

    def test_canonical_runtime_contains_exact_hybrid_partitions(self) -> None:
        counts = Counter(row.source_kind for row in self.runtime)

        self.assertEqual(82_132, len(self.runtime))
        self.assertEqual(30_000, counts["curated_authored"])
        self.assertEqual(51_326, counts["legacy_surface_variant"])
        self.assertEqual(537, counts["rewritten_topic"])
        self.assertEqual(140, counts["new_ambient"])
        self.assertEqual(30, counts["preserved_easter_egg"])
        self.assertEqual(99, counts["curated_standalone"])

    def test_canonical_surface_manifest_tracks_every_restored_surface(self) -> None:
        lines = self.surface_manifest.read_text(encoding="utf-8").splitlines()
        self.assertEqual(51_327, len(lines))
        self.assertEqual("\t".join(SURFACE_MANIFEST_HEADER), lines[0])

    def test_legacy_archive_remains_complete_and_only_audited_surfaces_are_restored(self) -> None:
        archive = load_archive()
        self.assertEqual(75_375, len(archive))
        archive_text = {normalize_text(row.original_text) for row in archive}
        surface_text = {
            normalize_text(row.text)
            for row in self.runtime
            if row.source_kind == "legacy_surface_variant"
        }
        self.assertEqual(51_326, len(surface_text))
        self.assertTrue(surface_text <= archive_text)
        self.assertEqual(51_802, sum(row.can_recover for row in archive))


if __name__ == "__main__":
    unittest.main()
