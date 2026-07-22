from __future__ import annotations

import unittest

from src.persona_corpus.extraction import extract_structure
from src.persona_corpus.models import LegacyLine


def make_two_by_two_by_two_rows() -> list[LegacyLine]:
    prefixes = ("Hey, ", "Notice: ")
    topics = ("the cache is stale", "the queue is full")
    suffixes = ("; inspect the logs.", "; reproduce it first.")
    rows: list[LegacyLine] = []
    source_line = 1
    for prefix in prefixes:
        for topic in topics:
            for suffix in suffixes:
                rows.append(
                    LegacyLine(source_line, "Debugging", prefix + topic + suffix)
                )
                source_line += 1
    return rows


class ExtractionTests(unittest.TestCase):
    def test_cartesian_rows_recover_shared_parts(self) -> None:
        result = extract_structure(make_two_by_two_by_two_rows())

        self.assertEqual(2, len(result.prefixes))
        self.assertEqual(2, len(result.topics))
        self.assertEqual(2, len(result.suffixes))
        self.assertTrue(
            all(row.extraction_confidence >= 0.9 for row in result.mappings)
        )

    def test_easter_egg_is_standalone(self) -> None:
        result = extract_structure(
            [LegacyLine(1, "EasterEgg", "玥玥把秘密藏进了书页。")]
        )

        self.assertEqual("", result.mappings[0].prefix_id)
        self.assertTrue(result.mappings[0].topic_id.startswith("egg_standalone_"))
        self.assertEqual("", result.mappings[0].suffix_id)

    def test_shared_suffix_tail_does_not_hide_cartesian_suffixes(self) -> None:
        rows: list[LegacyLine] = []
        for prefix in ("Hey, ", "Notice: "):
            for topic in ("cache stale", "queue full"):
                for suffix in ("; inspect it now.", "; reproduce it now."):
                    rows.append(
                        LegacyLine(len(rows) + 1, "Debugging", prefix + topic + suffix)
                    )

        result = extract_structure(rows)

        self.assertEqual(2, len(result.suffixes))
        self.assertTrue(all(not topic.standalone for topic in result.topics))
        self.assertTrue(
            all(mapping.extraction_confidence >= 0.9 for mapping in result.mappings)
        )


if __name__ == "__main__":
    unittest.main()
