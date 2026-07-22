from __future__ import annotations

import argparse
import csv
import sys
from pathlib import Path
from typing import Iterable, Sequence

REPOSITORY_ROOT = Path(__file__).resolve().parents[1]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from src.persona_corpus.extraction import ExtractedComponent, extract_structure
from src.persona_corpus.loader import load_legacy, sha256_file


COMPONENT_HEADER = (
    "id",
    "category",
    "text",
    "normalized_text",
    "source_count",
    "extraction_confidence",
    "standalone",
)
SOURCE_MAP_HEADER = (
    "source_line",
    "category",
    "original_text",
    "prefix_id",
    "topic_id",
    "suffix_id",
    "extraction_confidence",
)


def _write_rows(path: Path, header: Sequence[str], rows: Iterable[Sequence[object]]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.writer(stream, delimiter="\t", lineterminator="\n")
        writer.writerow(header)
        writer.writerows(rows)


def _component_rows(components: Iterable[ExtractedComponent]):
    for component in components:
        yield (
            component.id,
            component.category,
            component.text,
            component.normalized_text,
            component.source_count,
            f"{component.extraction_confidence:.6f}",
            str(component.standalone).lower(),
        )


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Recover category-local prefix/topic/suffix corpus structure"
    )
    parser.add_argument("--input", required=True, type=Path, help="Legacy two-column TSV")
    parser.add_argument("--output-dir", required=True, type=Path)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    arguments = parse_args(argv)
    lines = load_legacy(arguments.input)
    result = extract_structure(lines)
    outputs = {
        "extracted-prefixes.tsv": (COMPONENT_HEADER, _component_rows(result.prefixes)),
        "extracted-topics.tsv": (COMPONENT_HEADER, _component_rows(result.topics)),
        "extracted-suffixes.tsv": (COMPONENT_HEADER, _component_rows(result.suffixes)),
        "source-line-map.tsv": (
            SOURCE_MAP_HEADER,
            (
                (
                    mapping.source_line,
                    mapping.category,
                    mapping.original_text,
                    mapping.prefix_id,
                    mapping.topic_id,
                    mapping.suffix_id,
                    f"{mapping.extraction_confidence:.6f}",
                )
                for mapping in result.mappings
            ),
        ),
    }
    for filename, (header, rows) in outputs.items():
        _write_rows(arguments.output_dir / filename, header, rows)

    counts = (
        f"prefixes={len(result.prefixes)} topics={len(result.topics)} "
        f"suffixes={len(result.suffixes)} mappings={len(result.mappings)}"
    )
    hashes = " ".join(
        f"{filename}={sha256_file(arguments.output_dir / filename)}"
        for filename in sorted(outputs)
    )
    print(f"Extracted {counts}")
    print(f"SHA-256 {hashes}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
