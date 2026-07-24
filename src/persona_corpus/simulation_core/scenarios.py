from __future__ import annotations

import hashlib


SUBSEED_DERIVATION_VERSION = "persona-simulation-v2"


def derive_subseed(
    *,
    seed: int,
    day_index: int,
    slot_index: int,
    corpus_sha256: str,
    scheduler_config_sha256: str,
    scenario: str,
    derivation_version: str = SUBSEED_DERIVATION_VERSION,
) -> int:
    """Derive one deterministic selector seed bound to all replay inputs."""

    identity = "\x1f".join(
        (
            derivation_version,
            corpus_sha256,
            scheduler_config_sha256,
            scenario,
            str(seed),
            str(day_index),
            str(slot_index),
        )
    ).encode("utf-8")
    return int.from_bytes(hashlib.sha256(identity).digest()[:8], "big", signed=False)

