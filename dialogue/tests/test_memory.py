import unittest

from langchain_core.messages import HumanMessage

from persona_dialogue.memory import (
    TRANSCRIPT_LIMIT,
    apply_memory_update,
    clip_summary,
    expire_overflow,
    fold_pending,
    live_messages,
    parse_memory_update,
    pending_messages,
    recent_messages,
)
from persona_dialogue.notebook import sanitize_facts


class MemoryTests(unittest.TestCase):
    def test_recent_window_stops_on_the_character_budget(self) -> None:
        messages = [HumanMessage(content="字" * 200, id=str(index)) for index in range(20)]
        recent = recent_messages(messages)
        self.assertLess(len(recent), 16)
        self.assertEqual(messages[-1], recent[-1])
        self.assertLessEqual(sum(len(message.content) for message in recent), 1400)

    def test_pending_slice_continues_after_the_first_batch(self) -> None:
        messages = [HumanMessage(content="甲" * 1000, id=str(index)) for index in range(6)]
        first = pending_messages(messages, 0)
        self.assertEqual(3, len(first))
        second = pending_messages(messages, len(first))
        self.assertEqual(2, len(second))

    def test_live_context_keeps_a_short_gap_until_it_is_compacted(self) -> None:
        messages = [HumanMessage(content=f"第{index}句", id=str(index)) for index in range(18)]
        self.assertEqual([], pending_messages(messages, 0))
        self.assertEqual(messages, live_messages(messages, 0))

    def test_overflow_only_drops_messages_already_in_the_summary(self) -> None:
        messages = [HumanMessage(content=str(index), id=str(index)) for index in range(TRANSCRIPT_LIMIT + 10)]
        expired, cover = expire_overflow(messages, 4)
        self.assertEqual(["0", "1", "2", "3"], [message.id for message in expired])
        self.assertEqual(0, cover)
        expired, cover = expire_overflow(messages, TRANSCRIPT_LIMIT + 10)
        self.assertEqual(10, len(expired))
        self.assertEqual(TRANSCRIPT_LIMIT, cover)

    def test_a_fenced_update_replaces_stale_facts(self) -> None:
        parsed = parse_memory_update(
            """```json
            {"summary": "考试已经结束，对方改口叫小周。", "remember": ["我叫小周"], "forget": ["下周考试", "我叫小林"]}
            ```"""
        )
        summary, facts = apply_memory_update(
            "对方下周考试。",
            ["下周考试", "我叫小林", "她是对方的女朋友"],
            parsed,
        )
        self.assertIn("考试已经结束", summary)
        self.assertEqual(["我叫小周", "她是对方的女朋友"], facts)

    def test_garbage_model_output_still_folds_the_old_lines(self) -> None:
        pending = [
            HumanMessage(content="下周考试"),
            HumanMessage(content="考试考完了"),
        ]
        summary, facts = fold_pending("对方下周考试。", ["下周考试", "多喝水"], pending, "我先随便说说。")
        self.assertIn("考试考完了", summary)
        self.assertNotIn("下周考试", facts)
        self.assertIn("多喝水", facts)
        self.assertNotIn("小玥", clip_summary("小玥明天来，考试考完了。"))

    def test_explicit_facts_keep_the_newest_twenty_four(self) -> None:
        facts = sanitize_facts([f"小事{index}" for index in range(25)])
        self.assertEqual(24, len(facts))
        self.assertNotIn("小事0", facts)
        self.assertIn("小事24", facts)
        pinned = sanitize_facts(["她是对方的女朋友", *[f"小事{index}" for index in range(30)]])
        self.assertEqual(24, len(pinned))
        self.assertIn("她是对方的女朋友", pinned)
        self.assertNotIn("小事0", pinned)


if __name__ == "__main__":
    unittest.main()
