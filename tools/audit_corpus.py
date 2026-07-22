from __future__ import annotations

import argparse
import sys
from collections import Counter
from pathlib import Path
from typing import Iterable, Mapping, Sequence

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from src.persona_corpus.loader import load_legacy, sha256_file
from src.persona_corpus.models import AuditResult, LegacyLine
from src.persona_corpus.normalization import audit_legacy


def _escape(value: object) -> str:
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ")


def _table(headers: Sequence[str], rows: Iterable[Sequence[object]]) -> list[str]:
    result = [
        "| " + " | ".join(headers) + " |",
        "| " + " | ".join("---" for _ in headers) + " |",
    ]
    result.extend(
        "| " + " | ".join(_escape(value) for value in row) + " |" for row in rows
    )
    return result


def _top(mapping: Mapping[str, int], limit: int = 15) -> list[tuple[str, int]]:
    return sorted(mapping.items(), key=lambda item: (-item[1], item[0]))[:limit]


def _line_refs(line_numbers: Iterable[int]) -> str:
    values = list(line_numbers)
    return ", ".join(f"source line {line_number}" for line_number in values) or "—"


def _length_buckets(lines: Sequence[LegacyLine]) -> Counter[str]:
    buckets: Counter[str] = Counter()
    for line in lines:
        length = len(line.text)
        if length <= 10:
            label = "0–10"
        elif length <= 20:
            label = "11–20"
        elif length <= 30:
            label = "21–30"
        elif length <= 50:
            label = "31–50"
        else:
            label = "51+"
        buckets[label] += 1
    return buckets


def render_report(
    input_path: Path,
    lines: Sequence[LegacyLine],
    result: AuditResult,
    digest: str,
) -> str:
    by_source_line = {line.source_line: line for line in lines}
    report = [
        "# Persona Corpus Baseline Audit",
        "",
        f"- Input: `{input_path.as_posix()}`",
        f"- SHA-256: `{digest}`",
        "- Audit mode: bounded rare character 3-gram candidate buckets; no all-pairs comparison",
        "",
        "## Summary",
        "",
    ]
    report.extend(
        _table(
            ("Metric", "Value"),
            (
                ("Total lines", result.total_lines),
                ("Categories", len(result.category_counts)),
                ("Exact duplicate rows beyond first", result.exact_duplicate_count),
                (
                    "Normalized duplicate rows beyond first",
                    result.normalized_duplicate_count,
                ),
                ("Question lines", result.question_count),
                ("Likely PII lines", result.likely_pii_count),
                ("Bounded near-duplicate pairs", result.similar_pair_count),
            ),
        )
    )
    report.extend(["", "## Category distribution", ""])
    report.extend(_table(("Category", "Count"), _top(result.category_counts, 100)))
    report.extend(["", "## Text-length distribution", ""])
    length_order = ("0–10", "11–20", "21–30", "31–50", "51+")
    length_buckets = _length_buckets(lines)
    report.extend(
        _table(
            ("Characters", "Count"),
            ((bucket, length_buckets[bucket]) for bucket in length_order),
        )
    )

    report.extend(["", "## Risk indicators", ""])
    report.extend(
        _table(
            ("Indicator", "Count", "Examples"),
            [
                (
                    "Chinese or ASCII question mark",
                    result.question_count,
                    _line_refs(result.question_examples),
                ),
                (
                    "Likely PII marker",
                    result.likely_pii_count,
                    _line_refs(result.likely_pii_examples),
                ),
            ]
            + [
                (
                    f"High-risk phrase `{pattern}`",
                    count,
                    _line_refs(result.high_risk_examples.get(pattern, ())),
                )
                for pattern, count in _top(result.high_risk_patterns, 100)
            ],
        )
    )

    report.extend(["", "## Catchphrase distribution", ""])
    report.extend(
        _table(
            ("Phrase", "Count", "Examples"),
            (
                (
                    phrase,
                    count,
                    _line_refs(result.catchphrase_examples.get(phrase, ())),
                )
                for phrase, count in _top(result.catchphrase_counts, 100)
            ),
        )
    )

    report.extend(["", "## Prefix distribution", ""])
    for length in sorted(result.prefix_counts):
        report.extend([f"### Length {length}", ""])
        report.extend(_table(("Prefix", "Count"), _top(result.prefix_counts[length])))
        report.append("")

    report.extend(["## Suffix distribution", ""])
    for length in sorted(result.suffix_counts):
        report.extend([f"### Length {length}", ""])
        report.extend(_table(("Suffix", "Count"), _top(result.suffix_counts[length])))
        report.append("")

    report.extend(["## Duplicate and similarity examples", ""])
    duplicate_rows = []
    for first, duplicate in result.normalized_duplicate_examples:
        duplicate_rows.append(
            (
                "Normalized duplicate",
                _line_refs((first, duplicate)),
                by_source_line[duplicate].text,
            )
        )
    for pair in result.similar_pair_examples:
        duplicate_rows.append(
            (
                f"Near duplicate ({pair.similarity:.3f})",
                _line_refs((pair.left_source_line, pair.right_source_line)),
                f"{pair.left_text} / {pair.right_text}",
            )
        )
    report.extend(
        _table(("Kind", "Source", "Text"), duplicate_rows)
        if duplicate_rows
        else ["No duplicate examples found."]
    )

    report.extend(["", "## Flagged line examples", ""])
    flagged = dict.fromkeys(result.question_examples + result.likely_pii_examples)
    for examples in result.high_risk_examples.values():
        flagged.update(dict.fromkeys(examples))
    report.extend(
        _table(
            ("Source", "Category", "Text"),
            (
                (
                    f"source line {line_number}",
                    by_source_line[line_number].category,
                    by_source_line[line_number].text,
                )
                for line_number in list(flagged)[:20]
            ),
        )
        if flagged
        else ["No flagged examples found."]
    )
    report.append("")
    return "\n".join(report)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Audit the immutable legacy corpus")
    parser.add_argument("--input", required=True, type=Path, help="Legacy two-column TSV")
    parser.add_argument("--output", required=True, type=Path, help="Markdown report path")
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_args(argv)
    lines = load_legacy(arguments.input)
    result = audit_legacy(lines)
    digest = sha256_file(arguments.input)
    report = render_report(arguments.input, lines, result, digest)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(report, encoding="utf-8", newline="\n")
    print(
        f"Audited {result.total_lines} lines; SHA-256 {digest}; "
        f"report {arguments.output}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
