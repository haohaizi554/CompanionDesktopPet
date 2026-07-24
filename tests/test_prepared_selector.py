from __future__ import annotations

import random
import time
import unittest
from collections.abc import Iterator, Sequence
from dataclasses import replace
from datetime import datetime
from zoneinfo import ZoneInfo

from src.persona_corpus.context import PersonaContext
from src.persona_corpus.history import SelectionHistory
from src.persona_corpus.models import CorpusLine
from src.persona_corpus.selector import PreparedCorpus, prepare_corpus, select_line


NOW = datetime(2026, 7, 24, 10, 0, tzinfo=ZoneInfo("Asia/Shanghai"))


def line(line_id: str, semantic_group: str, **overrides: object) -> CorpusLine:
    values: dict[str, object] = {
        "id": line_id,
        "category": "CharacterLife",
        "category_group": "character_life",
        "topic_id": semantic_group,
        "semantic_group": semantic_group,
        "output_mode": "self_talk",
        "trigger": "any",
        "required_context": "none",
        "tone": "dry",
        "interrupt_cost": 0,
        "cooldown_hours": 1.0,
        "semantic_cooldown_hours": 1.0,
        "max_per_day": 1,
        "weight": 1.0,
        "requires_reply": False,
        "enabled": True,
        "text": f"{semantic_group} 的第 {line_id} 条表述。",
        "source_kind": "legacy_surface_variant",
        "source_reference": f"catalog:test;variant:{line_id}",
        "rewrite_reason": "prepared selector fixture",
    }
    values.update(overrides)
    return CorpusLine(**values)  # type: ignore[arg-type]


def context() -> PersonaContext:
    return PersonaContext.from_datetime(NOW, minutes_since_last_output=600)


class CountingSequence(Sequence[CorpusLine]):
    def __init__(self, rows: Sequence[CorpusLine]) -> None:
        self._rows = tuple(rows)
        self.iterations = 0

    def __len__(self) -> int:
        return len(self._rows)

    def __getitem__(self, index):
        return self._rows[index]

    def __iter__(self) -> Iterator[CorpusLine]:
        self.iterations += 1
        return iter(self._rows)


class PreparedCorpusTests(unittest.TestCase):
    def test_prepare_builds_one_sorted_scene_per_semantic_group(self) -> None:
        rows = [
            line("b-2", "scene.b"),
            line("a-1", "scene.a"),
            line("b-1", "scene.b"),
        ]

        prepared = prepare_corpus(list(reversed(rows)))

        self.assertIsInstance(prepared, PreparedCorpus)
        self.assertEqual(3, prepared.input_row_count)
        self.assertEqual(3, prepared.variant_count)
        self.assertEqual(2, prepared.scene_count)
        self.assertEqual(
            ["scene.a", "scene.b"],
            [scene.semantic_group for scene in prepared.scenes],
        )
        self.assertEqual(
            ["b-1", "b-2"],
            [row.id for row in prepared.scenes[1].variants],
        )

    def test_inconsistent_scene_metadata_is_rejected_as_a_whole(self) -> None:
        good = line("good", "scene.good")
        first = line("bad-1", "scene.bad")
        inconsistent = replace(first, id="bad-2", weight=0.5)

        prepared = prepare_corpus([good, first, inconsistent])

        self.assertEqual(["scene.good"], [scene.semantic_group for scene in prepared.scenes])
        self.assertEqual(("scene.bad",), prepared.rejected_semantic_groups)
        self.assertEqual(1, prepared.variant_count)

    def test_duplicate_ids_are_removed_before_scene_indexing(self) -> None:
        duplicate = line("duplicate", "scene.duplicate")
        prepared = prepare_corpus(
            [
                line("safe", "scene.safe"),
                duplicate,
                replace(duplicate, semantic_group="scene.other", topic_id="scene.other"),
            ]
        )

        self.assertEqual(["safe"], [row.id for scene in prepared.scenes for row in scene.variants])
        self.assertEqual(("duplicate",), prepared.duplicate_ids)

    def test_prepared_selection_never_reiterates_the_original_fifty_thousand_rows(self) -> None:
        source = CountingSequence(
            [
                line(f"scene-{scene}-variant-{variant}", f"scene.{scene}")
                for scene in range(50)
                for variant in range(20)
            ]
        )
        prepared = prepare_corpus(source)

        for seed in range(100):
            selected = select_line(
                prepared,
                context(),
                SelectionHistory(),
                NOW,
                seed=seed,
            )
            self.assertIsNotNone(selected)

        self.assertEqual(1, source.iterations)

    def test_scene_choice_is_unchanged_when_one_scene_gains_many_surface_variants(self) -> None:
        one_each = prepare_corpus(
            [line("a-0", "scene.a"), line("b-0", "scene.b")]
        )
        expanded = prepare_corpus(
            [line("a-0", "scene.a")]
            + [line(f"b-{index}", "scene.b") for index in range(100)]
        )

        for seed in range(200):
            first = select_line(one_each, context(), SelectionHistory(), NOW, seed=seed)
            second = select_line(expanded, context(), SelectionHistory(), NOW, seed=seed)
            with self.subTest(seed=seed):
                self.assertIsNotNone(first)
                self.assertIsNotNone(second)
                self.assertEqual(first.row.semantic_group, second.row.semantic_group)

    def test_surface_stage_reaches_alternatives_without_global_random_mutation(self) -> None:
        prepared = prepare_corpus(
            [line(f"variant-{index}", "scene.only") for index in range(40)]
        )
        random.seed(2407)
        before = random.getstate()

        selected_ids = {
            select_line(prepared, context(), SelectionHistory(), NOW, seed=seed).row.id
            for seed in range(200)
        }

        self.assertGreaterEqual(len(selected_ids), 35)
        self.assertEqual(before, random.getstate())

    def test_fifty_thousand_row_prepare_and_repeated_selection_have_bounded_cost(self) -> None:
        rows = [
            line(f"scene-{scene}-variant-{variant}", f"scene.{scene}")
            for scene in range(250)
            for variant in range(200)
        ]

        prepare_started = time.perf_counter()
        prepared = prepare_corpus(rows)
        prepare_seconds = time.perf_counter() - prepare_started
        selection_started = time.perf_counter()
        for seed in range(100):
            self.assertIsNotNone(
                select_line(prepared, context(), SelectionHistory(), NOW, seed=seed)
            )
        selection_seconds = time.perf_counter() - selection_started

        self.assertEqual(50_000, prepared.variant_count)
        self.assertEqual(250, prepared.scene_count)
        self.assertLess(prepare_seconds, 5.0)
        self.assertLess(selection_seconds, 5.0)


if __name__ == "__main__":
    unittest.main()
