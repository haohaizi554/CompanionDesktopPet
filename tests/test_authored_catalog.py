from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from src.persona_corpus.authored_catalog import (
    AUTHORED_HEADER,
    EXPECTED_BATCH_IDS,
    load_authored_catalog,
    parse_authored_batches,
)


ROOT = Path(__file__).resolve().parents[1]
MANIFEST_TOOL = ROOT / "tools" / "build_authorship_manifest.py"


def _fixture_category(batch_id: str) -> tuple[str, str, str]:
    number = int(batch_id.removeprefix("b"))
    if number <= 18:
        return ("Debugging", "technical", "self_talk")
    if number <= 28:
        return ("Study", "growth", "self_talk")
    if number <= 35:
        return ("Career", "career", "self_talk")
    if number <= 45:
        return ("DailyCare", "daily_care", "ambient")
    if number <= 55:
        return ("EmotionalSupport", "emotional_reflection", "self_talk")
    if number <= 82:
        return ("ProactiveChat", "character_life", "self_talk")
    if number <= 92:
        return ("EasterEgg", "easter_egg", "self_talk")
    return ("SystemAmbient", "system_ambient", "system_observe")


def _row(batch_id: str, ordinal: int) -> tuple[str, ...]:
    category, category_group, output_mode = _fixture_category(batch_id)
    return (
        f"authored.{batch_id}.technical.fixture.entry.{ordinal:04d}",
        batch_id,
        category,
        category_group,
        f"technical.fixture.{batch_id}",
        "fixture_entry",
        f"technical.fixture.{batch_id}",
        output_mode,
        "any",
        "none",
        "dry",
        "1",
        "24",
        "48",
        "1",
        "1",
        "neutral",
        f"这是 {batch_id} 的第 {ordinal} 条独立测试语料。",
        "approved",
    )


def _write_batch(authored_dir: Path, batch_id: str, row_count: int = 300) -> None:
    lines = ["\t".join(AUTHORED_HEADER)]
    lines.extend("\t".join(_row(batch_id, ordinal)) for ordinal in range(1, row_count + 1))
    (authored_dir / f"{batch_id}.tsv").write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_valid_authored_fixture(root: Path) -> tuple[Path, Path]:
    authored_dir = root / "authored"
    authored_dir.mkdir()
    for batch_id in EXPECTED_BATCH_IDS:
        _write_batch(authored_dir, batch_id)

    manifest = root / "authorship-manifest.json"
    completed = subprocess.run(
        [
            sys.executable,
            str(MANIFEST_TOOL),
            "--authored-dir",
            str(authored_dir),
            "--output",
            str(manifest),
        ],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=False,
    )
    if completed.returncode != 0:
        raise AssertionError(
            "manifest fixture generation failed:\n"
            f"stdout:\n{completed.stdout}\n"
            f"stderr:\n{completed.stderr}"
        )
    return authored_dir, manifest


def mutate_first_text(path: Path) -> None:
    lines = path.read_text(encoding="utf-8").splitlines()
    fields = lines[1].split("\t")
    text_index = AUTHORED_HEADER.index("text")
    fields[text_index] = "这条文本已经被篡改。"
    lines[1] = "\t".join(fields)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


class AuthoredCatalogTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls._temporary_directory = tempfile.TemporaryDirectory()
        cls.root = Path(cls._temporary_directory.name)
        cls.authored_dir, cls.manifest_path = write_valid_authored_fixture(cls.root)

    @classmethod
    def tearDownClass(cls) -> None:
        cls._temporary_directory.cleanup()

    def test_load_authored_catalog_requires_all_100_300_row_batches(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            with self.assertRaisesRegex(ValueError, "expected 100 batches"):
                load_authored_catalog(root / "authored", root / "manifest.json")

    def test_relationship_profile_allowlist_is_the_shared_contract_instance(self) -> None:
        from src.persona_corpus.authored_catalog import RELATIONSHIP_PROFILES
        from src.persona_corpus.contract import PERSONA_CONTRACT

        self.assertIs(RELATIONSHIP_PROFILES, PERSONA_CONTRACT.relationship_profiles)

    def test_parse_authored_batches_rejects_a_group_output_mode_mismatch(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b001.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            for line_index in range(1, len(lines)):
                fields = lines[line_index].split("\t")
                fields[AUTHORED_HEADER.index("output_mode")] = "ambient"
                lines[line_index] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, r"output_mode.*technical"):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_an_unknown_tone(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b001.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("tone")] = "bright"
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "tone must be one of"):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_an_unknown_trigger(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b001.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("trigger")] = "surprise"
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "trigger must be one of"):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_an_unknown_required_context(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b001.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("required_context")] = "unknown_context"
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "required_context must"):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_category_group_inventory_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b100.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            for line_index in range(1, len(lines)):
                fields = lines[line_index].split("\t")
                fields[AUTHORED_HEADER.index("category")] = "DailyCare"
                fields[AUTHORED_HEADER.index("category_group")] = "daily_care"
                fields[AUTHORED_HEADER.index("output_mode")] = "ambient"
                lines[line_index] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "category_group inventory"):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_semantic_group_metadata_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b002.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("semantic_group")] = _row(
                "b001", 1
            )[AUTHORED_HEADER.index("semantic_group")]
            fields[AUTHORED_HEADER.index("relationship_profile")] = "playful_friend"
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(
                ValueError, r"semantic_group.*relationship_profile"
            ):
                parse_authored_batches(authored_dir)

    def test_parse_authored_batches_rejects_semantic_group_cooldown_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, _ = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b002.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("semantic_group")] = _row(
                "b001", 1
            )[AUTHORED_HEADER.index("semantic_group")]
            fields[AUTHORED_HEADER.index("semantic_cooldown_hours")] = "49"
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(
                ValueError, r"semantic_group.*semantic_cooldown_hours"
            ):
                parse_authored_batches(authored_dir)

    def test_load_authored_catalog_rejects_manifest_text_hash_drift(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, manifest = write_valid_authored_fixture(Path(temporary_directory))
            mutate_first_text(authored_dir / "b001.tsv")
            with self.assertRaisesRegex(ValueError, "text_sha256"):
                load_authored_catalog(authored_dir, manifest)

    def test_parse_authored_batches_sorts_entries_and_rejects_an_unapproved_row(self) -> None:
        entries = parse_authored_batches(self.authored_dir)

        self.assertEqual(30_000, len(entries))
        self.assertEqual(("b001", "authored.b001.technical.fixture.entry.0001"), (entries[0].batch_id, entries[0].variant_id))
        self.assertEqual(("b100", "authored.b100.technical.fixture.entry.0300"), (entries[-1].batch_id, entries[-1].variant_id))

        path = self.authored_dir / "b100.tsv"
        original = path.read_text(encoding="utf-8")
        try:
            lines = original.splitlines()
            fields = lines[-1].split("\t")
            fields[AUTHORED_HEADER.index("review_status")] = "draft"
            lines[-1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "review_status"):
                parse_authored_batches(self.authored_dir)
        finally:
            path.write_text(original, encoding="utf-8")

    def test_load_authored_catalog_rejects_duplicate_text_even_when_ids_differ(self) -> None:
        with tempfile.TemporaryDirectory() as temporary_directory:
            authored_dir, manifest = write_valid_authored_fixture(Path(temporary_directory))
            path = authored_dir / "b002.tsv"
            lines = path.read_text(encoding="utf-8").splitlines()
            fields = lines[1].split("\t")
            fields[AUTHORED_HEADER.index("text")] = _row("b001", 1)[AUTHORED_HEADER.index("text")]
            lines[1] = "\t".join(fields)
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")

            with self.assertRaisesRegex(ValueError, "duplicate text"):
                load_authored_catalog(authored_dir, manifest)

    def test_manifest_builder_writes_canonical_complete_inventory(self) -> None:
        manifest = json.loads(self.manifest_path.read_text(encoding="utf-8"))

        self.assertEqual("persona-authorship-manifest-v1", manifest["format"])
        self.assertEqual(100, manifest["batch_count"])
        self.assertEqual(300, manifest["rows_per_batch"])
        self.assertEqual(30_000, manifest["total_rows"])
        self.assertEqual(list(EXPECTED_BATCH_IDS), list(manifest["batches"]))
        self.assertEqual(list(AUTHORED_HEADER), manifest["authored_header"])
        self.assertEqual(64, len(manifest["root_sha256"]))
        self.assertTrue(all(batch["row_count"] == 300 for batch in manifest["batches"].values()))

    def test_manifest_builder_writes_exact_utf8_lf_bytes(self) -> None:
        manifest_bytes = self.manifest_path.read_bytes()
        manifest = json.loads(manifest_bytes.decode("utf-8"))
        expected_bytes = (
            json.dumps(manifest, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
        ).encode("utf-8")

        self.assertEqual(expected_bytes, manifest_bytes)
        self.assertNotIn(b"\r", manifest_bytes)

    def test_load_authored_catalog_rejects_non_integer_manifest_inventory_counts(self) -> None:
        cases = (
            ("batch_count", 100, 100.0),
            ("batch_count", 100, True),
            ("rows_per_batch", 300, 300.0),
            ("rows_per_batch", 300, True),
            ("total_rows", 30_000, 30_000.0),
            ("total_rows", 30_000, True),
        )
        for field_name, expected_value, invalid_value in cases:
            with self.subTest(field_name=field_name, invalid_value=invalid_value):
                manifest_payload = json.loads(self.manifest_path.read_text(encoding="utf-8"))
                manifest_payload[field_name] = invalid_value
                manifest_path = self.root / f"non-integer-{field_name}-{type(invalid_value).__name__}.json"
                manifest_path.write_text(json.dumps(manifest_payload), encoding="utf-8")

                with self.assertRaisesRegex(
                    ValueError,
                    rf"{field_name} must be an integer {expected_value}",
                ):
                    load_authored_catalog(self.authored_dir, manifest_path)

    def test_load_authored_catalog_rejects_non_integer_batch_row_count(self) -> None:
        for invalid_value in (300.0, True):
            with self.subTest(invalid_value=invalid_value):
                manifest_payload = json.loads(self.manifest_path.read_text(encoding="utf-8"))
                manifest_payload["batches"]["b001"]["row_count"] = invalid_value
                manifest_path = self.root / f"non-integer-row-count-{type(invalid_value).__name__}.json"
                manifest_path.write_text(json.dumps(manifest_payload), encoding="utf-8")

                with self.assertRaisesRegex(
                    ValueError,
                    r"batch b001 row_count must be an integer 300",
                ):
                    load_authored_catalog(self.authored_dir, manifest_path)

    def test_ledger_rows_are_one_to_one_and_hash_bound(self) -> None:
        catalog = load_authored_catalog(self.authored_dir, self.manifest_path)
        ledger = list(catalog.ledger_rows())

        self.assertEqual(30_000, len(ledger))
        self.assertEqual(30_000, len({row.variant_id for row in ledger}))
        self.assertEqual(catalog.root_sha256, ledger[0].root_sha256)
        self.assertEqual("approved", ledger[0].review_status)
        self.assertEqual("neutral", ledger[0].relationship_profile)

    def test_load_authored_catalog_rejects_a_manifest_with_unexpected_ledger_batch(self) -> None:
        manifest_payload = json.loads(self.manifest_path.read_text(encoding="utf-8"))
        manifest_payload["batches"]["b001"]["row_count"] = 299
        mutated_manifest = self.root / "mutated-manifest.json"
        mutated_manifest.write_text(
            json.dumps(manifest_payload, ensure_ascii=False, sort_keys=True, indent=2) + "\n",
            encoding="utf-8",
        )

        with self.assertRaisesRegex(ValueError, "row_count"):
            load_authored_catalog(self.authored_dir, mutated_manifest)


if __name__ == "__main__":
    unittest.main()
