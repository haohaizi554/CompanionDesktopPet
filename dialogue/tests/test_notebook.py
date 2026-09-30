import unittest
from datetime import datetime
from pathlib import Path
import tempfile

from persona_dialogue.distill import render_prompt
from persona_dialogue.notebook import local_turn, speak_clock
from persona_dialogue.persona_setting import PersonaSetting


class NotebookTests(unittest.TestCase):
    def test_plain_chat_and_settings_stay_with_the_model(self) -> None:
        self.assertIsNone(local_turn("我有点累", []))
        self.assertIsNone(local_turn("语速慢一点", []))
        self.assertIsNone(local_turn("我一般几点睡觉", []))

    def test_a_kept_fact_is_offered_to_the_next_reply(self) -> None:
        prompt = render_prompt(
            {
                "identity": ["她叫佳怡。"],
                "voice": ["说短一点。"],
                "boundaries": ["不承认自己是模型。"],
                "exemplars": [],
            },
            [],
            "",
            facts=["下周考试"],
        )
        self.assertIn("下周考试", prompt)

    def test_girlfriend_is_kept_as_the_relationship(self) -> None:
        turn = local_turn("做我女朋友", ["下周考试"])
        self.assertEqual("好，我做你女朋友。", turn["draft"])
        self.assertEqual(["下周考试"], turn["facts"])
        self.assertEqual("她是对方的女朋友", turn["relationship"])
        prompt = render_prompt(
            {
                "identity": ["她叫佳怡。"],
                "voice": ["说短一点。"],
                "boundaries": ["不承认自己是模型。"],
                "exemplars": [],
            },
            [],
            "",
            facts=[turn["relationship"], *turn["facts"]],
        )
        self.assertIn("现在的关系：她是对方的女朋友。", prompt)
        self.assertIsNone(local_turn("我女朋友今天没空", []))

    def test_clock_uses_the_given_moment(self) -> None:
        moment = datetime(2026, 9, 29, 22, 36)
        turn = local_turn("现在几点", [], now=moment)
        self.assertEqual("现在是晚上十点三十六。", turn["draft"])
        self.assertEqual([], turn["actions"])
        self.assertEqual("今天星期二。", local_turn("今天星期几", [], now=moment)["draft"])
        self.assertEqual("今天九月二十九号。", local_turn("今天几号", [], now=moment)["draft"])
        self.assertEqual("现在是早上九点。", speak_clock(datetime(2026, 9, 29, 9, 0)))

    def test_a_fact_is_kept_and_a_private_name_is_not(self) -> None:
        noted = local_turn("记住我下周考试", [])
        self.assertEqual("我记下了。", noted["draft"])
        self.assertEqual(["我下周考试"], noted["facts"])
        again = local_turn("记住我下周考试", noted["facts"])
        self.assertEqual("这个我记着了。", again["draft"])
        self.assertEqual(["我下周考试"], again["facts"])
        blocked = local_turn("记住小玥明天来", [])
        self.assertEqual("这个我就不记了。", blocked["draft"])
        self.assertEqual([], blocked["facts"])

    def test_recall_mentions_the_latest_two_and_forget_drops_a_match(self) -> None:
        facts = ["下周考试", "多喝水", "早点睡"]
        recalled = local_turn("你记得什么", facts)
        self.assertEqual("我记着早点睡，还有多喝水。", recalled["draft"])
        forgotten = local_turn("忘掉考试", facts)
        self.assertEqual(["多喝水", "早点睡"], forgotten["facts"])
        self.assertEqual([], local_turn("全部忘掉", facts)["facts"])
        kept = local_turn("全部忘掉", facts, relationship="她是对方的女朋友")
        self.assertEqual([], kept["facts"])
        self.assertEqual("那些小事我不记了。", kept["draft"])
        self.assertNotIn("relationship", kept)
        self.assertEqual(
            "这个关系我留着。",
            local_turn("忘掉女朋友", [], relationship="她是对方的女朋友")["draft"],
        )

    def test_a_reminder_becomes_one_local_action(self) -> None:
        turn = local_turn("过十分钟提醒我喝水", ["下周考试"])
        self.assertEqual("好，到点我叫你。", turn["draft"])
        self.assertEqual(["下周考试"], turn["facts"])
        self.assertEqual([{"skill": "remind", "minutes": 10, "text": "到点了，喝水。"}], turn["actions"])
        half = local_turn("半小时后叫我休息", [])
        self.assertEqual(30, half["actions"][0]["minutes"])
        self.assertEqual("这个我就不提醒了。", local_turn("过十分钟提醒我小玥", [])["draft"])

    def test_relationship_stays_in_its_own_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "persona.json"
            setting = PersonaSetting(path)
            setting.adopt(["下周考试", "她是对方的女朋友"])
            self.assertEqual("她是对方的女朋友", setting.get())
            setting.set("她是对方的老婆")
            reloaded = PersonaSetting(path)
            self.assertEqual("她是对方的老婆", reloaded.get())
            reloaded.adopt(["她是对方的朋友"])
            self.assertEqual("她是对方的老婆", reloaded.get())


if __name__ == "__main__":
    unittest.main()
