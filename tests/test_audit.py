from __future__ import annotations

import hashlib
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from src.persona_corpus.loader import (
    CorpusFormatError,
    load_legacy,
    load_v2,
    sha256_file,
)
from src.persona_corpus.models import CorpusLine, LegacyLine
from src.persona_corpus.normalization import audit_legacy, normalize_text


V2_HEADER = (
    "id\tcategory\tcategory_group\ttopic_id\tsemantic_group\toutput_mode\t"
    "trigger\trequired_context\ttone\tinterrupt_cost\tcooldown_hours\t"
    "semantic_cooldown_hours\tmax_per_day\tweight\trequires_reply\tenabled\t"
    "text\tsource_kind\tsource_reference\trewrite_reason"
)


class CorpusTestCase(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._temporary_directory.cleanup)
        self.root = Path(self._temporary_directory.name)

    def write_fixture(self, contents: str, name: str = "fixture.tsv") -> Path:
        path = self.root / name
        path.write_text(contents, encoding="utf-8", newline="")
        return path


class LoaderTests(CorpusTestCase):
    def test_legacy_loader_preserves_source_line_category_and_text(self) -> None:
        path = self.write_fixture("Debugging\tok\nLife\t玥玥在看书。\n")

        self.assertEqual(
            [
                LegacyLine(1, "Debugging", "ok"),
                LegacyLine(2, "Life", "玥玥在看书。"),
            ],
            load_legacy(path),
        )

    def test_bad_row_reports_path_and_line_number(self) -> None:
        path = self.write_fixture("Debugging\tok\nmissing-tab\n")

        with self.assertRaisesRegex(
            CorpusFormatError, rf"{path.name}.*line 2"
        ):
            load_legacy(path)

    def test_empty_legacy_field_is_rejected(self) -> None:
        path = self.write_fixture("Debugging\t\n")

        with self.assertRaisesRegex(CorpusFormatError, r"line 1"):
            load_legacy(path)

    def test_malformed_quoted_row_reports_actual_line_number(self) -> None:
        path = self.write_fixture('Debugging\tok\n"unterminated\ttext\n')

        with self.assertRaisesRegex(CorpusFormatError, r"line 2"):
            load_legacy(path)

    def test_sha256_file_hashes_source_bytes(self) -> None:
        path = self.write_fixture("Debugging\t玥玥。\r\n")

        self.assertEqual(
            hashlib.sha256(path.read_bytes()).hexdigest(), sha256_file(path)
        )

    def test_v2_loader_parses_exact_schema_and_enabled_filter(self) -> None:
        enabled = (
            "line-1\tLife\tcharacter_life\treading\treading-window\tself_talk\t"
            "idle\tany\twarm\t1\t2.5\t8\t3\t1.25\tfalse\ttrue\t"
            "玥玥把书翻到下一页。\trewrite\tlegacy:1\tstandalone"
        )
        disabled = enabled.replace("line-1", "line-2", 1).replace(
            "\ttrue\t玥玥", "\tfalse\t玥玥", 1
        )
        path = self.write_fixture(f"{V2_HEADER}\n{enabled}\n{disabled}\n")

        rows = load_v2(path)

        self.assertEqual(2, len(rows))
        self.assertIsInstance(rows[0], CorpusLine)
        self.assertEqual(1, rows[0].interrupt_cost)
        self.assertEqual(2.5, rows[0].cooldown_hours)
        self.assertEqual(1.25, rows[0].weight)
        self.assertFalse(rows[0].requires_reply)
        self.assertTrue(rows[0].enabled)
        self.assertEqual([rows[0]], load_v2(path, enabled_only=True))

    def test_v2_loader_rejects_wrong_header(self) -> None:
        path = self.write_fixture("id\ttext\nline-1\thello\n")

        with self.assertRaisesRegex(CorpusFormatError, r"line 1"):
            load_v2(path)

    def test_v2_loader_reports_bad_typed_value_line(self) -> None:
        row = (
            "line-1\tLife\tcharacter_life\treading\treading-window\tself_talk\t"
            "idle\tany\twarm\texpensive\t2.5\t8\t3\t1.25\tfalse\ttrue\t"
            "玥玥把书翻到下一页。\trewrite\tlegacy:1\tstandalone"
        )
        path = self.write_fixture(f"{V2_HEADER}\n{row}\n")

        with self.assertRaisesRegex(CorpusFormatError, r"line 2"):
            load_v2(path)


class AuditTests(unittest.TestCase):
    def test_normalize_text_uses_nfkc_and_strips_punctuation_whitespace(self) -> None:
        self.assertEqual("ABC你好吗", normalize_text(" ＡＢＣ，你 好吗？ "))

    def test_audit_detects_normalized_and_question_risks(self) -> None:
        rows = [
            LegacyLine(1, "ProactiveChat", "你现在做什么？"),
            LegacyLine(2, "ProactiveChat", "你现在做什么 ?"),
        ]

        result = audit_legacy(rows)

        self.assertEqual(2, result.question_count)
        self.assertEqual(2, result.high_risk_patterns["你现在"])
        self.assertEqual(1, result.normalized_duplicate_count)

    def test_audit_counts_distributions_patterns_pii_and_examples(self) -> None:
        rows = [
            LegacyLine(7, "Life", "玥玥今天在湖南长沙散步。"),
            LegacyLine(8, "Life", "玥玥今天在湖南长沙看书。"),
            LegacyLine(9, "Debugging", "这个 bug 先看日志。"),
        ]

        result = audit_legacy(rows)

        self.assertEqual(3, result.total_lines)
        self.assertEqual({"Life": 2, "Debugging": 1}, result.category_counts)
        self.assertEqual(2, result.catchphrase_counts["玥玥"])
        self.assertGreaterEqual(result.likely_pii_count, 2)
        self.assertEqual([7, 8], result.likely_pii_examples[:2])
        self.assertEqual(2, result.prefix_counts[4]["玥玥今天"])
        self.assertIn(4, result.suffix_counts)
        self.assertGreaterEqual(result.similar_pair_count, 1)


class AuditCliTests(CorpusTestCase):
    def test_cli_report_contains_metrics_examples_and_source_lines(self) -> None:
        source = self.write_fixture(
            "ProactiveChat\t你现在做什么？\nLife\t玥玥今天在湖南长沙散步。\n"
        )
        output = self.root / "audit.md"
        completed = subprocess.run(
            [sys.executable, "tools/audit_corpus.py", "--input", str(source), "--output", str(output)],
            cwd=Path(__file__).resolve().parents[1],
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stderr)
        report = output.read_text(encoding="utf-8")
        self.assertIn("Total lines | 2", report)
        self.assertIn("## Category distribution", report)
        self.assertIn("## Prefix distribution", report)
        self.assertIn("## Suffix distribution", report)
        self.assertIn("source line 1", report)
        self.assertIn("source line 2", report)


if __name__ == "__main__":
    unittest.main()
