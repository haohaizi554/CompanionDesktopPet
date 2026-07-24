from __future__ import annotations

import csv
import re
import unittest
from pathlib import Path

from src.persona_corpus.loader import load_v2
from src.persona_corpus.models import CorpusLine
from src.persona_corpus.normalization import normalize_text
from src.persona_corpus.schema import ArchiveRow
from src.persona_corpus.surface_variants import (
    legacy_surface_line_id,
    prepare_legacy_surface_candidates,
)


ROOT = Path(__file__).resolve().parents[1]
ARCHIVE_PATH = ROOT / "data/optimized/persona-corpus-archive.tsv"
V2_PATH = ROOT / "data/optimized/persona-corpus-v2.tsv"


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
            archive_row(7, "这句本来安全但长度会超过三十六个汉字，所以它不应该作为桌面气泡运行时语料进入候选。"),
            archive_row(8, "能回复我一下吗。", category="ProactiveChat"),
            archive_row(
                9,
                "玥玥把一个小彩蛋藏进日志末尾。",
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
                "fake_context": 1,
                "implicit_question": 1,
                "overly_commanding": 1,
                "pii": 1,
                "question_or_reply": 2,
                "too_long": 1,
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


class RealLegacySurfaceCandidateTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.existing = load_v2(V2_PATH)
        cls.prepared = prepare_legacy_surface_candidates(load_archive(), cls.existing)

    def test_exact_audited_candidate_counts(self) -> None:
        self.assertEqual(49_018, self.prepared.cartesian_count)
        self.assertEqual(192, self.prepared.easter_egg_count)
        self.assertEqual(49_210, len(self.prepared.candidates))
        self.assertEqual(50_010, len(self.existing) + len(self.prepared.candidates))

    def test_safety_audit_reports_overlapping_marker_hits_and_first_dispositions(self) -> None:
        self.assertEqual(
            {
                "direct_state": 206,
                "implicit_question": 900,
                "reply_hook": 1_617,
                "technical_current_object": 1_009,
            },
            {
                name: count
                for name, count in self.prepared.safety_marker_counts.items()
                if count
            },
        )
        self.assertEqual(900, self.prepared.rejection_counts["implicit_question"])
        self.assertEqual(1_601, self.prepared.rejection_counts["reply_hook"])
        self.assertEqual(1_009, self.prepared.rejection_counts["fake_context"])

    def test_candidates_have_unique_stable_ids_text_and_complete_lineage(self) -> None:
        ids = [row.id for row in self.prepared.candidates]
        texts = [row.normalized_text for row in self.prepared.candidates]
        existing_texts = {normalize_text(row.text) for row in self.existing}

        self.assertEqual(len(ids), len(set(ids)))
        self.assertEqual(len(texts), len(set(texts)))
        self.assertTrue(all(texts))
        self.assertTrue(
            all(
                re.fullmatch(
                    rf"legacy:{row.source_line};topic:{re.escape(row.topic_id)};"
                    rf"variant:surface_{row.source_line}_[0-9a-f]{{12}}",
                    row.source_reference,
                )
                for row in self.prepared.candidates
            )
        )
        self.assertTrue(
            all(row.source_kind == "legacy_surface_variant" for row in self.prepared.candidates)
        )
        self.assertTrue(set(texts).isdisjoint(existing_texts))

    def test_all_excluded_privacy_and_fake_context_easter_rows_stay_out(self) -> None:
        enabled_sources = {row.source_line for row in self.prepared.candidates}
        archive = load_archive()
        unsafe_eggs = {
            row.source_line
            for row in archive
            if row.category == "EasterEgg"
            and row.archive_reason in {"privacy_risk", "fake_context"}
        }

        self.assertEqual(79, len(unsafe_eggs))
        self.assertTrue(enabled_sources.isdisjoint(unsafe_eggs))


if __name__ == "__main__":
    unittest.main()
