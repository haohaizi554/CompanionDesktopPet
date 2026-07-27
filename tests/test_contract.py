from __future__ import annotations

import json
import importlib.util
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from dataclasses import replace
from pathlib import Path
from unittest.mock import patch


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


class CSharpContractGeneratorTests(unittest.TestCase):
    def test_render_emits_the_contract_controlled_relationship_profiles(self) -> None:
        from tools import generate_persona_contract_cs as generator

        rendered = generator.render_contract()

        self.assertIn("ControlledRelationshipProfiles", rendered)
        for profile in (
            "neutral",
            "warm_friend",
            "playful_friend",
            "nickname_easter_egg",
        ):
            with self.subTest(profile=profile):
                self.assertIn(f'"{profile}"', rendered)

    def test_render_preserves_round_trip_double_literals_across_contract_fields(self) -> None:
        from tools import generate_persona_contract_cs as generator

        contract = generator.PERSONA_CONTRACT
        scheduler = dict(contract.scheduler)
        weights = dict(scheduler["category_group_weights"])
        weights["technical"] = 0.123456789
        scheduler["category_group_weights"] = weights
        mode_targets = dict(scheduler["output_mode_targets"])
        mode_targets["self_talk"] = 0.234567891
        scheduler["output_mode_targets"] = mode_targets
        acceptance = dict(scheduler["acceptance"])
        acceptance["easter_egg_playback_ratio"] = (0.612345678, 0.712345678)
        scheduler["acceptance"] = acceptance

        dry_sharp = dict(contract.dry_sharp)
        dry_sharp["scene_hash_threshold"] = 0.345678912
        dry_sharp["scene_inventory_acceptance"] = (0.456789123, 0.567891234)
        dry_sharp["playback_target"] = 0.678912345
        dry_sharp["playback_acceptance"] = (0.789123456, 0.891234567)

        lexical_exposure = dict(contract.lexical_exposure)
        seasoning = dict(lexical_exposure["seasoning"])
        inventory_profiles = dict(seasoning["inventory_profiles"])
        curated_core = dict(inventory_profiles["curated_core"])
        curated_core["maximum"] = 0.312345678
        inventory_profiles["curated_core"] = curated_core
        seasoning["inventory_profiles"] = inventory_profiles
        seasoning["playback_acceptance"] = (0.412345678, 0.512345678)
        lexical_exposure["seasoning"] = seasoning

        manifest = generator.EDITORIAL_MANIFEST
        identities = dict(manifest.identity_easter_eggs)
        line_id, identity = next(iter(identities.items()))
        identities[line_id] = replace(
            identity,
            cooldown_hours=720.123456789,
            weight=0.812345678,
        )

        modified_contract = replace(
            contract,
            scheduler=scheduler,
            dry_sharp=dry_sharp,
            lexical_exposure=lexical_exposure,
        )
        modified_manifest = replace(manifest, identity_easter_eggs=identities)
        with (
            patch.object(generator, "PERSONA_CONTRACT", modified_contract),
            patch.object(generator, "EDITORIAL_MANIFEST", modified_manifest),
        ):
            rendered = generator.render_contract()

        expected_literals = (
            "[DialogueCategoryGroup.Technical] = 0.123456789",
            "[DialogueOutputMode.SelfTalk] = 0.234567891",
            "DrySharpSceneHashThreshold = 0.345678912;",
            "DrySharpSceneInventoryMinimum = 0.456789123;",
            "DrySharpSceneInventoryMaximum = 0.567891234;",
            "DrySharpPlaybackTarget = 0.678912345;",
            "DrySharpPlaybackMinimum = 0.789123456;",
            "DrySharpPlaybackMaximum = 0.891234567;",
            "SeasoningCuratedCoreInventoryMaximum = 0.312345678;",
            "SeasoningPlaybackMinimum = 0.412345678;",
            "SeasoningPlaybackMaximum = 0.512345678;",
            "EasterEggPlaybackMinimum = 0.612345678;",
            "EasterEggPlaybackMaximum = 0.712345678;",
            f", 720.123456789, {identity.max_per_day}, 0.812345678)",
        )
        for literal in expected_literals:
            with self.subTest(literal=literal):
                self.assertIn(literal, rendered)

    def test_render_rejects_non_numeric_and_non_finite_double_values(self) -> None:
        from tools import generate_persona_contract_cs as generator

        for invalid in (
            True,
            "0.123",
            float("nan"),
            float("inf"),
            float("-inf"),
            10**400,
        ):
            with self.subTest(invalid=invalid):
                scheduler = dict(generator.PERSONA_CONTRACT.scheduler)
                weights = dict(scheduler["category_group_weights"])
                weights["technical"] = invalid
                scheduler["category_group_weights"] = weights
                modified_contract = replace(generator.PERSONA_CONTRACT, scheduler=scheduler)

                with patch.object(generator, "PERSONA_CONTRACT", modified_contract):
                    with self.assertRaisesRegex(
                        ValueError,
                        r"scheduler\.category_group_weights\.technical must be a finite number",
                    ):
                        generator.render_contract()


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

    def test_relationship_profiles_are_a_single_immutable_contract_set(self) -> None:
        payload = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))
        expected = {
            "neutral",
            "warm_friend",
            "playful_friend",
            "nickname_easter_egg",
        }

        from src.persona_corpus.contract import PERSONA_CONTRACT

        self.assertEqual(expected, set(payload["controlled_values"]["relationship_profiles"]))
        self.assertEqual(frozenset(expected), PERSONA_CONTRACT.relationship_profiles)
        with self.assertRaises(AttributeError):
            PERSONA_CONTRACT.relationship_profiles.add("exclusive")

    def test_contract_declares_uniform_half_open_context_ranges(self) -> None:
        payload = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        self.assertIn("temporal", payload)
        temporal = payload["temporal"]
        self.assertEqual(
            {
                "time:dawn": [[4, 6]],
                "time:morning": [[6, 11]],
                "time:noon": [[11, 14]],
                "time:afternoon": [[14, 18]],
                "time:evening": [[18, 23]],
                "time:late_night": [[0, 4], [23, 24]],
            },
            temporal["context_token_hours"],
        )
        self.assertEqual([[0, 6], [23, 24]], temporal["daypart_hours"]["late_night"])
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

    def test_release_inventory_uses_exact_published_counts(self) -> None:
        contract = json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

        self.assertEqual(
            {
                "expanded_runtime_rows": 52132,
                "semantic_scene_count": 533,
                "legacy_surface_rows": 51326,
            },
            contract["release_inventory"],
        )

        from src.persona_corpus.contract import PERSONA_CONTRACT

        self.assertEqual(52132, PERSONA_CONTRACT.release_inventory["expanded_runtime_rows"])
        self.assertEqual(533, PERSONA_CONTRACT.release_inventory["semantic_scene_count"])
        self.assertEqual(51326, PERSONA_CONTRACT.release_inventory["legacy_surface_rows"])

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
        privacy = contract["privacy"]

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
        self.assertEqual(["雷琳玥", "小玥", "玥仔", "玥玥"], privacy["pii_markers"])
        self.assertNotIn("identity_markers_excluded", seasoning)
        markers = set(seasoning["substring_markers"]) | set(
            seasoning["token_patterns"]
        )
        self.assertTrue(set(privacy["pii_markers"]).isdisjoint(markers))

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


class AuthoredIdentityContractTests(unittest.TestCase):
    def setUp(self) -> None:
        self._temporary_directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._temporary_directory.cleanup)
        self.temp = Path(self._temporary_directory.name)

    @staticmethod
    def load_contract_json() -> dict[str, object]:
        return json.loads(CONTRACT_PATH.read_text(encoding="utf-8"))

    def write_json(self, raw: dict[str, object]) -> Path:
        path = self.temp / "persona-contract.json"
        path.write_text(json.dumps(raw, ensure_ascii=False), encoding="utf-8")
        return path

    def test_contract_rejects_missing_or_misordered_authored_identity_marker(self) -> None:
        from src.persona_corpus.contract import PersonaContractError, load_persona_contract

        for markers in (
            ["雷琳玥", "小玥", "玥玥"],
            ["小玥", "雷琳玥", "玥仔", "玥玥"],
        ):
            with self.subTest(markers=markers):
                raw = self.load_contract_json()
                raw["authored_identity"]["markers"] = markers
                with self.assertRaisesRegex(
                    PersonaContractError, "authored_identity.*markers"
                ):
                    load_persona_contract(self.write_json(raw))

    def test_contract_rejects_non_session_identity_exposure_policy(self) -> None:
        from src.persona_corpus.contract import PersonaContractError, load_persona_contract

        raw = self.load_contract_json()
        raw["authored_identity"]["session_exposure"]["persist_across_restarts"] = True
        with self.assertRaisesRegex(PersonaContractError, "persist_across_restarts"):
            load_persona_contract(self.write_json(raw))

    def test_contract_rejects_exact_authored_identity_invariant_drift(self) -> None:
        from src.persona_corpus.contract import PersonaContractError, load_persona_contract

        cases = (
            (
                "policy version",
                ("authored_identity", "policy_version"),
                "authored-identity-v2",
                "policy_version",
            ),
            (
                "direct marker batch",
                ("authored_identity", "direct_marker_batches", "b085"),
                "小玥",
                "direct_marker_batches",
            ),
            (
                "easter egg batch range",
                ("authored_identity", "easter_egg_batches", -1),
                "b093",
                "easter_egg_batches",
            ),
            (
                "category",
                ("authored_identity", "category"),
                "Python",
                "authored_identity category",
            ),
            (
                "category group",
                ("authored_identity", "category_group"),
                "technical",
                "authored_identity category",
            ),
            (
                "output mode",
                ("authored_identity", "output_mode"),
                "ambient",
                "authored_identity category",
            ),
            (
                "relationship profiles",
                ("authored_identity", "allowed_relationship_profiles", -1),
                "neutral",
                "allowed_relationship_profiles",
            ),
            (
                "marker placement",
                ("authored_identity", "allow_markers_in_any_category"),
                False,
                "allow_markers_in_any_category",
            ),
            (
                "minimum intervening bubbles",
                (
                    "authored_identity",
                    "session_exposure",
                    "minimum_intervening_bubbles_same_semantic_group",
                ),
                4,
                "session_exposure",
            ),
            (
                "recent bubbles",
                ("authored_identity", "session_exposure", "recent_bubbles_per_semantic_group"),
                9,
                "session_exposure",
            ),
            (
                "direct marker cap",
                (
                    "authored_identity",
                    "session_exposure",
                    "direct_marker_max_per_identity_class",
                ),
                4,
                "session_exposure",
            ),
            (
                "privacy alignment",
                ("privacy", "pii_markers"),
                ["玥玥", "玥仔", "小玥", "雷琳玥"],
                "privacy.pii_markers",
            ),
            (
                "unknown identity key",
                ("authored_identity", "unexpected"),
                True,
                "unexpected key set",
            ),
        )
        for name, path, value, error in cases:
            with self.subTest(invariant=name):
                raw = self.load_contract_json()
                target = raw
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
                with self.assertRaisesRegex(PersonaContractError, error):
                    load_persona_contract(self.write_json(raw))

    def test_loader_exposes_the_frozen_authored_identity_policy(self) -> None:
        from src.persona_corpus.contract import load_persona_contract

        contract = load_persona_contract(CONTRACT_PATH)

        self.assertEqual(("雷琳玥", "小玥", "玥仔", "玥玥"), contract.pii_markers)
        self.assertEqual("玥仔", contract.authored_identity["direct_marker_batches"]["b085"])
        self.assertFalse(contract.authored_identity["session_exposure"]["persist_across_restarts"])
        with self.assertRaises(TypeError):
            contract.authored_identity["session_exposure"]["persist_across_restarts"] = True


if __name__ == "__main__":
    unittest.main()
