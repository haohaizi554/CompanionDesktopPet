import tempfile
import unittest
from pathlib import Path

from langchain_core.messages import AIMessage
from langgraph.checkpoint.sqlite import SqliteSaver

from persona_dialogue.audit import audit, clean_reply
from persona_dialogue.graph import build_graph
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


if __name__ == "__main__":
    unittest.main()
