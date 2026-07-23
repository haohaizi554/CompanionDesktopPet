from __future__ import annotations

import argparse
import hashlib
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
if str(ROOT) not in sys.path:
    sys.path.insert(0, str(ROOT))

from src.persona_corpus.loader import CorpusFormatError, load_v2
from src.persona_corpus.simulation import (
    SimulationError,
    render_simulation_report,
    simulate,
    write_editorial_reports,
)
from src.persona_corpus.validation import ValidationInputError, load_json_object


def seed_sequence_from_count(count: int) -> tuple[int, ...]:
    if type(count) is not int or count <= 0:
        raise ValueError("seed count must be a positive exact integer")
    return tuple(range(count))


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Run deterministic offline persona playback simulation and reports."
    )
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--days", type=int, default=30)
    parser.add_argument(
        "--seeds",
        type=int,
        default=10,
        help="number of seeds; N means the exact sequence range(N)",
    )
    parser.add_argument("--report", type=Path, required=True)
    parser.add_argument(
        "--events-json",
        type=Path,
        default=ROOT / "reports" / "simulation-events.json",
    )
    parser.add_argument(
        "--source",
        type=Path,
        default=ROOT / "data" / "source" / "persona-corpus.original.tsv",
    )
    parser.add_argument(
        "--archive",
        type=Path,
        default=ROOT / "data" / "optimized" / "persona-corpus-archive.tsv",
    )
    parser.add_argument(
        "--review",
        type=Path,
        default=ROOT / "data" / "optimized" / "persona-corpus-review.tsv",
    )
    parser.add_argument(
        "--pii-review",
        type=Path,
        default=ROOT / "reports" / "pii-review.tsv",
    )
    parser.add_argument(
        "--audit-after",
        type=Path,
        default=ROOT / "reports" / "corpus-audit-after.md",
    )
    parser.add_argument(
        "--rewrite-summary",
        type=Path,
        default=ROOT / "reports" / "corpus-rewrite-summary.md",
    )
    parser.add_argument(
        "--manual-review",
        type=Path,
        default=ROOT / "reports" / "corpus-manual-review.md",
    )
    return parser


def _write_lf(path: Path, payload: bytes) -> None:
    if b"\r" in payload or not payload.endswith(b"\n") or payload.endswith(b"\n\n"):
        raise SimulationError(f"{path}: output must use LF and exactly one trailing newline")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(payload)


def main(argv: list[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    try:
        seeds = seed_sequence_from_count(args.seeds)
        corpus = load_v2(args.corpus)
        config = load_json_object(args.config)
        report = simulate(corpus, config, days=args.days, seeds=seeds)
        simulation_markdown = render_simulation_report(report).encode("utf-8")
        events_json = report.to_validation_json()
        _write_lf(args.report, simulation_markdown)
        _write_lf(args.events_json, events_json)
        editorial = write_editorial_reports(
            corpus=corpus,
            source_path=args.source,
            archive_path=args.archive,
            review_path=args.review,
            pii_path=args.pii_review,
            audit_after_path=args.audit_after,
            rewrite_summary_path=args.rewrite_summary,
            manual_review_path=args.manual_review,
            simulation_report=report,
        )
    except (CorpusFormatError, SimulationError, ValidationInputError, ValueError, OSError) as error:
        print(f"Simulation failed: {error}", file=sys.stderr)
        return 2

    print(
        "Simulation: "
        f"{report.days} days x {len(report.seeds)} seeds, "
        f"{report.output_count}/{report.total_attempts} outputs, "
        f"{len(report.hard_violations)} hard violations"
    )
    print(f"Report SHA-256: {hashlib.sha256(simulation_markdown).hexdigest()}")
    print(f"Events SHA-256: {hashlib.sha256(events_json).hexdigest()}")
    print(
        "Editorial evidence: "
        f"rewrites={editorial.general_rewrite_examples}, "
        f"disabled={editorial.disabled_examples}, "
        f"tone={editorial.tone_fix_examples}, "
        f"fake_context={editorial.fake_context_examples}, "
        f"manual={editorial.manual_review_items}"
    )
    return 1 if report.hard_violations else 0


if __name__ == "__main__":
    raise SystemExit(main())
