import json
import tempfile
import unittest
from pathlib import Path

from langchain_core.messages import AIMessage, HumanMessage
from langgraph.checkpoint.sqlite import SqliteSaver

from persona_dialogue.audit import audit, clean_reply, completion_text, without_repeated_prior
from persona_dialogue.graph import build_graph, remember_line
from persona_dialogue.retrieve import LineIndex

SOUL = {
    "version": 1,
    "name": "佳怡",
    "identity": ["她叫佳怡。"],
    "voice": ["说短一点。"],
    "boundaries": ["不承认自己是模型。"],
    "exemplars": [{"label": "日常关心", "text": "你先喝口水。", "category": "DailyCare", "semantic_group": "daily"}],
}


class ScriptedModel:
    def __init__(self, replies: list[str]) -> None:
        self.replies = list(replies)
        self.seen: list[list[str]] = []

    def invoke(self, messages):
        self.seen.append([getattr(message, "content", "") for message in messages])
        return AIMessage(content=self.replies.pop(0))


class GraphTests(unittest.TestCase):
    def test_repeated_prior_sentence_is_removed_before_the_new_line(self) -> None:
        prior = "我先醒醒，马上就好。"
        self.assertEqual(
            "窗外的光挺亮堂，看着就让人心情舒展。",
            without_repeated_prior(
                "我先醒醒，马上就好。窗外的光挺亮堂，看着就让人心情舒展。",
                prior,
            ),
        )
        self.assertEqual("", without_repeated_prior(prior, prior))
        self.assertEqual("你先坐一会儿。", without_repeated_prior("你先坐一会儿。", prior))
        copied = "你来了，我刚把这页翻过去。今天过得怎么样，有没有遇到让你开心的小事？"
        self.assertEqual("", without_repeated_prior(copied, copied))
        self.assertEqual(
            "你敲这些数字，是想让我歇会儿吗？",
            without_repeated_prior(copied + "你敲这些数字，是想让我歇会儿吗？", copied),
        )

    def test_dots_are_not_a_reply_when_the_sentence_is_in_reasoning(self) -> None:
        self.assertEqual("在呢。", completion_text({"content": "……", "reasoning": "在呢。"}))
        self.assertEqual("在呢。", completion_text({"content": "...", "reasoning_content": "在呢。"}))
        self.assertEqual("在呢。", completion_text({"content": [{"type": "text", "text": "在呢。"}]}))
        self.assertEqual("", completion_text({"content": "……"}))

    def test_audit_strips_thinking_and_rejects_model_identity(self) -> None:
        self.assertEqual("我在。", clean_reply("<think>secret</think>我在。"))
        self.assertFalse(audit("我是通义千问。", "你好")[0])
        self.assertFalse(audit("雷琳玥今天也在。", "你好")[0])
        self.assertTrue(audit("你先坐一会儿。", "你好")[0])

    def test_failed_draft_is_rewritten_once_and_memory_survives(self) -> None:
        model = ScriptedModel(["我是通义千问，一个语言模型。", "你先把杯子放下。"])
        index = LineIndex(["你先把杯子放下。", "今天的风有点凉。"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, index, saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 12}
                first = graph.invoke({"user_text": "我有点累"}, config)
                self.assertEqual("你先把杯子放下。", first["draft"])
                self.assertEqual("", first["failure"])
                self.assertEqual(2, len(model.seen))

                model.replies.append("那你就靠一会儿。")
                second = graph.invoke({"user_text": "嗯"}, config)
                self.assertEqual("那你就靠一会儿。", second["draft"])
                contents = model.seen[-1]
                self.assertTrue(any("我有点累" in item for item in contents))

    def test_remembered_line_is_the_prior_sentence_on_the_next_reply(self) -> None:
        model = ScriptedModel(["那你就靠一会儿。"])
        index = LineIndex(["你先把杯子放下。"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, index, saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 12}
                self.assertTrue(remember_line(graph, "jiayi", "湿热的天气把树叶养得很绿。"))
                self.assertFalse(remember_line(graph, "jiayi", "湿热的天气把树叶养得很绿。"))
                self.assertEqual([], model.seen)
                result = graph.invoke({"user_text": "我有点累"}, config)

        self.assertEqual("那你就靠一会儿。", result["draft"])
        prompt = model.seen[0][0]
        self.assertNotIn("湿热的天气把树叶养得很绿。", prompt)
        self.assertIn("不要把先前的回答再念一遍", prompt)
        self.assertIn("湿热的天气把树叶养得很绿。", model.seen[0])
        self.assertIn("我有点累", model.seen[0][-1])

    def test_a_copied_earlier_answer_is_rewritten(self) -> None:
        copied = "你来了，我刚把这页翻过去。今天过得怎么样，有没有遇到让你开心的小事？"
        model = ScriptedModel([copied, "你敲这些数字，是想让我歇会儿吗？"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, LineIndex(["你先喝口水。"]), saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 12}
                self.assertTrue(remember_line(graph, "jiayi", copied))
                result = graph.invoke({"user_text": "333333"}, config)

        self.assertEqual("你敲这些数字，是想让我歇会儿吗？", result["draft"])
        self.assertEqual(2, len(model.seen))
        self.assertIn("不要复述旧回答", model.seen[1][0])

    def test_empty_input_does_not_call_the_model(self) -> None:
        model = ScriptedModel(["不该被叫到。"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, LineIndex(["你先喝口水。"]), saver)
                result = graph.invoke(
                    {"user_text": "   "},
                    {"configurable": {"thread_id": "empty"}, "recursion_limit": 8},
                )
        self.assertEqual("你说呀，我听着。", result["draft"])
        self.assertEqual([], model.seen)

    def test_retrieval_prefers_the_overlapping_line(self) -> None:
        index = LineIndex(["今天把音标再读一遍。", "路口的灯刚换，我们等车。"])
        found = index.search("路口等车")
        self.assertEqual("路口的灯刚换，我们等车。", found[0])

    def test_setting_request_is_recognized_and_plain_chat_is_not(self) -> None:
        from persona_dialogue.skills import wants_setting

        self.assertTrue(wants_setting("把语速调成0.9倍"))
        self.assertFalse(wants_setting("我有点累"))

    def test_skill_changes_speech_and_hidden_sampling_without_touching_other_fields(self) -> None:
        from persona_dialogue.skills import DEFAULT_SETTINGS, apply_tool_calls

        updated, actions, notes = apply_tool_calls(
            DEFAULT_SETTINGS,
            [{"name": "set_speech", "args": {"speed": 0.8, "top_k": 8}, "id": "s1"}],
        )
        self.assertEqual(0.8, updated["speech_speed"])
        self.assertEqual(1.0, updated["speech_temperature"])
        self.assertEqual(8, updated["top_k"])
        self.assertEqual(1.0, updated["top_p"])
        self.assertEqual("set_speech", actions[0]["skill"])
        self.assertEqual(0.8, actions[0]["speed"])
        self.assertNotIn("temperature", actions[0])
        self.assertTrue(notes[0])

        unchanged, rejected, _notes = apply_tool_calls(
            DEFAULT_SETTINGS,
            [{"name": "set_speech", "args": {"speed": 9}, "id": "s2"}],
        )
        self.assertEqual([], rejected)
        self.assertEqual(1.0, unchanged["speech_speed"])

    def test_tool_call_is_applied_once_and_the_next_turn_does_not_repeat_it(self) -> None:
        model = ToolThenSpeechModel()
        index = LineIndex(["你先把杯子放下。"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, index, saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 12}
                first = graph.invoke({"user_text": "语速慢一点", "settings": {}}, config)
                self.assertEqual("好，气泡我改好了。", first["draft"])
                self.assertEqual([{"skill": "set_bubble", "seconds": 8}], first["actions"])
                self.assertEqual(1, model.phase)

                second = graph.invoke({"user_text": "嗯", "settings": first["settings"]}, config)
                self.assertEqual("我听着呢。", second["draft"])
                self.assertEqual([], second["actions"])


    def test_older_turns_are_compacted_and_the_recent_ones_stay(self) -> None:
        packed = json.dumps(
            {"summary": "对方在准备下周考试。", "remember": ["下周考试"], "forget": []},
            ensure_ascii=False,
        )
        model = ScriptedModel([packed, "那你早点睡。"])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, LineIndex(["你先喝口水。"]), saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 12}
                for index in range(12):
                    graph.update_state(
                        config,
                        {
                            "messages": [
                                HumanMessage(content=f"第{index}件事先放着"),
                                AIMessage(content="嗯，我听着。"),
                            ]
                        },
                        as_node="inspect",
                    )
                result = graph.invoke({"user_text": "我有点累"}, config)
                values = graph.get_state(config).values

        self.assertEqual("那你早点睡。", result["draft"])
        self.assertEqual(2, len(model.seen))
        self.assertIn("第0件事先放着", model.seen[0][1])
        draft = model.seen[1]
        self.assertIn("对方在准备下周考试。", draft[0])
        self.assertIn("下周考试", draft[0])
        self.assertNotIn("第0件事先放着", "\n".join(draft[1:]))
        self.assertIn("第8件事先放着", "\n".join(draft[1:]))
        self.assertEqual(8, values["compacted_count"])
        self.assertIn("下周考试", values["facts"])

    def test_spoken_lines_past_two_hundred_stay_in_the_summary(self) -> None:
        model = ScriptedModel([])
        with tempfile.TemporaryDirectory() as directory:
            with SqliteSaver.from_conn_string(str(Path(directory) / "memory.sqlite")) as saver:
                graph = build_graph(model, SOUL, LineIndex(["你先喝口水。"]), saver)
                config = {"configurable": {"thread_id": "jiayi"}, "recursion_limit": 8}
                for index in range(201):
                    self.assertTrue(remember_line(graph, "jiayi", f"第{index}句树叶。"))
                values = graph.get_state(config).values

        self.assertEqual([], model.seen)
        self.assertEqual(200, len(values["messages"]))
        self.assertIn("第0句树叶", values["memory_summary"])
        self.assertNotIn("第0句树叶。", [message.content for message in values["messages"]])
        self.assertIn("第200句树叶。", [message.content for message in values["messages"]])


class ToolThenSpeechModel:
    def __init__(self) -> None:
        self.phase = 0

    def bind_tools(self, tools, **_kwargs):
        self.tools = tools
        return self

    def invoke(self, messages):
        self.phase += 1
        if self.phase == 1:
            return AIMessage(
                content="",
                tool_calls=[{"name": "set_bubble", "args": {"seconds": 8}, "id": "c1"}],
            )
        return AIMessage(content="我听着呢。")


if __name__ == "__main__":
    unittest.main()
