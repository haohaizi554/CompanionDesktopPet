"""长期驻留的对话进程。一行 JSON 进，一行 JSON 出。"""

from __future__ import annotations

import argparse
import json
import sys
import traceback
from pathlib import Path

from langgraph.checkpoint.sqlite import SqliteSaver

from persona_dialogue.graph import build_graph
from persona_dialogue.llm import LlmConfig, build_model
from persona_dialogue.retrieve import LineIndex


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--soul", type=Path, required=True)
    parser.add_argument("--corpus", type=Path, required=True)
    parser.add_argument("--checkpoint", type=Path, required=True)
    args = parser.parse_args()

    try:
        soul = json.loads(args.soul.read_text(encoding="utf-8"))
        config = LlmConfig.load(args.config)
        index = LineIndex.from_corpus(args.corpus)
        model = build_model(config)
        args.checkpoint.parent.mkdir(parents=True, exist_ok=True)
    except Exception as error:  # noqa: BLE001 - the host only sees one startup line.
        _emit({"type": "error", "message": str(error)})
        return 1

    with SqliteSaver.from_conn_string(str(args.checkpoint)) as saver:
        graph = build_graph(model, soul, index, saver)
        _emit({"type": "ready", "lines": index.count})
        for raw in sys.stdin:
            line = raw.strip()
            if not line:
                continue
            try:
                message = json.loads(line)
            except json.JSONDecodeError:
                _emit({"type": "error", "message": "请求不是 JSON"})
                continue
            kind = message.get("type")
            if kind == "exit":
                return 0
            if kind != "reply":
                _emit({"id": message.get("id"), "type": "error", "message": "未知请求"})
                continue
            try:
                result = graph.invoke(
                    {
                        "user_text": str(message.get("text") or ""),
                        "draft": "",
                        "attempts": 0,
                        "accepted": False,
                        "failure": "",
                        "retrieved": [],
                    },
                    config={
                        "configurable": {"thread_id": str(message.get("thread_id") or "jiayi")},
                        "recursion_limit": 12,
                    },
                )
                _emit(
                    {
                        "id": message.get("id"),
                        "type": "reply",
                        "text": result.get("draft") or "",
                        "fallback": result.get("failure") == "fallback",
                    }
                )
            except Exception as error:  # noqa: BLE001 - one bad turn must not kill the process.
                traceback.print_exc(file=sys.stderr)
                _emit({"id": message.get("id"), "type": "error", "message": str(error)})
    return 0


def _emit(payload: dict) -> None:
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + "\n")
    sys.stdout.flush()


if __name__ == "__main__":
    raise SystemExit(main())
