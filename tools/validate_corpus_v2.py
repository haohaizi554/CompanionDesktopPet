from __future__ import annotations

import argparse
import sys
from pathlib import Path


REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from src.persona_corpus.validation import (  # noqa: E402
    FORMAT_ERROR_CODES,
    ValidationInputError,
    format_report,
    validate_file,
)


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Validate Persona Corpus v2 and its offline scheduler gates."
    )
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument(
        "--allowlist",
        type=Path,
        default=Path("config/persona-review-allowlist.json"),
    )
    parser.add_argument(
        "--simulation",
        type=Path,
        help="Optional structured Task 6 simulation JSON result.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="strict")
    if hasattr(sys.stderr, "reconfigure"):
        sys.stderr.reconfigure(encoding="utf-8", errors="backslashreplace")
    args = parse_args(argv)
    try:
        report = validate_file(
            args.corpus,
            args.config,
            args.allowlist,
            simulation_path=args.simulation,
        )
    except ValidationInputError as error:
        print(f"INPUT_ERROR: {error}", file=sys.stderr)
        return 2
    print(format_report(report))
    if not report.errors:
        return 0
    if any(issue.code in FORMAT_ERROR_CODES for issue in report.errors):
        return 2
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
