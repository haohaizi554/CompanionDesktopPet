from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from src.persona_corpus.contract import (
    DEFAULT_CONTRACT_PATH,
    PersonaContractError,
    load_persona_contract,
)


class TemporalContractValidationTests(unittest.TestCase):
    def test_overlapping_context_token_hour_ranges_are_rejected(self) -> None:
        payload = json.loads(DEFAULT_CONTRACT_PATH.read_text(encoding="utf-8"))
        payload["temporal"]["context_token_hours"]["time:late_night"] = [
            [0, 6],
            [23, 24],
        ]

        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "persona-contract.json"
            path.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")

            with self.assertRaisesRegex(
                PersonaContractError,
                "context_token_hours must cover every hour exactly once",
            ):
                load_persona_contract(path)


if __name__ == "__main__":
    unittest.main()
