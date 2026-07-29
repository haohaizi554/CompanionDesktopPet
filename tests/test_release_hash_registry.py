from __future__ import annotations

import hashlib
import json
import re
import unittest
from pathlib import Path

from src.persona_corpus.simulation import SUBSEED_DERIVATION_SHA256
from src.persona_corpus.validation import scheduler_config_sha256


ROOT = Path(__file__).resolve().parents[1]
REGISTRY_PATH = (
    ROOT / "docs" / "release" / "2026-07-25-expanded-runtime-release-checklist.md"
)
PERSONA_README_PATH = ROOT / "README-persona-corpus.md"
ROW_PATTERN = re.compile(
    r"^\| (?P<label>[^|]+?) \| `(?P<value>TBD|[0-9a-f]{64})` \|$"
)


def file_sha256(relative_path: str) -> str:
    return hashlib.sha256((ROOT / relative_path).read_bytes()).hexdigest()


class ReleaseHashRegistryTests(unittest.TestCase):
    def test_release_workflow_uses_only_the_version_tag_as_its_title(self) -> None:
        workflow = (ROOT / ".github" / "workflows" / "ci-cd.yml").read_text(
            encoding="utf-8"
        )

        self.assertIn("$releaseTitle = $tag", workflow)
        self.assertNotIn('$releaseTitle = "佳怡桌宠 $tag（Windows x64）"', workflow)

    def registry(self) -> dict[str, str]:
        document = REGISTRY_PATH.read_text(encoding="utf-8")
        section = document.split("## 7. 发布哈希登记", 1)[1].split("### 7.1", 1)[0]
        entries: dict[str, str] = {}
        for line in section.splitlines():
            match = ROW_PATTERN.fullmatch(line)
            if match is not None:
                entries[match.group("label")] = match.group("value")
        return entries

    def test_verified_source_registry_matches_current_bytes_and_semantics(self) -> None:
        source_sha = file_sha256("src/CompanionDesktopPet/Assets/persona-corpus.tsv")
        self.assertEqual(
            source_sha,
            file_sha256("data/source/persona-corpus.original.tsv"),
            "the immutable source and byte copy must remain identical",
        )
        scheduler = json.loads(
            (ROOT / "config" / "persona-scheduler.json").read_text(encoding="utf-8")
        )
        expected = {
            "immutable source / byte copy": source_sha,
            "expanded runtime v2": file_sha256(
                "data/optimized/persona-corpus-v2.tsv"
            ),
            "archive": file_sha256("data/optimized/persona-corpus-archive.tsv"),
            "review": file_sha256("data/optimized/persona-corpus-review.tsv"),
            "PII review": file_sha256("reports/pii-review.tsv"),
            "surface manifest": file_sha256(
                "data/optimized/persona-surface-manifest.tsv"
            ),
            "persona contract": file_sha256("config/persona-contract.json"),
            "scheduler raw bytes": file_sha256("config/persona-scheduler.json"),
            "scheduler semantic binding": scheduler_config_sha256(scheduler),
            "editorial manifest": file_sha256(
                "config/persona-editorial-manifest.json"
            ),
            "subseed derivation v2": SUBSEED_DERIVATION_SHA256,
            "simulation report": file_sha256("reports/simulation-report.md"),
            "validator-facing simulation events": file_sha256(
                "reports/simulation-events.json"
            ),
        }

        registry = self.registry()
        self.assertEqual(set(expected), set(expected) & set(registry))
        for label, actual in expected.items():
            with self.subTest(label=label):
                self.assertNotEqual("TBD", registry[label])
                self.assertEqual(actual, registry[label])

    def test_hash_registry_uses_only_tbd_for_unverified_placeholders(self) -> None:
        section = REGISTRY_PATH.read_text(encoding="utf-8").split(
            "## 7. 发布哈希登记",
            1,
        )[1].split("### 7.1", 1)[0]
        hash_rows = [line for line in section.splitlines() if line.startswith("| ")]
        data_rows = hash_rows[2:]

        self.assertTrue(data_rows)
        self.assertTrue(
            all(ROW_PATTERN.fullmatch(line) is not None for line in data_rows),
            "registry values must be a verified lowercase SHA-256 or the exact TBD token",
        )

    def test_persona_readme_registry_matches_current_tracked_evidence(self) -> None:
        section = PERSONA_README_PATH.read_text(encoding="utf-8").split(
            "## 确定性重建哈希门禁",
            1,
        )[1]
        registry: dict[str, str] = {}
        for line in section.splitlines():
            match = ROW_PATTERN.fullmatch(line)
            if match is not None:
                registry[match.group("label")] = match.group("value")

        expected = {
            "`src/CompanionDesktopPet/Assets/persona-corpus.tsv`": file_sha256(
                "src/CompanionDesktopPet/Assets/persona-corpus.tsv"
            ),
            "`data/source/persona-corpus.original.tsv`": file_sha256(
                "data/source/persona-corpus.original.tsv"
            ),
            "expanded `data/optimized/persona-corpus-v2.tsv`": file_sha256(
                "data/optimized/persona-corpus-v2.tsv"
            ),
            "`data/optimized/persona-corpus-archive.tsv`": file_sha256(
                "data/optimized/persona-corpus-archive.tsv"
            ),
            "`data/optimized/persona-corpus-review.tsv`": file_sha256(
                "data/optimized/persona-corpus-review.tsv"
            ),
            "`reports/pii-review.tsv`": file_sha256("reports/pii-review.tsv"),
            "`data/optimized/persona-surface-manifest.tsv`": file_sha256(
                "data/optimized/persona-surface-manifest.tsv"
            ),
            "`reports/simulation-report.md`": file_sha256(
                "reports/simulation-report.md"
            ),
            "`reports/simulation-events.json`": file_sha256(
                "reports/simulation-events.json"
            ),
        }

        self.assertEqual(set(expected), set(expected) & set(registry))
        for label, actual in expected.items():
            with self.subTest(label=label):
                self.assertEqual(actual, registry[label])


if __name__ == "__main__":
    unittest.main()
