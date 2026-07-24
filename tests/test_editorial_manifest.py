from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from src.persona_corpus.content_catalog import CONTENT_CATALOG
from src.persona_corpus.editorial import (
    EDITORIAL_MANIFEST,
    EditorialManifestError,
    load_editorial_manifest,
)


class EditorialManifestTests(unittest.TestCase):
    def test_catalog_decisions_are_bidirectionally_consistent(self) -> None:
        catalog_ids = {entry.variant_id for entry in CONTENT_CATALOG}
        adjudicated = set(EDITORIAL_MANIFEST.adjudicated_variants)
        retired = set(EDITORIAL_MANIFEST.retired_variants)

        self.assertEqual(63, len(adjudicated))
        self.assertEqual(29, len(retired))
        self.assertTrue(adjudicated <= catalog_ids)
        self.assertTrue(retired.isdisjoint(catalog_ids))
        self.assertTrue(adjudicated.isdisjoint(retired))

    def test_identity_adjudications_are_exact_small_and_privacy_scoped(self) -> None:
        adjudications = tuple(EDITORIAL_MANIFEST.identity_easter_eggs.values())
        marker_counts: dict[str, int] = {}
        for item in adjudications:
            for marker in item.allowed_markers:
                marker_counts[marker] = marker_counts.get(marker, 0) + 1
            self.assertEqual("EasterEgg", item.category)
            self.assertEqual("easter_egg", item.category_group)
            self.assertEqual(1, item.max_per_day)
            self.assertGreaterEqual(item.cooldown_hours, 720)
            self.assertLessEqual(item.weight, 0.1)
            self.assertIn(f";variant:{item.variant_id}", item.source_reference)
            self.assertNotIn("75138", item.source_reference)
            self.assertNotIn("75153", item.source_reference)
            self.assertNotIn("75154", item.source_reference)

        self.assertEqual({"玥玥": 27, "小玥": 1, "雷琳玥": 1}, marker_counts)

    def test_curated_identity_entries_are_real_catalog_variants(self) -> None:
        entries = {entry.variant_id: entry for entry in CONTENT_CATALOG}
        curated = [
            item
            for item in EDITORIAL_MANIFEST.identity_easter_eggs.values()
            if item.source_reference.startswith("catalog:")
        ]
        self.assertEqual(2, len(curated))
        for item in curated:
            entry = entries[item.variant_id]
            self.assertEqual(item.text, entry.text)
            self.assertEqual(item.category, entry.category)
            self.assertEqual(item.category_group, entry.category_group)

    def test_legacy_identity_entries_bind_line_id_variant_reference_and_text(self) -> None:
        legacy = [
            item
            for item in EDITORIAL_MANIFEST.identity_easter_eggs.values()
            if item.source_reference.startswith("legacy:")
        ]
        self.assertEqual(27, len(legacy))
        self.assertEqual(
            {75136, 75137, *range(75139, 75153), *range(75155, 75166)},
            {item.source_line for item in legacy},
        )
        self.assertEqual(len(legacy), len({item.line_id for item in legacy}))
        self.assertEqual(len(legacy), len({item.variant_id for item in legacy}))

    def test_loader_rejects_duplicate_keys(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(
                '{"schema_version":1,"schema_version":1}', encoding="utf-8"
            )
            with self.assertRaises(EditorialManifestError):
                load_editorial_manifest(path)

    def test_loader_binds_authored_identity_text_as_raw_utf8(self) -> None:
        root = Path(__file__).resolve().parents[1]
        payload = json.loads(
            (root / "config/persona-editorial-manifest.json").read_text(
                encoding="utf-8"
            )
        )
        item = payload["identity_easter_eggs"][
            "v2_egg_editorial_full_name_01_3230a1453d30"
        ]
        item["text"] = item["text"][:-1] + "！"
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "manifest.json"
            path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
            with self.assertRaisesRegex(EditorialManifestError, "text hash mismatch"):
                load_editorial_manifest(path)


if __name__ == "__main__":
    unittest.main()
