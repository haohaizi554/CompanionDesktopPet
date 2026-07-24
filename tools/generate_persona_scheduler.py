#!/usr/bin/env python3
"""Generate the scheduler config from the authoritative persona contract."""

from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
CONTRACT_PATH = ROOT / "config/persona-contract.json"
OUTPUT_PATH = ROOT / "config/persona-scheduler.json"


def _reject_constant(value: str) -> None:
    raise ValueError(f"non-finite JSON number {value!r}")


def _pairs(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        if key in result:
            raise ValueError(f"duplicate JSON key {key!r}")
        result[key] = value
    return result


def expected_scheduler_bytes() -> bytes:
    contract_bytes = CONTRACT_PATH.read_bytes()
    contract = json.loads(
        contract_bytes.decode("utf-8-sig"),
        object_pairs_hook=_pairs,
        parse_constant=_reject_constant,
    )
    if (
        not isinstance(contract, dict)
        or type(contract.get("schema_version")) is not int
        or contract.get("schema_version") != 1
    ):
        raise ValueError("persona contract must be a schema-v1 JSON object")
    scheduler = contract.get("scheduler")
    controlled = contract.get("controlled_values")
    if not isinstance(scheduler, dict) or not isinstance(controlled, dict):
        raise ValueError("persona contract lacks scheduler or controlled_values")

    output = {
        "$schema": "./schemas/persona-scheduler.schema.json",
        "schema_version": 1,
        "derived_from": {
            "path": "config/persona-contract.json",
            "schema_version": contract["schema_version"],
            "sha256": hashlib.sha256(contract_bytes).hexdigest(),
        },
        "category_group_weights": scheduler["category_group_weights"],
        "output_mode_targets": scheduler["output_mode_targets"],
        "runtime_limits": scheduler["runtime_limits"],
        "context_tokens": controlled["context_tokens"],
        "mvp_triggers": controlled["mvp_triggers"],
        "future_triggers": controlled["future_triggers"],
    }
    return (json.dumps(output, ensure_ascii=False, indent=2) + "\n").encode("utf-8")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail instead of writing when persona-scheduler.json is stale",
    )
    arguments = parser.parse_args(argv)
    try:
        expected = expected_scheduler_bytes()
        current = OUTPUT_PATH.read_bytes() if OUTPUT_PATH.is_file() else None
        if arguments.check:
            if current != expected:
                print(
                    "config/persona-scheduler.json is stale; run "
                    "python tools/generate_persona_scheduler.py",
                    file=sys.stderr,
                )
                return 1
            return 0
        if current != expected:
            OUTPUT_PATH.write_bytes(expected)
            print(f"wrote {OUTPUT_PATH.relative_to(ROOT)}")
        return 0
    except (KeyError, OSError, UnicodeError, ValueError) as error:
        print(f"cannot generate scheduler config: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
