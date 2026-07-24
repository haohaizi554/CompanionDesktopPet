from __future__ import annotations

import unittest

from src.persona_corpus.lexical import (
    SEASONING_MARKERS,
    contains_seasoning_marker,
    match_seasoning_markers,
)


class SeasoningMatcherTests(unittest.TestCase):
    def test_shared_markers_cover_persona_phrases_but_exclude_identity_quota(self) -> None:
        self.assertTrue(
            {
                "哈？",
                "你认真的？",
                "真的假的",
                "啊推",
                "我靠",
                "我丢",
                "我真的不想多说什么了",
                "嗯嗯",
                "6",
                "666",
                "NB",
            }
            <= set(SEASONING_MARKERS)
        )
        self.assertTrue({"玥玥", "小玥", "雷琳玥"}.isdisjoint(SEASONING_MARKERS))

    def test_numeric_and_english_markers_require_lexical_boundaries(self) -> None:
        self.assertEqual(("6",), match_seasoning_markers("这次 6，确实可以。"))
        self.assertEqual(("666",), match_seasoning_markers("666！"))
        self.assertEqual(("NB",), match_seasoning_markers("这个结果 NB。"))
        for text in (
            "Python 3.6",
            "IPv6",
            "6666",
            "v666",
            "第6次",
            "6月",
            "6个",
            "SNBModel",
            "nb_value",
        ):
            self.assertFalse(contains_seasoning_marker(text), text)

    def test_matching_is_nfkc_and_case_insensitive_without_identity_leakage(self) -> None:
        self.assertEqual(("NB",), match_seasoning_markers("ｎｂ！"))
        self.assertTrue(contains_seasoning_marker("嗯嗯，这次可以。"))
        self.assertFalse(contains_seasoning_marker("玥玥把书翻到下一页。"))


if __name__ == "__main__":
    unittest.main()
