from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from src.persona_corpus.authored_catalog import load_authored_catalog  # noqa: E402
from src.persona_corpus.builder import (  # noqa: E402
    build_hybrid,
    build_v2,
    load_source_mappings,
    write_build_outputs,
)
from src.persona_corpus.loader import load_legacy  # noqa: E402


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Build deterministic, curated Persona Corpus v2 outputs."
    )
    parser.add_argument(
        "--input",
        type=Path,
        default=Path("data/source/persona-corpus.original.tsv"),
    )
    parser.add_argument(
        "--authored-dir",
        type=Path,
        default=Path("data/authored/v1"),
    )
    parser.add_argument(
        "--authorship-manifest",
        type=Path,
        default=Path("config/persona-authorship-manifest.json"),
    )
    parser.add_argument(
        "--mappings",
        type=Path,
        default=Path("data/intermediate/source-line-map.tsv"),
    )
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument(
        "--profile",
        choices=("authored", "legacy", "hybrid"),
        default="hybrid",
        help="Runtime partition profile; release builds use hybrid.",
    )
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
    authored = load_authored_catalog(args.authored_dir, args.authorship_manifest)
    if args.profile == "hybrid":
        result = build_hybrid(
            source,
            mappings,
            args.seed,
            pii_policy=args.pii_policy,
            authored=authored,
        )
    elif args.profile == "legacy":
        result = build_v2(
            source,
            mappings,
            args.seed,
            pii_policy=args.pii_policy,
            apply_scene_dose=False,
        )
    else:
        result = build_v2(
            source,
            mappings,
            args.seed,
            pii_policy=args.pii_policy,
            authored=authored,
        )
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
    print(f"authorship_ledger={len(result.authorship_ledger)}")
    for name, value in result.partition_manifest.items():
        print(f"partition_{name}={value}")
    print(f"v2_sha256={v2_hash}")
    for name, path in paths.items():
        print(f"{name}={path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
