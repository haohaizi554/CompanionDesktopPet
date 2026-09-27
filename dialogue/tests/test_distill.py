import json
import tempfile
import unittest
from pathlib import Path

from persona_dialogue.distill import PRIVACY_MARKERS, distill

ROOT = Path(__file__).resolve().parents[2]
CORPUS = ROOT / "data" / "optimized" / "persona-corpus-v2.tsv"
SOUL = ROOT / "data" / "persona" / "jiayi-soul.json"


class DistillTests(unittest.TestCase):
    def test_fixture_skips_privacy_and_legacy(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "fixture.tsv"
            path.write_text(
                "\n".join(
                    [
                        "id\tenabled\tsource_kind\tcategory\tsemantic_group\ttone\ttext",
                        "a\ttrue\tcurated_authored\tDailyCare\tdaily.one\tgentle\t你先喝口水，桌子我帮你看着。",
                        "b\ttrue\tcurated_authored\tDailyCare\tdaily.one\tgentle\t雷琳玥这个名字先放着。",
                        "c\ttrue\tlegacy_surface_variant\tDailyCare\tdaily.two\tgentle\t这句不该进人物卡。",
                        "d\tfalse\tcurated_authored\tDailyCare\tdaily.three\tgentle\t关掉的句子不该进去。",
                        "e\ttrue\tcurated_authored\tStudy\tstudy.one\tcalm\t今天就看懂这一小段。",
                    ]
                ),
                encoding="utf-8",
            )
            payload = distill(path)

        texts = [item["text"] for item in payload["exemplars"]]
        self.assertEqual(2, payload["source"]["line_count"])
        self.assertIn("你先喝口水，桌子我帮你看着。", texts)
        self.assertIn("今天就看懂这一小段。", texts)
        self.assertFalse(any(marker in json.dumps(payload, ensure_ascii=False) for marker in PRIVACY_MARKERS))

    def test_published_soul_matches_the_authored_corpus(self) -> None:
        payload = json.loads(SOUL.read_text(encoding="utf-8"))
        fresh = distill(CORPUS)
        self.assertEqual(fresh, payload)
        self.assertGreater(payload["source"]["line_count"], 1000)
        self.assertEqual("佳怡", payload["name"])
        self.assertGreaterEqual(len(payload["exemplars"]), 10)
        blob = json.dumps(payload, ensure_ascii=False)
        self.assertFalse(any(marker in blob for marker in PRIVACY_MARKERS))


if __name__ == "__main__":
    unittest.main()
