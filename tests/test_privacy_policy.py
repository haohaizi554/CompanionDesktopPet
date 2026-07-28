from __future__ import annotations

import ast
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


class PrivacyPolicyContractTests(unittest.TestCase):
    def test_known_identity_markers_are_the_immutable_contract_source(self) -> None:
        """Changing the contract marker set must change every privacy consumer together."""
        from src.persona_corpus.contract import PERSONA_CONTRACT
        from src.persona_corpus.privacy import PII_MARKERS, classify_pii

        expected_markers = (
            "\u96f7\u7433\u73a5",
            "\u5c0f\u73a5",
            "\u73a5\u4ed4",
            "\u73a5\u73a5",
        )
        self.assertEqual(expected_markers, PERSONA_CONTRACT.pii_markers)
        self.assertIs(PII_MARKERS, PERSONA_CONTRACT.pii_markers)
        self.assertEqual(
            [
                ("known_identity", marker)
                for marker in expected_markers
            ],
            [
                (finding.kind, finding.evidence)
                for marker in expected_markers
                for finding in classify_pii(marker)
            ],
        )
        with self.assertRaises(TypeError):
            PERSONA_CONTRACT.pii_markers[0] = "changed"  # type: ignore[index]

    def test_classifier_preserves_each_known_identity_evidence(self) -> None:
        from src.persona_corpus.privacy import classify_pii

        findings = classify_pii("小玥和玥仔都只是契约里的角色昵称。")
        self.assertEqual(
            [("known_identity", "小玥"), ("known_identity", "玥仔")],
            [(finding.kind, finding.evidence) for finding in findings],
        )

    def test_contract_loader_rejects_an_empty_identity_marker_set(self) -> None:
        from src.persona_corpus.contract import (
            DEFAULT_CONTRACT_PATH,
            PersonaContractError,
            load_persona_contract,
        )

        payload = json.loads(DEFAULT_CONTRACT_PATH.read_text(encoding="utf-8"))
        payload["privacy"] = {"pii_markers": []}
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "persona-contract.json"
            path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")

            with self.assertRaisesRegex(
                PersonaContractError,
                r"privacy\.pii_markers must be a non-empty unique string array",
            ):
                load_persona_contract(path)

    def test_direct_identifiers_have_the_same_findings_at_every_stage(self) -> None:
        from src.persona_corpus.privacy import (
            ENABLED_CONTENT_POLICY,
            LEGACY_AUDIT_POLICY,
            LEGACY_REVIEW_POLICY,
            pii_kinds,
        )

        cases = {
            "联系电话 13800138000。": "phone_number",
            "证件号 11010519491231002X。": "national_id",
            "邮箱 test@example.com。": "email_address",
            "姓名：张伟。": "person_name",
            "张伟昨天来办公室。": "person_name",
            "雷琳玥。": "known_identity",
            "地址：广东省长沙市雨花区。": "personal_location",
            "我的月薪是三千元。": "personal_income",
        }
        policies = (
            LEGACY_REVIEW_POLICY,
            LEGACY_AUDIT_POLICY,
            ENABLED_CONTENT_POLICY,
        )

        for text, expected_kind in cases.items():
            with self.subTest(text=text):
                findings = [pii_kinds(text, policy) for policy in policies]
                self.assertTrue(all(expected_kind in kinds for kinds in findings))
                self.assertEqual(findings[0], findings[1])
                self.assertEqual(findings[1], findings[2])

    def test_named_stage_policies_make_conservative_legacy_review_explicit(self) -> None:
        from src.persona_corpus.privacy import (
            ENABLED_CONTENT_POLICY,
            LEGACY_AUDIT_POLICY,
            LEGACY_REVIEW_POLICY,
            pii_kinds,
        )

        self.assertEqual("legacy_review", LEGACY_REVIEW_POLICY.name)
        self.assertEqual("legacy_audit", LEGACY_AUDIT_POLICY.name)
        self.assertEqual("enabled_content", ENABLED_CONTENT_POLICY.name)

        for text, broad_kind in (
            ("湖南菜的辣味很有层次。", "location_keyword"),
            ("工资字段最好统一用整数分保存。", "income_or_employment_keyword"),
        ):
            with self.subTest(text=text):
                self.assertIn(broad_kind, pii_kinds(text, LEGACY_REVIEW_POLICY))
                self.assertIn(broad_kind, pii_kinds(text, LEGACY_AUDIT_POLICY))
                self.assertEqual(frozenset(), pii_kinds(text, ENABLED_CONTENT_POLICY))

        self.assertIn(
            "personal_location",
            pii_kinds("我来自湖南。", ENABLED_CONTENT_POLICY),
        )
        self.assertIn(
            "personal_income",
            pii_kinds("我的月薪是三千元。", ENABLED_CONTENT_POLICY),
        )

        # The tracked legacy surface intentionally preserves this nickname;
        # the shared classifier still exposes it to enabled-content validation.
        self.assertNotIn(
            "known_identity",
            pii_kinds("玥玥在这里。", LEGACY_REVIEW_POLICY),
        )
        self.assertIn(
            "known_identity",
            pii_kinds("玥玥在这里。", ENABLED_CONTENT_POLICY),
        )

    def test_consumers_do_not_redeclare_pii_marker_or_regex_tables(self) -> None:
        forbidden_assignments = {"PII_MARKERS", "PII_PATTERNS", "PII_REGEXES"}
        consumers = (
            ROOT / "src/persona_corpus/builder.py",
            ROOT / "src/persona_corpus/normalization.py",
            ROOT / "src/persona_corpus/validation_rules/content_rules.py",
        )

        for path in consumers:
            tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
            assigned = {
                target.id
                for node in ast.walk(tree)
                if isinstance(node, (ast.Assign, ast.AnnAssign))
                for target in (
                    node.targets if isinstance(node, ast.Assign) else (node.target,)
                )
                if isinstance(target, ast.Name)
            }
            with self.subTest(path=path.name):
                self.assertEqual(set(), forbidden_assignments & assigned)


if __name__ == "__main__":
    unittest.main()
