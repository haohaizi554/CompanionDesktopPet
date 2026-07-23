from __future__ import annotations

import ast
import hashlib
import re
import subprocess
import sys
import tempfile
import unittest
from collections import Counter
from difflib import SequenceMatcher
from dataclasses import fields, replace
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
SOURCE_PATH = REPOSITORY_ROOT / "data/source/persona-corpus.original.tsv"
MAPPING_PATH = REPOSITORY_ROOT / "data/intermediate/source-line-map.tsv"
SPEC_PATH = REPOSITORY_ROOT / "docs/superpowers/specs/2026-07-22-persona-corpus-v2-design.md"
REPORT_PATH = REPOSITORY_ROOT / ".superpowers/sdd/task-3-report.md"
ATTRIBUTES_PATH = REPOSITORY_ROOT / ".gitattributes"
IMMUTABLE_SOURCE_SHA256 = "3fd7356845df838c652f7a7668013f2b15b0e91ddfa5d784b2b71a514a2c7534"
IMMUTABLE_SOURCE_BYTES = 7_961_787
IMMUTABLE_SOURCE_CRLF = 75_375


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
    from src.persona_corpus.content_catalog import CatalogEntry

    source = [
        LegacyLine(1, "Debugging", "哈？空指针又来了，先看完整堆栈。"),
        LegacyLine(2, "WanderingLife", "今晚突然想起湖南的味道。"),
        LegacyLine(3, "ProactiveChat", "你今天写了什么代码？"),
        LegacyLine(4, "EasterEgg", "雷琳玥把真名藏进了第七页。"),
        LegacyLine(5, "DailyCare", "你的杯子是不是又一口没动。"),
    ]
    catalog = (
        CatalogEntry(
            category="Debugging",
            category_group="technical",
            variant_id="fixture.debugging.stack.01",
            runtime_topic_id="topic-1",
            editorial_role="core_observation",
            semantic_group="fixture.debugging.stack",
            output_mode="self_talk",
            trigger="idle",
            required_context="none",
            tone="dry_warm",
            interrupt_cost=1,
            cooldown_hours=24.0,
            semantic_cooldown_hours=24.0,
            max_per_day=1,
            weight=1.0,
            text="空指针出现时，完整堆栈比猜测更可靠。",
            source_kind="topic_rewrite",
            source_reference="legacy:1;topic:topic-1",
            rewrite_reason="fixture rewrite with exact lineage",
        ),
        CatalogEntry(
            category="EasterEgg",
            category_group="easter_egg",
            variant_id="fixture.easter.safe.01",
            runtime_topic_id="topic-4",
            editorial_role="easter_egg_scene",
            semantic_group="fixture.easter.safe",
            output_mode="ambient",
            trigger="app_start",
            required_context="none",
            tone="playful_rare",
            interrupt_cost=0,
            cooldown_hours=48.0,
            semantic_cooldown_hours=48.0,
            max_per_day=1,
            weight=1.0,
            text="第七页藏着一枚不透露真名的小彩蛋。",
            source_kind="legacy_standalone",
            source_reference="legacy:4;topic:topic-4",
            rewrite_reason="fixture privacy-safe rewrite with exact lineage",
        ),
        CatalogEntry(
            category="DailyCare",
            category_group="daily_care",
            variant_id="fixture.daily.water.01",
            runtime_topic_id="topic-5",
            editorial_role="care_rationale",
            semantic_group="fixture.daily.water",
            output_mode="ambient",
            trigger="long_active",
            required_context="none",
            tone="soft_warm",
            interrupt_cost=1,
            cooldown_hours=24.0,
            semantic_cooldown_hours=24.0,
            max_per_day=1,
            weight=1.0,
            text="桌边放杯水，想起来时喝一口就好。",
            source_kind="topic_rewrite",
            source_reference="legacy:5;topic:topic-5",
            rewrite_reason="fixture context-safe rewrite with exact lineage",
        ),
    )
    return build_v2(
        source,
        [mapping(line) for line in source],
        seed,
        catalog=catalog,
    )


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

    def test_archive_and_review_headers_match_authoritative_spec(self) -> None:
        self.assertEqual(
            (
                "source_line", "category", "original_text", "archive_reason",
                "topic_id", "suggested_rewrite", "can_recover",
            ),
            ARCHIVE_HEADER,
        )
        self.assertEqual(
            (
                "review_id", "source_line", "category", "original_text",
                "risk_type", "risk_description", "suggested_action",
                "suggested_rewrite", "default_enabled",
            ),
            REVIEW_HEADER,
        )

    def test_catalog_has_explicit_immutable_variant_and_source_reference(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG, CatalogEntry

        catalog_fields = set(CatalogEntry.__dataclass_fields__)
        self.assertIn("variant_id", catalog_fields)
        self.assertIn("runtime_topic_id", catalog_fields)
        self.assertIn("editorial_role", catalog_fields)
        self.assertIn("source_reference", catalog_fields)
        self.assertNotIn("source_reference_hint", catalog_fields)
        variants = [entry.variant_id for entry in CONTENT_CATALOG]
        self.assertEqual(len(variants), len(set(variants)))
        self.assertTrue(all(entry.source_reference for entry in CONTENT_CATALOG))
        self.assertTrue(all(entry.runtime_topic_id for entry in CONTENT_CATALOG))
        self.assertTrue(all(entry.editorial_role for entry in CONTENT_CATALOG))
        authored = [
            entry for entry in CONTENT_CATALOG
            if entry.source_reference.startswith("catalog:")
        ]
        self.assertTrue(authored)
        self.assertTrue(
            all(entry.runtime_topic_id != entry.variant_id for entry in authored)
        )

    def test_legacy_topics_declare_distinct_immutable_editorial_roles(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        grouped: dict[tuple[str, str], list[object]] = {}
        for entry in CONTENT_CATALOG:
            legacy = re.fullmatch(r"legacy:(\d+);topic:([^;]+)", entry.source_reference)
            if legacy is None:
                continue
            source_topic = legacy.group(2)
            self.assertEqual(source_topic, entry.runtime_topic_id)
            self.assertRegex(entry.editorial_role, r"^[a-z][a-z0-9_]*$")
            grouped.setdefault((entry.category, source_topic), []).append(entry)

        self.assertTrue(grouped)
        for topic, entries in grouped.items():
            if len(entries) < 2:
                continue
            roles = [entry.editorial_role for entry in entries]
            self.assertEqual(
                len(roles),
                len(set(roles)),
                f"legacy topic {topic!r} reuses an editorial role: {roles!r}",
            )

    def test_sixty_three_practice_variants_have_human_editorial_angles(self) -> None:
        from src.persona_corpus.content_catalog import (
            CONTENT_CATALOG,
            EDITORIALLY_ADJUDICATED_VARIANTS,
        )
        from src.persona_corpus.normalization import normalize_text

        expected_counts = {
            "Algorithms": 13,
            "Architecture": 10,
            "Backend": 9,
            "Career": 9,
            "Cpp": 12,
            "Database": 10,
        }
        entries = {entry.variant_id: entry for entry in CONTENT_CATALOG}
        adjudicated = tuple(EDITORIALLY_ADJUDICATED_VARIANTS)
        self.assertEqual(63, len(adjudicated))
        self.assertEqual(63, len(set(adjudicated)))
        self.assertEqual(
            expected_counts,
            dict(Counter(entries[variant_id].category for variant_id in adjudicated)),
        )
        for variant_id in adjudicated:
            self.assertTrue(variant_id.endswith(".practice"), variant_id)
            entry = entries[variant_id]
            observation = entries[variant_id.removesuffix(".practice") + ".observation"]
            self.assertNotIn(
                entry.editorial_role,
                {"core_observation", "implementation_practice"},
            )
            self.assertTrue(
                entry.rewrite_reason.startswith("human-editorial-angle:"),
                entry.rewrite_reason,
            )
            similarity = SequenceMatcher(
                None,
                normalize_text(observation.text),
                normalize_text(entry.text),
            ).ratio()
            self.assertLessEqual(similarity, 0.55, (variant_id, similarity))

    def test_all_retained_observation_practice_pairs_are_semantically_distinct(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG
        from src.persona_corpus.normalization import normalize_text

        entries = {entry.variant_id: entry for entry in CONTENT_CATALOG}
        offenders = []
        for variant_id, practice in entries.items():
            if not variant_id.endswith(".practice"):
                continue
            observation_id = variant_id.removesuffix(".practice") + ".observation"
            observation = entries.get(observation_id)
            if observation is None:
                continue
            similarity = SequenceMatcher(
                None,
                normalize_text(observation.text),
                normalize_text(practice.text),
            ).ratio()
            if similarity > 0.55:
                offenders.append((variant_id, round(similarity, 4)))
        self.assertEqual([], sorted(offenders))

    def test_second_pass_authored_entries_declare_monotonic_cooldowns(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        entries = [
            entry
            for entry in CONTENT_CATALOG
            if entry.source_reference
            == "catalog:second-editorial-pass:independent-life-care-reflection"
        ]
        self.assertEqual(29, len(entries))
        for entry in entries:
            self.assertGreaterEqual(
                entry.semantic_cooldown_hours,
                entry.cooldown_hours,
                entry.variant_id,
            )

    def test_design_spec_lists_exact_archive_and_review_headers(self) -> None:
        spec = SPEC_PATH.read_text(encoding="utf-8")
        archive = (
            "source_line,category,original_text,archive_reason,topic_id,"
            "suggested_rewrite,can_recover"
        )
        review = (
            "review_id,source_line,category,original_text,risk_type,"
            "risk_description,suggested_action,suggested_rewrite,default_enabled"
        )
        self.assertEqual(1, spec.count(archive))
        self.assertEqual(1, spec.count(review))

    def test_copy_edit_does_not_change_stable_catalog_id(self) -> None:
        from src.persona_corpus.builder import catalog_line_id
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        entry = CONTENT_CATALOG[0]
        edited = replace(
            entry,
            text="改过文案以后，历史标识仍然保持不变。",
            category="CopyEditedCategory",
            semantic_group="copy.edited.group",
        )
        self.assertEqual(catalog_line_id(entry), catalog_line_id(edited))

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
            root = Path(directory)
            output = root / "data/optimized/persona-corpus-v2.tsv"
            paths = write_build_outputs(result, output)

            self.assertEqual(set(paths), {"v2", "archive", "review", "pii_review"})
            self.assertEqual(root / "reports/pii-review.tsv", paths["pii_review"])
            expected_headers = {
                "v2": V2_HEADER,
                "archive": ARCHIVE_HEADER,
                "review": REVIEW_HEADER,
                "pii_review": PII_REVIEW_HEADER,
            }
            for name, path in paths.items():
                header = path.read_text(encoding="utf-8").splitlines()[0]
                self.assertEqual("\t".join(expected_headers[name]), header)

    def test_flat_output_requires_explicit_contained_report_path(self) -> None:
        result = fixture_result()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "persona-corpus-v2.tsv"
            with self.assertRaisesRegex(ValueError, "report_output"):
                write_build_outputs(result, output)

            report_output = root / "reports/pii-review.tsv"
            paths = write_build_outputs(result, output, report_output=report_output)
            self.assertEqual(report_output, paths["pii_review"])
            self.assertTrue(report_output.is_file())

    def test_writer_rejects_colliding_output_paths_before_writing(self) -> None:
        result = fixture_result()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            flat_output = root / "persona-corpus-v2.tsv"
            with self.assertRaisesRegex(ValueError, "distinct"):
                write_build_outputs(
                    result,
                    flat_output,
                    report_output=flat_output,
                )

            canonical_collision = root / "data/optimized/persona-corpus-archive.tsv"
            with self.assertRaisesRegex(ValueError, "distinct"):
                write_build_outputs(result, canonical_collision)

    def test_noncanonical_report_output_stays_under_output_directory(self) -> None:
        result = fixture_result()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            output = root / "nested/persona-corpus-v2.tsv"
            escaping_report = root / "reports/pii-review.tsv"
            with self.assertRaisesRegex(ValueError, "contained"):
                write_build_outputs(
                    result,
                    output,
                    report_output=escaping_report,
                )


class RealCorpusBuildTests(unittest.TestCase):
    def test_real_source_fixture_is_the_tracked_canonical_copy(self) -> None:
        self.assertEqual(
            REPOSITORY_ROOT / "data/source/persona-corpus.original.tsv",
            SOURCE_PATH,
        )
        self.assertTrue(SOURCE_PATH.is_file())

    def test_exact_newline_policy_preserves_source_and_generated_bytes(self) -> None:
        self.assertTrue(ATTRIBUTES_PATH.is_file(), "root .gitattributes is required")
        attributes = {
            line.strip()
            for line in ATTRIBUTES_PATH.read_text(encoding="utf-8").splitlines()
            if line.strip() and not line.lstrip().startswith("#")
        }
        self.assertTrue(
            {
                "/data/source/persona-corpus.original.tsv -text diff whitespace=cr-at-eol",
                "/src/CompanionDesktopPet/Assets/persona-corpus.tsv -text diff whitespace=cr-at-eol",
                "/data/intermediate/*.tsv text eol=lf",
                "/data/optimized/*.tsv text eol=lf",
                "/reports/*.tsv text eol=lf",
            }.issubset(attributes)
        )

        source = SOURCE_PATH.read_bytes()
        self.assertEqual(IMMUTABLE_SOURCE_BYTES, len(source))
        self.assertEqual(IMMUTABLE_SOURCE_SHA256, hashlib.sha256(source).hexdigest())
        self.assertEqual(IMMUTABLE_SOURCE_CRLF, source.count(b"\r\n"))
        self.assertEqual(IMMUTABLE_SOURCE_CRLF, source.count(b"\n"))
        self.assertNotIn(b"\n", source.replace(b"\r\n", b""))

        legacy_asset = REPOSITORY_ROOT / "src/CompanionDesktopPet/Assets/persona-corpus.tsv"
        if legacy_asset.is_file():
            self.assertEqual(source, legacy_asset.read_bytes())

        generated = tuple((REPOSITORY_ROOT / "data/intermediate").glob("*.tsv"))
        generated += tuple((REPOSITORY_ROOT / "data/optimized").glob("*.tsv"))
        generated += tuple((REPOSITORY_ROOT / "reports").glob("*.tsv"))
        self.assertTrue(generated)
        for path in generated:
            self.assertNotIn(b"\r\n", path.read_bytes(), path)

    @classmethod
    def setUpClass(cls) -> None:
        if not SOURCE_PATH.is_file() or not MAPPING_PATH.is_file():
            raise FileNotFoundError("tracked full corpus and mapping fixtures are required")
        from src.persona_corpus.builder import load_source_mappings
        from src.persona_corpus.loader import load_legacy

        cls.result = build_v2(
            load_legacy(SOURCE_PATH),
            load_source_mappings(MAPPING_PATH),
            20260722,
        )

    def test_real_build_has_curated_target_size_and_traceability(self) -> None:
        self.assertEqual(800, len(self.result.enabled))
        self.assertEqual(75375, len(self.result.dispositions))
        self.assertTrue(self.result.archive)
        self.assertTrue(self.result.review)
        self.assertTrue(self.result.pii_review)

    def test_real_lineage_matches_explicit_catalog_source_mapping(self) -> None:
        from src.persona_corpus.builder import catalog_line_id, load_source_mappings
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        entries = {entry.variant_id: entry for entry in CONTENT_CATALOG}
        mappings = {
            mapping.source_line: mapping for mapping in load_source_mappings(MAPPING_PATH)
        }
        self.assertEqual(len(CONTENT_CATALOG), len(entries))
        for row in self.result.enabled:
            match = re.search(r"(?:^|;)variant:([^;]+)$", row.source_reference)
            self.assertIsNotNone(match, row.source_reference)
            variant_id = match.group(1)
            entry = entries[variant_id]
            self.assertEqual(catalog_line_id(entry), row.id)
            self.assertEqual(entry.runtime_topic_id, row.topic_id)
            self.assertEqual(entry.required_context, row.required_context)
            if row.source_kind not in {"rewritten_topic", "preserved_easter_egg"}:
                self.assertTrue(row.source_reference.startswith("catalog:"))
                continue
            legacy = re.fullmatch(
                r"legacy:(\d+);topic:([^;]+);variant:([^;]+)",
                row.source_reference,
            )
            self.assertIsNotNone(legacy, row.source_reference)
            source_line = int(legacy.group(1))
            source_topic = legacy.group(2)
            self.assertEqual(variant_id, legacy.group(3))
            self.assertEqual(entry.source_reference, f"legacy:{source_line};topic:{source_topic}")
            self.assertEqual(entry.category, mappings[source_line].category)
            self.assertEqual(source_topic, mappings[source_line].topic_id)
            self.assertEqual(source_topic, row.topic_id)

    def test_every_runtime_category_group_meets_topic_cardinality_contract(self) -> None:
        expected_ranges = {
            "technical": (1, 2),
            "growth": (1, 2),
            "career": (1, 2),
            "daily_care": (2, 3),
            "emotional_reflection": (2, 3),
            "character_life": (3, 5),
            "easter_egg": (1, 1),
            "system_ambient": (5, 5),
        }
        variants = Counter(
            (row.category_group, row.topic_id) for row in self.result.enabled
        )
        self.assertEqual(
            set(expected_ranges),
            {group for group, _topic_id in variants},
        )
        for (group, topic_id), count in variants.items():
            minimum, maximum = expected_ranges[group]
            self.assertGreaterEqual(count, minimum, (group, topic_id, count))
            self.assertLessEqual(count, maximum, (group, topic_id, count))

    def test_technical_growth_and_career_have_meaningful_one_two_topic_mix(self) -> None:
        topic_sizes = Counter(
            (row.category_group, row.topic_id) for row in self.result.enabled
        )
        for group in ("technical", "growth", "career"):
            sizes = [
                count
                for (category_group, _topic_id), count in topic_sizes.items()
                if category_group == group
            ]
            with self.subTest(category_group=group):
                self.assertTrue(sizes)
                self.assertEqual({1, 2}, set(sizes))
                singleton_share = sizes.count(1) / len(sizes)
                self.assertGreaterEqual(singleton_share, 0.10)
                self.assertGreater(sizes.count(2), sizes.count(1))

    def test_task_report_has_one_current_generated_output_truth(self) -> None:
        report = REPORT_PATH.read_text(encoding="utf-8")
        heading = "## Current generated outputs"
        self.assertEqual(1, report.count(heading))
        section = report.split(heading, 1)[1].split("\n## ", 1)[0]
        rows = re.findall(
            r"^\| `([^`]+)` \| ([\d,]+) \| `([0-9a-f]{64})` \|$",
            section,
            flags=re.MULTILINE,
        )
        actual_paths = {
            "persona-corpus-v2.tsv": REPOSITORY_ROOT / "data/optimized/persona-corpus-v2.tsv",
            "persona-corpus-archive.tsv": REPOSITORY_ROOT / "data/optimized/persona-corpus-archive.tsv",
            "persona-corpus-review.tsv": REPOSITORY_ROOT / "data/optimized/persona-corpus-review.tsv",
            "pii-review.tsv": REPOSITORY_ROOT / "reports/pii-review.tsv",
        }
        reported = {
            name: (int(count.replace(",", "")), digest)
            for name, count, digest in rows
        }
        expected = {}
        for name, path in actual_paths.items():
            with path.open(encoding="utf-8") as stream:
                line_count = sum(1 for _line in stream) - 1
            expected[name] = (
                line_count,
                hashlib.sha256(path.read_bytes()).hexdigest(),
            )
        self.assertEqual(expected, reported)
        self.assertNotIn("3,212", report)

    def test_real_archive_recovery_is_exact_and_never_category_wide(self) -> None:
        enabled_by_source: dict[int, set[str]] = {}
        for row in self.result.enabled:
            match = re.match(r"legacy:(\d+);", row.source_reference)
            if match:
                enabled_by_source.setdefault(int(match.group(1)), set()).add(row.text)

        recoverable_sources = set()
        for row in self.result.archive:
            if row.can_recover:
                recoverable_sources.add(row.source_line)
                self.assertIn(row.source_line, enabled_by_source)
                self.assertIn(row.suggested_rewrite, enabled_by_source[row.source_line])
            else:
                self.assertEqual("", row.suggested_rewrite)
        self.assertEqual(set(enabled_by_source), recoverable_sources)

    def test_source_75122_preserves_each_independent_review_risk(self) -> None:
        risks = {
            row.risk_type for row in self.result.review if row.source_line == 75122
        }
        self.assertEqual({"privacy_risk", "future_context_signal"}, risks)
        self.assertEqual(
            len(risks),
            len({row.review_id for row in self.result.review if row.source_line == 75122}),
        )

    def test_catalog_contains_no_semicolon_padded_duplicate_sentence(self) -> None:
        from src.persona_corpus.content_catalog import CONTENT_CATALOG

        complete = {entry.text.rstrip("。！") for entry in CONTENT_CATALOG}
        padded = []
        for entry in CONTENT_CATALOG:
            if "；" not in entry.text:
                continue
            first_clause = entry.text.split("；", 1)[0].rstrip("。！")
            if first_clause in complete:
                padded.append((entry.variant_id, entry.text))
        self.assertEqual([], padded)

    def test_enabled_lines_do_not_assert_unavailable_current_state(self) -> None:
        unsupported = (
            "饭点到了", "困得眼睛都睁不开", "屏幕亮成这样",
            "连续工作这么久", "咖啡喝太晚今晚又要睡不着",
            "手腕酸了", "睡前还盯着报错", "胃不舒服还空腹扛着",
            "空调吹久了",
        )
        offenders = [
            (row.id, row.text)
            for row in self.result.enabled
            if row.required_context == "none"
            and any(marker in row.text for marker in unsupported)
        ]
        self.assertEqual([], offenders)

    def test_known_technical_lines_are_timeless_without_fake_current_context(self) -> None:
        expected = {
            "v2_topic_java_0c686ce39743_observation_365c905b0e89":
                "Java 空指针通常要检查初始化与生命周期。",
            "v2_topic_database_47e099c79fa9_observation_8db719cc42c4":
                "数据库死锁先对齐双方持锁顺序。",
        }
        actual = {
            row.id: row.text
            for row in self.result.enabled
            if row.id in expected
        }
        self.assertEqual(expected, actual)

        old_context_claims = (
            "Java 这个空指针先看对象生命周期。",
            "这次死锁得把双方持锁顺序对出来。",
        )
        enabled_texts = {row.text for row in self.result.enabled}
        self.assertTrue(enabled_texts.isdisjoint(old_context_claims))

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
        short_share = sum(8 <= len(text) <= 16 for text in texts) / len(texts)
        medium_share = sum(17 <= len(text) <= 24 for text in texts) / len(texts)
        long_share = sum(25 <= len(text) <= 36 for text in texts) / len(texts)
        catchphrases = ("哈？", "我靠", "我丢", "真的假的", "本姑娘", "笨蛋", "玥玥")
        catchphrase_share = sum(
            any(marker in text for marker in catchphrases) for text in texts
        ) / len(texts)

        self.assertGreaterEqual(average, 18)
        self.assertLessEqual(average, 26)
        self.assertGreaterEqual(short_share, 0.26)
        self.assertLessEqual(short_share, 0.34)
        self.assertGreaterEqual(medium_share, 0.36)
        self.assertLessEqual(medium_share, 0.44)
        self.assertGreaterEqual(long_share, 0.20)
        self.assertLessEqual(long_share, 0.29)
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

    def test_real_build_avoids_template_ending_dominance(self) -> None:
        texts = [row.text for row in self.result.enabled]
        for width in (4, 6, 8, 10):
            endings = Counter(text[-width:] for text in texts if len(text) >= width)
            phrase, count = endings.most_common(1)[0]
            self.assertLessEqual(
                count / len(texts),
                0.02,
                f"{width}-character ending {phrase!r} appears {count} times",
            )

    def test_cli_double_build_is_byte_identical(self) -> None:
        with tempfile.TemporaryDirectory() as first_dir, tempfile.TemporaryDirectory() as second_dir:
            output_sets: list[dict[str, Path]] = []
            for directory in (first_dir, second_dir):
                root = Path(directory)
                output = root / "data/optimized/persona-corpus-v2.tsv"
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
                output_sets.append(
                    {
                        "v2": output,
                        "archive": output.with_name("persona-corpus-archive.tsv"),
                        "review": output.with_name("persona-corpus-review.tsv"),
                        "pii_review": root / "reports/pii-review.tsv",
                    }
                )

            for name in output_sets[0]:
                first_hash = hashlib.sha256(output_sets[0][name].read_bytes()).hexdigest()
                second_hash = hashlib.sha256(output_sets[1][name].read_bytes()).hexdigest()
                self.assertEqual(first_hash, second_hash, name)


if __name__ == "__main__":
    unittest.main()
