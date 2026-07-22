from __future__ import annotations

import ast
import hashlib
import subprocess
import sys
import tempfile
import unittest
from collections import Counter
from dataclasses import fields
from pathlib import Path

from src.persona_corpus.builder import (
    BuildResult,
    build_v2,
    serialize_archive,
    serialize_pii_review,
    serialize_review,
    serialize_v2,
    write_build_outputs,
)
from src.persona_corpus.extraction import SourceMapping
from src.persona_corpus.models import CorpusLine, LegacyLine
from src.persona_corpus.schema import (
    ARCHIVE_HEADER,
    PII_REVIEW_HEADER,
    REVIEW_HEADER,
    V2_HEADER,
)


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
SOURCE_PATH = REPOSITORY_ROOT / "src/CompanionDesktopPet/Assets/persona-corpus.tsv"
MAPPING_PATH = REPOSITORY_ROOT / "data/intermediate/source-line-map.tsv"


def mapping(line: LegacyLine, topic_id: str | None = None) -> SourceMapping:
    return SourceMapping(
        source_line=line.source_line,
        category=line.category,
        original_text=line.text,
        prefix_id="",
        topic_id=topic_id or f"topic-{line.source_line}",
        suffix_id="",
        extraction_confidence=0.0,
    )


def fixture_result(seed: int = 20260722) -> BuildResult:
    source = [
        LegacyLine(1, "Debugging", "哈？空指针又来了，先看完整堆栈。"),
        LegacyLine(2, "WanderingLife", "今晚突然想起湖南的味道。"),
        LegacyLine(3, "ProactiveChat", "你今天写了什么代码？"),
        LegacyLine(4, "EasterEgg", "雷琳玥把真名藏进了第七页。"),
        LegacyLine(5, "DailyCare", "你的杯子是不是又一口没动。"),
    ]
    return build_v2(source, [mapping(line) for line in source], seed)


class BuildContractTests(unittest.TestCase):
    def test_content_catalog_is_materialized_complete_sentences_not_a_product(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        catalog_path = REPOSITORY_ROOT / "src/persona_corpus/content_catalog.py"
        tree = ast.parse(catalog_path.read_text(encoding="utf-8"))
        materialized = next(
            node
            for node in tree.body
            if (
                isinstance(node, ast.Assign)
                and any(
                    isinstance(target, ast.Name) and target.id == "CONTENT_CATALOG"
                    for target in node.targets
                )
            )
            or (
                isinstance(node, ast.AnnAssign)
                and isinstance(node.target, ast.Name)
                and node.target.id == "CONTENT_CATALOG"
            )
        )

        self.assertGreaterEqual(len(CONTENT_CATALOG), 800)
        self.assertLessEqual(len(CONTENT_CATALOG), 1200)
        self.assertIsInstance(materialized.value, ast.Tuple)
        self.assertEqual(len(CONTENT_CATALOG), len(materialized.value.elts))
        self.assertTrue(all(isinstance(node, ast.Call) for node in materialized.value.elts))
        self.assertNotIn("itertools.product", catalog_path.read_text(encoding="utf-8"))
        self.assertTrue(all(entry.text.strip().endswith(("。", "！")) for entry in CONTENT_CATALOG))

    def test_schema_is_exact_and_matches_model(self) -> None:
        self.assertEqual(
            (
                "id",
                "category",
                "category_group",
                "topic_id",
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
                "text",
                "source_kind",
                "source_reference",
                "rewrite_reason",
            ),
            V2_HEADER,
        )
        self.assertEqual(V2_HEADER, tuple(field.name for field in fields(CorpusLine)))

    def test_enabled_lines_are_standalone_and_need_no_reply(self) -> None:
        result = fixture_result()

        self.assertGreater(len(result.enabled), 0)
        self.assertTrue(all(not row.requires_reply for row in result.enabled))
        self.assertTrue(
            all("?" not in row.text and "？" not in row.text for row in result.enabled)
        )
        self.assertTrue(
            all(row.semantic_group and row.source_reference for row in result.enabled)
        )
        self.assertTrue(all("\t" not in row.text and "\n" not in row.text for row in result.enabled))

    def test_enabled_metadata_respects_runtime_schema(self) -> None:
        result = fixture_result()
        allowed_groups = {
            "technical", "growth", "career", "daily_care",
            "emotional_reflection", "character_life", "easter_egg", "system_ambient",
        }
        allowed_modes = {"self_talk", "ambient", "user_direct", "system_observe"}
        allowed_triggers = {
            "any", "app_start", "morning", "noon", "afternoon", "evening",
            "late_night", "day_changed", "weekday", "weekend", "holiday",
            "anniversary", "long_silence", "ide_foreground", "long_active",
            "idle_return", "story_timer",
        }
        allowed_tones = {
            "calm", "gentle", "playful", "dry", "serious", "sleepy",
            "nostalgic", "curious", "intimate", "encouraging",
        }

        for row in result.enabled:
            self.assertIn(row.category_group, allowed_groups)
            self.assertIn(row.output_mode, allowed_modes)
            self.assertIn(row.trigger, allowed_triggers)
            self.assertIn(row.tone, allowed_tones)
            self.assertIn(row.interrupt_cost, range(0, 6))
            self.assertGreaterEqual(row.cooldown_hours, 1)
            self.assertGreaterEqual(row.semantic_cooldown_hours, row.cooldown_hours)
            self.assertIn(row.max_per_day, (1, 2))
            self.assertGreater(row.weight, 0)

    def test_public_taxonomy_matches_the_v2_contract(self) -> None:
        result = fixture_result()
        allowed_source_kinds = {
            "rewritten_topic",
            "curated_standalone",
            "preserved_easter_egg",
            "new_ambient",
            "archived_question",
            "manual_review",
        }

        self.assertTrue(
            all(row.source_kind in allowed_source_kinds for row in result.enabled)
        )
        self.assertTrue(
            all(row.category_group == "career" for row in result.enabled if row.category == "Career")
        )
        self.assertTrue(
            all(
                row.category_group == "growth"
                for row in result.enabled
                if row.category in {"Study", "EnglishPractice"}
            )
        )
        self.assertTrue(
            all(
                row.output_mode == "system_observe"
                for row in result.enabled
                if row.category_group == "system_ambient"
            )
        )

    def test_ids_and_serialized_outputs_are_reproducible(self) -> None:
        first = fixture_result(20260722)
        second = fixture_result(20260722)

        self.assertEqual(serialize_v2(first.enabled), serialize_v2(second.enabled))
        self.assertEqual(serialize_archive(first.archive), serialize_archive(second.archive))
        self.assertEqual(serialize_review(first.review), serialize_review(second.review))
        self.assertEqual(
            serialize_pii_review(first.pii_review),
            serialize_pii_review(second.pii_review),
        )
        self.assertEqual(len(first.enabled), len({row.id for row in first.enabled}))

    def test_every_source_line_has_a_migration_disposition(self) -> None:
        result = fixture_result()

        self.assertEqual({1, 2, 3, 4, 5}, set(result.dispositions))
        self.assertTrue(all(result.dispositions[line] for line in result.dispositions))

    def test_suggested_rewrites_point_to_the_exact_inspiring_source_line(self) -> None:
        result = fixture_result()
        rewritten_by_source: dict[int, set[str]] = {}
        for row in result.enabled:
            if not row.source_reference.startswith("legacy:"):
                continue
            source_line = int(row.source_reference.split(";", 1)[0].split(":", 1)[1])
            rewritten_by_source.setdefault(source_line, set()).add(row.text)

        recoverable = [row for row in result.archive if row.can_recover]
        self.assertTrue(recoverable)
        for row in recoverable:
            self.assertIn(row.suggested_rewrite, rewritten_by_source[row.source_line])

    def test_proactive_chat_is_archived_and_never_directly_enabled(self) -> None:
        result = fixture_result()

        archived = [row for row in result.archive if row.source_line == 3]
        self.assertEqual(1, len(archived))
        self.assertEqual("requires_user_reply", archived[0].archive_reason)
        self.assertFalse(
            any(row.source_reference == "legacy:3" for row in result.enabled)
        )

    def test_pii_and_uncertain_context_are_reviewed_and_disabled(self) -> None:
        result = fixture_result()

        self.assertTrue(any(row.source_line == 4 for row in result.pii_review))
        self.assertTrue(any(row.source_line == 4 for row in result.review))
        self.assertTrue(any(row.source_line == 5 for row in result.review))
        self.assertFalse(
            any(
                marker in row.text
                for row in result.enabled
                for marker in ("雷琳玥", "湖南", "广东", "月薪", "工资", "打零工")
            )
        )

    def test_source_mapping_mismatch_is_rejected(self) -> None:
        source = [LegacyLine(1, "Debugging", "完整原文。")]
        bad_mapping = mapping(LegacyLine(1, "Debugging", "另一段文字。"))

        with self.assertRaisesRegex(ValueError, "source line 1"):
            build_v2(source, [bad_mapping], 20260722)

    def test_invalid_pii_policy_is_rejected(self) -> None:
        line = LegacyLine(1, "Debugging", "完整原文。")

        with self.assertRaisesRegex(ValueError, "pii_policy"):
            build_v2([line], [mapping(line)], 20260722, pii_policy="ignore")

    def test_writer_emits_all_four_exact_tsv_headers(self) -> None:
        result = fixture_result()
        with tempfile.TemporaryDirectory() as directory:
            output = Path(directory) / "persona-corpus-v2.tsv"
            paths = write_build_outputs(result, output)

            self.assertEqual(set(paths), {"v2", "archive", "review", "pii_review"})
            expected_headers = {
                "v2": V2_HEADER,
                "archive": ARCHIVE_HEADER,
                "review": REVIEW_HEADER,
                "pii_review": PII_REVIEW_HEADER,
            }
            for name, path in paths.items():
                header = path.read_text(encoding="utf-8").splitlines()[0]
                self.assertEqual("\t".join(expected_headers[name]), header)


class RealCorpusBuildTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        if not SOURCE_PATH.exists() or not MAPPING_PATH.exists():
            raise unittest.SkipTest("full corpus and mapping outputs are required")
        from src.persona_corpus.builder import load_source_mappings
        from src.persona_corpus.loader import load_legacy

        cls.result = build_v2(
            load_legacy(SOURCE_PATH),
            load_source_mappings(MAPPING_PATH),
            20260722,
        )

    def test_real_build_has_curated_target_size_and_traceability(self) -> None:
        self.assertGreaterEqual(len(self.result.enabled), 800)
        self.assertLessEqual(len(self.result.enabled), 1200)
        self.assertEqual(75375, len(self.result.dispositions))
        self.assertTrue(self.result.archive)
        self.assertTrue(self.result.review)
        self.assertTrue(self.result.pii_review)

    def test_real_build_has_unique_text_and_stable_ids(self) -> None:
        from src.persona_corpus.normalization import normalize_text

        texts = [row.text for row in self.result.enabled]
        normalized = [normalize_text(text) for text in texts]
        self.assertEqual(len(texts), len(set(texts)))
        self.assertEqual(len(normalized), len(set(normalized)))
        self.assertEqual(len(texts), len({row.id for row in self.result.enabled}))

    def test_real_build_meets_bubble_length_and_voice_limits(self) -> None:
        texts = [row.text for row in self.result.enabled]
        average = sum(map(len, texts)) / len(texts)
        over_36 = sum(len(text) > 36 for text in texts) / len(texts)
        catchphrases = ("哈？", "我靠", "我丢", "真的假的", "本姑娘", "笨蛋", "玥玥")
        catchphrase_share = sum(
            any(marker in text for marker in catchphrases) for text in texts
        ) / len(texts)

        self.assertGreaterEqual(average, 18)
        self.assertLessEqual(average, 36)
        self.assertLessEqual(over_36, 0.08)
        self.assertLessEqual(catchphrase_share, 0.10)

    def test_real_build_avoids_template_opening_dominance(self) -> None:
        texts = [row.text for row in self.result.enabled]
        for width in range(2, 7):
            starts = Counter(text[:width] for text in texts if len(text) >= width)
            phrase, count = starts.most_common(1)[0]
            self.assertLessEqual(
                count / len(texts),
                0.02,
                f"{width}-character opening {phrase!r} appears {count} times",
            )

    def test_cli_double_build_is_byte_identical(self) -> None:
        with tempfile.TemporaryDirectory() as first_dir, tempfile.TemporaryDirectory() as second_dir:
            outputs: list[Path] = []
            for directory in (first_dir, second_dir):
                output = Path(directory) / "persona-corpus-v2.tsv"
                command = [
                    sys.executable,
                    "tools/build_corpus_v2.py",
                    "--input",
                    str(SOURCE_PATH),
                    "--mappings",
                    str(MAPPING_PATH),
                    "--output",
                    str(output),
                    "--seed",
                    "20260722",
                ]
                completed = subprocess.run(
                    command,
                    cwd=REPOSITORY_ROOT,
                    check=False,
                    capture_output=True,
                    text=True,
                    encoding="utf-8",
                )
                self.assertEqual(0, completed.returncode, completed.stderr)
                outputs.append(output)

            first_hash = hashlib.sha256(outputs[0].read_bytes()).hexdigest()
            second_hash = hashlib.sha256(outputs[1].read_bytes()).hexdigest()
            self.assertEqual(first_hash, second_hash)


if __name__ == "__main__":
    unittest.main()
