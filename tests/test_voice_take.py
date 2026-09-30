from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


def _load_infer():
    path = Path(__file__).resolve().parents[1] / "voice" / "infer_stdio.py"
    spec = importlib.util.spec_from_file_location("infer_stdio", path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class VoiceTakeTests(unittest.TestCase):
    def test_slightly_short_clip_is_kept(self) -> None:
        infer = _load_infer()
        text = "你先把杯子放下吧"  # 8 chars, floor 0.45s, keep above 0.279s
        self.assertTrue(infer._keeps_first_take(text, int(0.4 * 32000), 32000, 1.0))

    def test_swallowed_clip_is_synthesized_again(self) -> None:
        infer = _load_infer()
        text = "你先把杯子放下，别急着站起来跟我说话"  # long enough that 0.3s is swallowed
        self.assertFalse(infer._keeps_first_take(text, int(0.3 * 32000), 32000, 1.0))

    def test_edge_silence_is_cut_and_a_quiet_clip_is_kept(self) -> None:
        import numpy as np

        infer = _load_infer()
        tone = np.zeros(32000, dtype=np.int16)
        tone[8000:16000] = 1000
        trimmed = infer._trim_edges(tone, 32000)
        self.assertLess(trimmed.size, tone.size)
        self.assertGreaterEqual(int(np.argmax(np.abs(trimmed) >= 128)), 0)
        self.assertLess(int(np.argmax(np.abs(trimmed) >= 128)), int(0.03 * 32000) + 1)
        self.assertEqual(32000, infer._trim_edges(np.zeros(32000, dtype=np.int16), 32000).size)


if __name__ == "__main__":
    unittest.main()
