from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from src.persona_corpus.builder import (  # noqa: E402
    build_v2,
    load_source_mappings,
    write_build_outputs,
)
from src.persona_corpus.loader import load_legacy  # noqa: E402


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build deterministic, curated Persona Corpus v2 outputs."
    )
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument(
        "--mappings",
        type=Path,
        default=Path("data/intermediate/source-line-map.tsv"),
    )
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--report-output",
        type=Path,
        help="Explicit pii-review.tsv path for noncanonical output layouts.",
    )
    parser.add_argument("--seed", type=int, default=20260722)
    parser.add_argument("--pii-policy", choices=("review",), default="review")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(argv)
    source = load_legacy(args.input)
    mappings = load_source_mappings(args.mappings)
    result = build_v2(source, mappings, args.seed, pii_policy=args.pii_policy)
    paths = write_build_outputs(
        result,
        args.output,
        report_output=args.report_output,
    )
    v2_hash = hashlib.sha256(paths["v2"].read_bytes()).hexdigest()
    print(f"enabled={len(result.enabled)}")
    print(f"archive={len(result.archive)}")
    print(f"review={len(result.review)}")
    print(f"pii_review={len(result.pii_review)}")
    print(f"v2_sha256={v2_hash}")
    for name, path in paths.items():
        print(f"{name}={path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
