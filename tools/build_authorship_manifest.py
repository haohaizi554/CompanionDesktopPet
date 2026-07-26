"""Build the canonical authorship manifest from literal source TSV batches."""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from src.persona_corpus.authored_catalog import (  # noqa: E402
    AuthoredCatalogError,
    build_authorship_manifest_payload,
    canonical_manifest_json,
    parse_authored_batches,
)


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Write the canonical hash-bound manifest for authored persona source batches."
    )
    parser.add_argument("--authored-dir", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    return parser.parse_args()


def main() -> int:
    arguments = _arguments()
    try:
        entries = parse_authored_batches(arguments.authored_dir)
        payload = build_authorship_manifest_payload(entries)
        arguments.output.parent.mkdir(parents=True, exist_ok=True)
        arguments.output.write_bytes(canonical_manifest_json(payload).encode("utf-8"))
    except AuthoredCatalogError as error:
        print(f"authorship manifest build failed: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
