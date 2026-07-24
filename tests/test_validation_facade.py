from __future__ import annotations

import pickle
import subprocess
import sys
import unittest
from pathlib import Path

from src.persona_corpus import validation
from tests.test_validation import valid_config, valid_line


ROOT = Path(__file__).resolve().parents[1]
VALIDATION_PATH = ROOT / "src" / "persona_corpus" / "validation.py"


class ValidationFacadeContractTests(unittest.TestCase):
    def test_facade_stays_below_seven_hundred_lines(self) -> None:
        line_count = len(VALIDATION_PATH.read_text(encoding="utf-8").splitlines())

        self.assertLess(line_count, 700)

    def test_public_and_existing_private_compatibility_names_remain_importable(self) -> None:
        self.assertEqual(
            [
                "FORMAT_ERROR_CODES",
                "VALIDATION_GROUPS",
                "ValidationInputError",
                "ValidationIssue",
                "ValidationReport",
                "format_report",
                "load_json_object",
                "normalized_text_sha256",
                "scheduler_config_sha256",
                "validate_config",
                "validate_corpus",
                "validate_file",
            ],
            validation.__all__,
        )
        compatibility_names = (
            "CONTEXT_TOKEN_PATTERN",
            "ID_PATTERN",
            "DIRECT_STATE_PATTERNS",
            "TECHNICAL_CURRENT_PATTERNS",
            "PII_MARKERS",
            "PII_PATTERNS",
            "COMMON_CHINESE_SURNAMES",
            "COMMON_CHINESE_GIVEN_NAMES",
            "NAME_CONTEXT_MARKERS",
            "CONTEXTUAL_CHINESE_NAME_PATTERN",
            "LABELED_CHINESE_NAME_PATTERN",
            "STRONG_EMOTION_MARKERS",
            "SURFACE_CATCHPHRASE_HARD_MAX",
            "SURFACE_OPENING_HARD_MAX",
            "SURFACE_ENDING_HARD_MAX",
            "SURFACE_CARTESIAN_TOPIC_HARD_MAX",
            "SURFACE_TOPIC_FACE_HARD_MAX",
            "ALLOWLIST_KEYS",
            "ALLOWLISTABLE_CODES",
            "TOP_LEVEL_CONFIG_KEYS",
            "RUNTIME_LIMIT_KEYS",
            "SIMULATION_KEYS",
            "SIMULATION_ATTEMPT_KEYS",
            "SIMULATION_CONTEXT_KEYS",
            "SIMULATION_EVENTS",
            "SIMULATION_DAYPARTS",
            "_Issues",
            "_is_finite_number",
            "_is_integer",
            "_json_pairs",
            "_reject_json_constant",
            "_validate_weight_map",
            "_validate_output_targets",
            "_valid_int_limit",
            "_validate_runtime_limits",
            "_validate_context_and_triggers",
            "_required_context_tokens",
            "_has_identity_marker",
            "_looks_like_non_identity_pii",
            "_trigger_context_conflict",
            "_validate_line",
            "_has_cartesian_grid",
            "_cartesian_grid_issues",
            "_surface_inventory_issues",
            "_distribution_issues",
            "_SimulationAttempt",
            "_SimulationOutput",
            "_expected_daypart",
            "_parse_simulation_timestamp",
            "_valid_optional_boolean",
            "_simulation_context_valid",
            "_simulation_trigger_matches",
            "_simulation_context_token_matches",
            "_simulation_issues",
            "_apply_allowlist",
        )

        missing = [name for name in compatibility_names if not hasattr(validation, name)]
        self.assertEqual([], missing)

    def test_selector_simulation_and_surface_variants_import_in_fresh_processes(self) -> None:
        module_orders = (
            ("surface_variants", "simulation", "selector", "validation"),
            ("validation", "selector", "surface_variants", "simulation"),
            ("simulation", "validation", "surface_variants", "selector"),
        )
        for order in module_orders:
            imports = "; ".join(
                f"import src.persona_corpus.{module}" for module in order
            )
            command = (
                imports
                + "; from src.persona_corpus.validation import "
                + "ValidationIssue, ValidationReport, ValidationInputError, "
                + "validate_config, validate_corpus, validate_file, format_report, "
                + "normalized_text_sha256, scheduler_config_sha256, load_json_object, "
                + "DIRECT_STATE_PATTERNS, TECHNICAL_CURRENT_PATTERNS, "
                + "_has_identity_marker, _looks_like_non_identity_pii"
            )
            completed = subprocess.run(
                [sys.executable, "-c", command],
                cwd=ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
            )
            with self.subTest(order=order):
                self.assertEqual(0, completed.returncode, completed.stderr)

    def test_simulation_scenarios_imports_before_validation_in_fresh_process(self) -> None:
        completed = subprocess.run(
            [
                sys.executable,
                "-c",
                "import src.persona_corpus.simulation_core.scenarios; "
                "import src.persona_corpus.validation",
            ],
            cwd=ROOT,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )

        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_public_symbols_keep_facade_introspection_and_pickle_contract(self) -> None:
        public_callables = (
            validation.ValidationInputError,
            validation.ValidationIssue,
            validation.ValidationReport,
            validation.format_report,
            validation.load_json_object,
            validation.normalized_text_sha256,
            validation.scheduler_config_sha256,
            validation.validate_config,
            validation.validate_corpus,
            validation.validate_file,
        )
        for value in public_callables:
            with self.subTest(value=value.__name__):
                self.assertEqual("src.persona_corpus.validation", value.__module__)
                self.assertIs(value, pickle.loads(pickle.dumps(value)))

        issue = validation.ValidationIssue("example", "message", "line", 2)
        self.assertEqual(issue, pickle.loads(pickle.dumps(issue)))

    def test_issue_order_format_and_warning_wording_are_characterized(self) -> None:
        rows = [
            valid_line(id="z", topic_id="z", semantic_group="z", text="?"),
            valid_line(id="a", topic_id="a", semantic_group="a", text="?"),
        ]
        expected_signature = [
            ("duplicate_normalized_text", "a", "normalized text occurs 2 times: ''"),
            ("duplicate_text", "a", "enabled text occurs 2 times: '?'"),
            (
                "normalized_text_empty",
                "a",
                "text is empty after NFKC/casefold/punctuation/format normalization",
            ),
            (
                "normalized_text_empty",
                "z",
                "text is empty after NFKC/casefold/punctuation/format normalization",
            ),
            ("question", "a", "original text contains a question mark"),
            ("question", "z", "original text contains a question mark"),
            (
                "simulation_missing",
                "",
                "Task 6 structured 30-day simulation JSON is not supplied yet; "
                "static gates still ran.",
            ),
        ]

        reports = [
            validation.validate_corpus(candidate, valid_config(), {"exceptions": []})
            for candidate in (rows, list(reversed(rows)))
        ]
        for report in reports:
            signature = [
                (issue.code, issue.line_id, issue.message)
                for issue in report.errors + report.warnings
            ]
            self.assertEqual(expected_signature, signature)

        self.assertEqual(
            "Validation: 6 hard errors, 1 warnings\n"
            "ERROR duplicate_normalized_text [a]: normalized text occurs 2 times: ''\n"
            "ERROR duplicate_text [a]: enabled text occurs 2 times: '?'\n"
            "ERROR normalized_text_empty [a] [row 3]: text is empty after "
            "NFKC/casefold/punctuation/format normalization\n"
            "ERROR normalized_text_empty [z] [row 2]: text is empty after "
            "NFKC/casefold/punctuation/format normalization\n"
            "ERROR question [a] [row 3]: original text contains a question mark\n"
            "ERROR question [z] [row 2]: original text contains a question mark\n"
            "WARNING simulation_missing: Task 6 structured 30-day simulation JSON is "
            "not supplied yet; static gates still ran.",
            validation.format_report(reports[0]),
        )

    def test_semantic_json_and_normalized_text_hashes_are_stable(self) -> None:
        self.assertEqual(
            "18645950aba114ae00830224ac0c8a53c5ae359a24335c7da6840a925e475e67",
            validation.scheduler_config_sha256(valid_config()),
        )
        self.assertEqual(
            "936a185caaa266bb9cbe981e9e05cb78cd732b0b3280eb944412bb6f8f8f07af",
            validation.normalized_text_sha256("Hello, world!"),
        )


if __name__ == "__main__":
    unittest.main()
