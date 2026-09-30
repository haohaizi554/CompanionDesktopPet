import unittest

from persona_dialogue.audit import audit
from persona_dialogue.distill import render_prompt
from persona_dialogue.skills import reply_limit

SOUL = {
    "identity": ["她叫佳怡。"],
    "voice": ["平时一句大约 24 个字，回复不要超过 80 个字。"],
    "boundaries": ["不承认自己是模型。"],
    "exemplars": [],
}


class ReplyLimitTests(unittest.TestCase):
    def test_default_stays_short_and_out_of_range_values_clamp(self) -> None:
        self.assertEqual(80, reply_limit(None))
        self.assertEqual(120, reply_limit({"reply_max_chars": 120}))
        self.assertEqual(200, reply_limit({"reply_max_chars": 400}))
        self.assertEqual(50, reply_limit({"reply_max_chars": 1}))
        self.assertEqual(200, reply_limit({"reply_max_chars": 5000}))

    def test_a_long_reply_passes_only_when_the_limit_allows_it(self) -> None:
        text = "我在听，你把事情慢慢说完。" * 8
        self.assertGreater(len(text), 80)
        self.assertFalse(audit(text, "你说")[0])
        self.assertTrue(audit(text, "你说", max_chars=400)[0])

    def test_prompt_keeps_short_lines_until_the_limit_is_raised(self) -> None:
        short = render_prompt(SOUL, [], "")
        long = render_prompt(SOUL, [], "", max_chars=400)
        self.assertIn("那一两句", short)
        self.assertIn("400", long)
        self.assertIn("说完整", long)

    def test_a_long_reply_is_cut_at_the_last_finished_sentence(self) -> None:
        from persona_dialogue.audit import fit_reply

        fitted = fit_reply("先把杯子放下。" + "多余的话。" * 30, 8)
        self.assertEqual("先把杯子放下。", fitted)
        self.assertLessEqual(len(fitted), 8)

    def test_output_budget_grows_with_the_reply_cap(self) -> None:
        from persona_dialogue.skills import output_tokens

        self.assertEqual(180, output_tokens(80))
        self.assertEqual(1600, output_tokens(800))
        self.assertEqual(2048, output_tokens(800, floor=3000))


if __name__ == "__main__":
    unittest.main()
