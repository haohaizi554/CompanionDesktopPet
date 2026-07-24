from __future__ import annotations

import json
import importlib.util
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config/persona-contract.json"


class PersonaContractFileTests(unittest.TestCase):
    def test_repository_has_one_machine_readable_persona_contract(self) -> None:
        self.assertTrue(CONTRACT_PATH.is_file(), "config/persona-contract.json is missing")

        payload = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        self.assertEqual(1, payload["schema_version"])
        self.assertEqual(
            {
                "technical",
                "growth",
                "career",
                "daily_care",
                "emotional_reflection",
                "character_life",
                "easter_egg",
                "system_ambient",
            },
            set(payload["category_groups"]),
        )
        self.assertEqual("career", payload["categories"]["Career"])
        self.assertEqual("growth", payload["categories"]["Study"])
        self.assertEqual("growth", payload["categories"]["EnglishPractice"])

    def test_python_loader_exposes_immutable_taxonomy(self) -> None:
        self.assertIsNotNone(
            importlib.util.find_spec("src.persona_corpus.contract"),
            "src.persona_corpus.contract is missing",
        )

        from src.persona_corpus.contract import (
            CATEGORY_GROUP_BY_CATEGORY,
            CATEGORY_GROUPS,
            category_group_for,
        )

        self.assertEqual("career", category_group_for("Career"))
        self.assertEqual("growth", category_group_for("Study"))
        self.assertEqual("growth", category_group_for("EnglishPractice"))
        self.assertEqual(set(CATEGORY_GROUPS), set(CATEGORY_GROUP_BY_CATEGORY.values()))
        with self.assertRaises(TypeError):
            CATEGORY_GROUP_BY_CATEGORY["Career"] = "technical"  # type: ignore[index]

    def test_contract_declares_dawn_inside_late_night_runtime_daypart(self) -> None:
        payload = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        self.assertIn("temporal", payload)
        temporal = payload["temporal"]
        self.assertEqual([4, 6], temporal["context_token_hours"]["time:dawn"])
        self.assertEqual("late_night", temporal["context_token_trigger"]["time:dawn"])


if __name__ == "__main__":
    unittest.main()
