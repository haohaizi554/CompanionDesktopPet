from __future__ import annotations

import hashlib
import json
import os
import subprocess
import sys
import unittest
from copy import deepcopy
from pathlib import Path

from jsonschema import Draft202012Validator, ValidationError

from src.persona_corpus.validation import validate_config


ROOT = Path(__file__).resolve().parents[1]
CONFIG_DIR = ROOT / "config"
CONFIG_SCHEMAS = {
    "persona-contract.json": "./schemas/persona-contract.schema.json",
    "persona-scheduler.json": "./schemas/persona-scheduler.schema.json",
    "persona-authorship-manifest.json": "./schemas/persona-authorship-manifest.schema.json",
    "persona-editorial-manifest.json": "./schemas/persona-editorial-manifest.schema.json",
    "persona-review-allowlist.json": "./schemas/persona-review-allowlist.schema.json",
}
JSON_SCHEMA_DRAFT = "https://json-schema.org/draft/2020-12/schema"


class ConfigSchemaContractTests(unittest.TestCase):
    def test_every_public_config_has_a_parseable_resolving_local_schema(self) -> None:
        self.assertEqual(
            set(CONFIG_SCHEMAS),
            {path.name for path in CONFIG_DIR.glob("persona-*.json")},
            "new public persona configs must declare and test a local JSON Schema",
        )
        for filename, expected_reference in CONFIG_SCHEMAS.items():
            with self.subTest(config=filename):
                config_path = CONFIG_DIR / filename
                config = json.loads(config_path.read_text(encoding="utf-8"))

                # This literal is deliberately independent of the generator and contract
                # so a coordinated but breaking schema-version change still fails review.
                self.assertEqual(1, config["schema_version"])
                self.assertEqual(expected_reference, config["$schema"])

                schema_path = (config_path.parent / expected_reference).resolve()
                self.assertTrue(schema_path.is_relative_to(CONFIG_DIR.resolve()))
                schema = json.loads(schema_path.read_text(encoding="utf-8"))
                self.assertEqual(JSON_SCHEMA_DRAFT, schema["$schema"])
                self.assertEqual("object", schema["type"])
                self.assertFalse(schema["additionalProperties"])
                Draft202012Validator.check_schema(schema)
                Draft202012Validator(schema).validate(config)

    def test_scheduler_schema_rejects_incomplete_weights_and_unknown_limits(self) -> None:
        config = json.loads(
            (CONFIG_DIR / "persona-scheduler.json").read_text(encoding="utf-8")
        )
        schema = json.loads(
            (CONFIG_DIR / "schemas/persona-scheduler.schema.json").read_text(
                encoding="utf-8"
            )
        )
        validator = Draft202012Validator(schema)

        missing_group = deepcopy(config)
        del missing_group["category_group_weights"]["growth"]
        unknown_limit = deepcopy(config)
        unknown_limit["runtime_limits"]["surprise_limit"] = 1

        with self.assertRaises(ValidationError):
            validator.validate(missing_group)
        with self.assertRaises(ValidationError):
            validator.validate(unknown_limit)

    def test_authorship_manifest_schema_rejects_missing_batch_and_bad_hash(self) -> None:
        config = json.loads(
            (CONFIG_DIR / "persona-authorship-manifest.json").read_text(encoding="utf-8")
        )
        schema = json.loads(
            (CONFIG_DIR / "schemas/persona-authorship-manifest.schema.json").read_text(
                encoding="utf-8"
            )
        )
        validator = Draft202012Validator(schema)

        missing_batch = deepcopy(config)
        del missing_batch["batches"]["b100"]
        bad_hash = deepcopy(config)
        bad_hash["root_sha256"] = "not-a-sha256"
        unknown_field = deepcopy(config)
        unknown_field["generated_at"] = "nondeterministic"

        for invalid in (missing_batch, bad_hash, unknown_field):
            with self.subTest(invalid=invalid):
                with self.assertRaises(ValidationError):
                    validator.validate(invalid)

    def test_contract_schema_rejects_incomplete_or_invalid_temporal_contract(self) -> None:
        config = json.loads(
            (CONFIG_DIR / "persona-contract.json").read_text(encoding="utf-8")
        )
        schema = json.loads(
            (CONFIG_DIR / "schemas/persona-contract.schema.json").read_text(
                encoding="utf-8"
            )
        )
        validator = Draft202012Validator(schema)

        missing_daypart = deepcopy(config)
        del missing_daypart["temporal"]["daypart_hours"]["morning"]
        invalid_dawn = deepcopy(config)
        invalid_dawn["temporal"]["context_token_hours"]["time:dawn"] = [[6, 4]]
        missing_trigger = deepcopy(config)
        del missing_trigger["temporal"]["context_token_trigger"]["time:dawn"]
        empty_pii_markers = deepcopy(config)
        empty_pii_markers["privacy"]["pii_markers"] = []

        for invalid in (
            missing_daypart,
            invalid_dawn,
            missing_trigger,
            empty_pii_markers,
        ):
            with self.subTest(invalid=invalid["temporal"]):
                with self.assertRaises(ValidationError):
                    validator.validate(invalid)

    def test_contract_schema_rejects_incomplete_tree_weights(self) -> None:
        config = json.loads(
            (CONFIG_DIR / "persona-contract.json").read_text(encoding="utf-8")
        )
        schema = json.loads(
            (CONFIG_DIR / "schemas/persona-contract.schema.json").read_text(
                encoding="utf-8"
            )
        )
        del config["scheduler"]["tree_weights"]["life"]

        with self.assertRaises(ValidationError):
            Draft202012Validator(schema).validate(config)

    def test_contract_schema_rejects_invalid_authored_identity_policy(self) -> None:
        config = json.loads(
            (CONFIG_DIR / "persona-contract.json").read_text(encoding="utf-8")
        )
        schema = json.loads(
            (CONFIG_DIR / "schemas/persona-contract.schema.json").read_text(
                encoding="utf-8"
            )
        )
        validator = Draft202012Validator(schema)

        cases = (
            (
                "missing marker",
                ("authored_identity", "markers"),
                ["雷琳玥", "小玥", "玥玥"],
            ),
            (
                "misordered marker",
                ("authored_identity", "markers"),
                ["小玥", "雷琳玥", "玥仔", "玥玥"],
            ),
            (
                "direct marker batch",
                ("authored_identity", "direct_marker_batches", "b085"),
                "小玥",
            ),
            (
                "easter egg batch range",
                ("authored_identity", "easter_egg_batches", -1),
                "b093",
            ),
            (
                "category",
                ("authored_identity", "category"),
                "Python",
            ),
            (
                "category group",
                ("authored_identity", "category_group"),
                "technical",
            ),
            (
                "output mode",
                ("authored_identity", "output_mode"),
                "ambient",
            ),
            (
                "relationship profiles",
                ("authored_identity", "allowed_relationship_profiles", -1),
                "neutral",
            ),
            (
                "marker placement",
                ("authored_identity", "allow_markers_in_any_category"),
                False,
            ),
            (
                "minimum intervening bubbles",
                (
                    "authored_identity",
                    "session_exposure",
                    "minimum_intervening_bubbles_same_semantic_group",
                ),
                4,
            ),
            (
                "recent bubbles",
                ("authored_identity", "session_exposure", "recent_bubbles_per_semantic_group"),
                9,
            ),
            (
                "direct marker cap",
                (
                    "authored_identity",
                    "session_exposure",
                    "direct_marker_max_per_identity_class",
                ),
                4,
            ),
            (
                "restart persistence",
                ("authored_identity", "session_exposure", "persist_across_restarts"),
                True,
            ),
            (
                "privacy alignment",
                ("privacy", "pii_markers"),
                ["玥玥", "玥仔", "小玥", "雷琳玥"],
            ),
            (
                "unknown identity key",
                ("authored_identity", "unexpected"),
                True,
            ),
        )
        for name, path, value in cases:
            with self.subTest(invariant=name):
                invalid = deepcopy(config)
                target = invalid
                for key in path[:-1]:
                    target = target[key]
                target[path[-1]] = value
                with self.assertRaises(ValidationError):
                    validator.validate(invalid)

    def test_scheduler_provenance_binds_exact_contract_bytes(self) -> None:
        contract_path = CONFIG_DIR / "persona-contract.json"
        scheduler = json.loads(
            (CONFIG_DIR / "persona-scheduler.json").read_text(encoding="utf-8")
        )

        self.assertEqual(
            {
                "path": "config/persona-contract.json",
                "schema_version": 1,
                "sha256": hashlib.sha256(contract_path.read_bytes()).hexdigest(),
            },
            scheduler["derived_from"],
        )
        self.assertFalse(validate_config(scheduler).errors)

    def test_scheduler_validator_rejects_stale_or_partial_provenance(self) -> None:
        scheduler = json.loads(
            (CONFIG_DIR / "persona-scheduler.json").read_text(encoding="utf-8")
        )
        stale = json.loads(json.dumps(scheduler))
        stale["derived_from"]["sha256"] = "0" * 64
        partial = json.loads(json.dumps(scheduler))
        del partial["derived_from"]["schema_version"]

        self.assertIn(
            "config_provenance",
            {issue.code for issue in validate_config(stale).errors},
        )
        self.assertIn(
            "config_provenance",
            {issue.code for issue in validate_config(partial).errors},
        )

    def test_generated_scheduler_is_current(self) -> None:
        completed = subprocess.run(
            [sys.executable, "tools/generate_persona_scheduler.py", "--check"],
            cwd=ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

        self.assertEqual(0, completed.returncode, completed.stdout + completed.stderr)


class StableValidationDigestTests(unittest.TestCase):
    def test_malformed_values_hash_independently_of_mapping_and_set_order(self) -> None:
        from src.persona_corpus.validation_rules.orchestration import (
            _stable_validation_sha256,
        )

        first = {"b": {"z", "a"}, "a": float("nan")}
        second = {"a": float("nan"), "b": {"a", "z"}}

        self.assertEqual(
            _stable_validation_sha256(first, domain="scheduler"),
            _stable_validation_sha256(second, domain="scheduler"),
        )
        self.assertNotEqual(
            _stable_validation_sha256(first, domain="scheduler"),
            _stable_validation_sha256(first, domain="corpus"),
        )

    def test_unsupported_mapping_key_collisions_do_not_restore_insertion_order(self) -> None:
        from src.persona_corpus.validation_rules.orchestration import (
            _stable_validation_sha256,
        )

        class UnsupportedKey:
            pass

        first = {UnsupportedKey(): "z", UnsupportedKey(): "a"}
        second = {UnsupportedKey(): "a", UnsupportedKey(): "z"}

        self.assertEqual(
            _stable_validation_sha256(first, domain="scheduler"),
            _stable_validation_sha256(second, domain="scheduler"),
        )

    def test_malformed_digest_is_stable_across_process_hash_seeds(self) -> None:
        program = """
from src.persona_corpus.validation_rules.orchestration import _stable_validation_sha256
value = {key: payload for key, payload in {('b', frozenset({'z', 'a'})), ('a', float('nan'))}}
print(_stable_validation_sha256(value, domain='scheduler'))
"""
        digests: list[str] = []
        for seed in ("1", "92741"):
            environment = dict(os.environ)
            environment["PYTHONHASHSEED"] = seed
            completed = subprocess.run(
                [sys.executable, "-c", program],
                cwd=ROOT,
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual(0, completed.returncode, completed.stderr)
            digests.append(completed.stdout.strip())

        self.assertEqual(digests[0], digests[1])
        self.assertRegex(digests[0], r"^[0-9a-f]{64}$")


if __name__ == "__main__":
    unittest.main()
