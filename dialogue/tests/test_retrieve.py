import unittest

from persona_dialogue.retrieve import LineIndex


class RetrieveTests(unittest.TestCase):
    def test_overlapping_line_stays_first(self) -> None:
        index = LineIndex(["今天把音标再读一遍。", "路口的灯刚换，我们等车。"])
        self.assertEqual("路口的灯刚换，我们等车。", index.search("路口等车")[0])

    def test_later_phrase_outranks_the_opening(self) -> None:
        index = LineIndex(
            [
                "我想把周末留一段给自己。",
                "喝水的间隙不必刷消息。",
            ]
        )
        self.assertEqual("喝水的间隙不必刷消息。", index.search("我想喝水")[0])

    def test_latin_word_outranks_a_shared_bigram(self) -> None:
        index = LineIndex(
            [
                "设计图里标上数据怎么流。",
                "复盘一次 bug 时，记录触发条件。",
            ]
        )
        self.assertIn("bug", index.search("这个 bug 怎么看")[0])

    def test_finished_phrase_outranks_the_same_bigram_inside_another_word(self) -> None:
        index = LineIndex(
            [
                "卡片边缘有细小刻度，记录耐心一点点累积的长度。",
                "路上也能少一点累。",
            ]
        )
        self.assertEqual("路上也能少一点累。", index.search("我有点累")[0])

    def test_conflicting_suffix_does_not_hide_the_opening(self) -> None:
        index = LineIndex(
            [
                "翻到另一段才能知道输入长什么样。",
                "今天的风有点凉，窗口先安静一会儿。",
            ]
        )
        self.assertEqual("今天的风有点凉，窗口先安静一会儿。", index.search("今天怎么样")[0])

    def test_recently_spoken_line_is_skipped(self) -> None:
        index = LineIndex(["路口的灯刚换，我们等车。", "站在路口等灯，鞋尖朝着下一段街。"])
        found = index.search("路口等车", exclude=["路口的灯刚换，我们等车。"])
        self.assertEqual(["站在路口等灯，鞋尖朝着下一段街。"], found)


if __name__ == "__main__":
    unittest.main()
