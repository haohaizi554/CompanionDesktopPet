from __future__ import annotations

import ast
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CATALOG_PATH = ROOT / "src/persona_corpus/content_catalog.py"


class ContentCatalogIntegrityTests(unittest.TestCase):
    def test_catalog_runtime_integrity_does_not_depend_on_assert_statements(self) -> None:
        tree = ast.parse(CATALOG_PATH.read_text(encoding="utf-8"), filename=str(CATALOG_PATH))

        self.assertEqual([], [node.lineno for node in ast.walk(tree) if isinstance(node, ast.Assert)])

    def test_duplicate_catalog_text_is_rejected_even_under_python_optimized_mode(self) -> None:
        command = (
            "from src.persona_corpus.content_catalog import "
            "CONTENT_CATALOG, _validate_catalog_integrity; "
            "_validate_catalog_integrity((CONTENT_CATALOG[0],) * 800)"
        )

        completed = subprocess.run(
            [sys.executable, "-O", "-c", command],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )

        self.assertNotEqual(0, completed.returncode)
        self.assertIn("duplicate text", completed.stderr)


if __name__ == "__main__":
    unittest.main()
