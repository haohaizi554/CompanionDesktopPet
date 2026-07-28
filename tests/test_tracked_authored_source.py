from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from collections import Counter
from pathlib import Path

from src.persona_corpus.authored_catalog import load_authored_catalog
from src.persona_corpus.authored_identity import marker_hits
from src.persona_corpus.contract import PERSONA_CONTRACT
from src.persona_corpus.models import LegacyLine
from src.persona_corpus.normalization import audit_legacy, normalize_text


ROOT = Path(__file__).resolve().parents[1]
AUTHORED_DIR = ROOT / "data" / "authored" / "v1"
MANIFEST_PATH = ROOT / "config" / "persona-authorship-manifest.json"
MANIFEST_TOOL = ROOT / "tools" / "build_authorship_manifest.py"


class TrackedAuthoredSourceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.catalog = load_authored_catalog(AUTHORED_DIR, MANIFEST_PATH)

    def test_tracked_source_has_the_approved_inventory_and_identity_allocation(self) -> None:
        entries = self.catalog.entries

        self.assertEqual(30_000, len(entries))
        self.assertEqual(30_000, len({entry.variant_id for entry in entries}))
        self.assertEqual(3_000, Counter(entry.category_group for entry in entries)["easter_egg"])

        identity_policy = PERSONA_CONTRACT.authored_identity
        for batch_id, assigned_marker in identity_policy["direct_marker_batches"].items():
            batch = tuple(entry for entry in entries if entry.batch_id == batch_id)
            self.assertEqual(300, len(batch), batch_id)
            self.assertTrue(
                all(marker_hits(entry.text) == (assigned_marker,) for entry in batch),
                batch_id,
            )

    def test_tracked_source_has_unique_normalized_text_and_no_question_prompts(self) -> None:
        entries = self.catalog.entries
        normalized = [normalize_text(entry.text) for entry in entries]

        self.assertEqual(30_000, len(set(normalized)))
        self.assertFalse(
            [entry.variant_id for entry in entries if "?" in entry.text or "？" in entry.text]
        )

    def test_tracked_source_has_no_unadjudicated_high_similarity_pairs(self) -> None:
        audit = audit_legacy(
            [
                LegacyLine(
                    source_line=index,
                    category=entry.category,
                    text=entry.text,
                )
                for index, entry in enumerate(self.catalog.entries, start=1)
            ]
        )

        self.assertEqual(0, audit.similar_pair_count, audit.similar_pair_examples)

    def test_tracked_manifest_is_byte_reproducible(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            rebuilt = Path(temporary_directory) / "persona-authorship-manifest.json"
            completed = subprocess.run(
                [
                    sys.executable,
                    str(MANIFEST_TOOL),
                    "--authored-dir",
                    str(AUTHORED_DIR),
                    "--output",
                    str(rebuilt),
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                check=False,
            )

            self.assertEqual(0, completed.returncode, completed.stderr)
            self.assertEqual(MANIFEST_PATH.read_bytes(), rebuilt.read_bytes())


if __name__ == "__main__":
    unittest.main()
