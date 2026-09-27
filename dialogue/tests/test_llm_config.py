import json
import tempfile
import unittest
from pathlib import Path

from persona_dialogue.llm import LlmConfig, normalize_auth, normalize_base_url


class LlmConfigTests(unittest.TestCase):
    def test_normalize_accepts_host_port_and_completions_path(self) -> None:
        self.assertEqual("http://127.0.0.1:11434/v1", normalize_base_url("127.0.0.1:11434"))
        self.assertEqual("http://192.168.1.8:8080/v1", normalize_base_url("http://192.168.1.8:8080/v1/"))
        self.assertEqual("https://api.openai.com/v1", normalize_base_url("https://api.openai.com/v1/chat/completions"))

    def test_missing_auth_stays_both_so_existing_configs_keep_the_header(self) -> None:
        self.assertEqual("both", normalize_auth(None))
        self.assertEqual("bearer", normalize_auth("Bearer"))
        self.assertEqual("x-api-key", normalize_auth("x-api-key"))

    def test_load_normalizes_a_hand_written_file(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "llm.runtime.json"
            path.write_text(
                json.dumps({"base_url": "10.0.0.8:8000/v1/chat/completions", "model": " qwen ", "api_key": "local"}),
                encoding="utf-8",
            )
            config = LlmConfig.load(path)
            self.assertEqual("http://10.0.0.8:8000/v1", config.base_url)
            self.assertEqual("qwen", config.model)
            self.assertEqual("both", config.auth)


if __name__ == "__main__":
    unittest.main()
