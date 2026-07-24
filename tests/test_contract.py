from __future__ import annotations

import json
import importlib.util
import subprocess
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config/persona-contract.json"
TEST_PROJECT_PATH = (
    ROOT / "tests/CompanionDesktopPet.Tests/CompanionDesktopPet.Tests.csproj"
)


class ReleaseTestProjectContractTests(unittest.TestCase):
    def test_dotnet_test_project_is_explicitly_discoverable_without_restore_artifacts(self) -> None:
        project = ET.parse(TEST_PROJECT_PATH).getroot()
        values = [
            node.text.strip().lower()
            for node in project.findall(".//IsTestProject")
            if node.text is not None
        ]

        self.assertIn(
            "true",
            values,
            "The test project must declare IsTestProject=true so a clean checkout cannot "
            "silently report zero tests when restore artifacts are absent.",
        )


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

    def test_scheduler_config_matches_the_shared_contract(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        scheduler = json.loads(
            (ROOT / "config/persona-scheduler.json").read_text(encoding="utf-8")
        )

        self.assertEqual(
            contract["scheduler"]["category_group_weights"],
            scheduler["category_group_weights"],
        )
        self.assertEqual(
            contract["scheduler"]["output_mode_targets"],
            scheduler["output_mode_targets"],
        )
        self.assertEqual(
            contract["scheduler"]["runtime_limits"],
            scheduler["runtime_limits"],
        )

    def test_output_mode_targets_are_derived_from_group_weights(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        scheduler = contract["scheduler"]
        aggregate = {mode: 0.0 for mode in scheduler["output_mode_targets"]}
        for group, weight in scheduler["category_group_weights"].items():
            aggregate[scheduler["category_group_output_modes"][group]] += weight

        expected = {
            "self_talk": 0.82,
            "ambient": 0.10,
            "user_direct": 0.0,
            "system_observe": 0.08,
        }
        for mode, target in expected.items():
            self.assertAlmostEqual(target, aggregate[mode])
            self.assertAlmostEqual(target, scheduler["output_mode_targets"][mode])

    def test_inventory_size_policy_is_shared_and_explicit(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        self.assertEqual(
            {
                "curated_core": [800, 1200],
                "expanded_runtime": [50000, 60000],
            },
            contract["inventory"],
        )

        from src.persona_corpus.contract import PERSONA_CONTRACT

        self.assertEqual((800, 1200), PERSONA_CONTRACT.inventory["curated_core"])
        self.assertEqual(
            (50000, 60000), PERSONA_CONTRACT.inventory["expanded_runtime"]
        )

    def test_topic_id_is_lineage_metadata_not_a_semantic_scene_dimension(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        lineage = contract["lineage"]

        self.assertEqual("row_editorial_lineage", lineage["topic_id_role"])
        self.assertEqual("may_span_topics", lineage["semantic_group_topic_policy"])
        self.assertEqual("variant_prefix", lineage["editorial_variant_topic_binding"])
        self.assertEqual(
            "source_reference_topic_token",
            lineage["surface_variant_topic_binding"],
        )
        self.assertEqual("catalog_registry", lineage["catalog_variant_topic_binding"])
        self.assertNotIn("topic_id", lineage["semantic_scene_signature_fields"])
        self.assertIn("category_group", lineage["semantic_scene_signature_fields"])
        self.assertIn("weight", lineage["semantic_scene_signature_fields"])

    def test_dry_sharp_scene_eligibility_is_hash_stable(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        dry_sharp = contract["dry_sharp"]

        self.assertEqual("semantic_group", dry_sharp["scene_assignment_field"])
        self.assertEqual(
            "persona-dry-sharp-scene-v1", dry_sharp["scene_hash_namespace"]
        )
        self.assertEqual(0.07, dry_sharp["scene_hash_threshold"])
        self.assertEqual([0.04, 0.06], dry_sharp["scene_inventory_acceptance"])
        self.assertEqual(
            "expanded_runtime", dry_sharp["scene_inventory_enforcement_profile"]
        )
        self.assertEqual("observation_only", dry_sharp["row_inventory_policy"])
        self.assertNotIn("inventory_acceptance", dry_sharp)

    def test_lexical_exposure_contract_is_single_source_and_identity_safe(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        seasoning = contract["lexical_exposure"]["seasoning"]

        self.assertEqual([0.03, 0.06], seasoning["playback_acceptance"])
        self.assertEqual(20, seasoning["recent_window"])
        self.assertEqual(1, seasoning["recent_max"])
        self.assertEqual(
            {"policy": "maximum", "maximum": 0.10},
            seasoning["inventory_profiles"]["curated_core"],
        )
        self.assertEqual(
            {"policy": "observation_only"},
            seasoning["inventory_profiles"]["expanded_runtime"],
        )
        self.assertEqual(
            ["玥玥", "小玥", "雷琳玥"], seasoning["identity_markers_excluded"]
        )
        markers = set(seasoning["substring_markers"]) | set(
            seasoning["token_patterns"]
        )
        self.assertTrue(set(seasoning["identity_markers_excluded"]).isdisjoint(markers))

    def test_csharp_contract_is_generated_and_current(self) -> None:
        generator = ROOT / "tools/generate_persona_contract_cs.py"
        self.assertTrue(generator.is_file(), "C# persona contract generator is missing")

        completed = subprocess.run(
            [sys.executable, str(generator), "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)


if __name__ == "__main__":
    unittest.main()
