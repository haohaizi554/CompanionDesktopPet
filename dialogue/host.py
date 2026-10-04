"""让手机把对话发到这台电脑。本进程拉起 dialogue/serve.py，不在手机上跑模型。"""

from __future__ import annotations

import json
import os
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

HOST = "0.0.0.0"
PORT = 8766
ROOT = Path(__file__).resolve().parents[1]
PYTHON = ROOT / "dialogue" / "python" / "Scripts" / "python.exe"
SERVE = ROOT / "dialogue" / "serve.py"
SOUL = ROOT / "data" / "persona" / "jiayi-soul.json"
CORPUS = ROOT / "data" / "optimized" / "persona-corpus-v2.tsv"
CONFIG_DIR = Path(os.environ.get("LOCALAPPDATA", "")) / "CompanionDesktopPet" / "dialogue"
USER_CONFIG = CONFIG_DIR / "llm.runtime.json"
FALLBACK_CONFIG = ROOT / "config" / "llm.runtime.json"
CHECKPOINT = CONFIG_DIR / "checkpoints.sqlite"


class DialogueHost:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._id = 0
        self._ready = False
        self._error = ""
        self._lines = 0
        self._process: subprocess.Popen[str] | None = None
        CONFIG_DIR.mkdir(parents=True, exist_ok=True)
        if not USER_CONFIG.exists() and FALLBACK_CONFIG.exists():
            USER_CONFIG.write_text(FALLBACK_CONFIG.read_text(encoding="utf-8"), encoding="utf-8")
        threading.Thread(target=self._boot, name="dialogue-boot", daemon=True).start()

    @property
    def ready(self) -> bool:
        return self._ready

    @property
    def error(self) -> str:
        return self._error

    @property
    def lines(self) -> int:
        return self._lines

    def reply(self, body: dict) -> dict:
        text = str(body.get("text") or "").strip()
        if not text:
            raise ValueError("text is required")
        message = self._request(
            {
                "type": "reply",
                "text": text,
                "thread_id": "jiayi",
                "settings": body.get("settings") if isinstance(body.get("settings"), dict) else {},
            }
        )
        if message.get("type") == "error":
            raise RuntimeError(str(message.get("message") or "对话失败"))
        return {
            "ok": True,
            "text": message.get("text") or "",
            "fallback": bool(message.get("fallback")),
            "actions": message.get("actions") or [],
        }

    def remember(self, body: dict) -> None:
        text = str(body.get("text") or "").strip()
        if not text:
            return
        message = self._request({"type": "remember", "text": text, "thread_id": "jiayi"})
        if message.get("type") == "error":
            raise RuntimeError(str(message.get("message") or "没记住"))

    def save_endpoint(self, body: dict) -> None:
        base_url = str(body.get("base_url") or "").strip()
        model = str(body.get("model") or "").strip()
        if not base_url or not model:
            raise ValueError("地址和模型名都要填")
        payload = {
            "base_url": base_url,
            "model": model,
            "api_key": str(body.get("api_key") or "local").strip() or "local",
            "auth": str(body.get("auth") or "bearer"),
            "max_tokens": 180,
            "temperature": 0.7,
            "timeout_seconds": 45,
        }
        USER_CONFIG.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
        self._restart()

    def _request(self, payload: dict) -> dict:
        with self._lock:
            process = self._process
            if not self._ready or process is None or process.poll() is not None or process.stdin is None or process.stdout is None:
                raise RuntimeError(self._error or "对话还没准备好")
            self._id += 1
            payload["id"] = self._id
            request_id = self._id
            process.stdin.write(json.dumps(payload, ensure_ascii=False) + "\n")
            process.stdin.flush()
            while True:
                line = process.stdout.readline()
                if not line:
                    self._ready = False
                    self._error = "对话进程退出了"
                    raise RuntimeError(self._error)
                message = json.loads(line)
                if message.get("id") != request_id:
                    continue
                return message

    def _restart(self) -> None:
        with self._lock:
            self._stop_locked()
            self._ready = False
            self._error = ""
        self._boot()

    def _boot(self) -> None:
        config = USER_CONFIG if USER_CONFIG.exists() else FALLBACK_CONFIG
        if not PYTHON.exists() or not SERVE.exists() or not config.exists():
            self._error = "对话运行时还没放好。"
            print(self._error, flush=True)
            return
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        env["PYTHONUTF8"] = "1"
        process = subprocess.Popen(
            [
                str(PYTHON),
                str(SERVE),
                "--config",
                str(config),
                "--soul",
                str(SOUL),
                "--corpus",
                str(CORPUS),
                "--checkpoint",
                str(CHECKPOINT),
            ],
            cwd=str(ROOT / "dialogue"),
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding="utf-8",
            errors="replace",
            bufsize=1,
            env=env,
        )
        threading.Thread(target=self._drain, args=(process,), name="dialogue-log", daemon=True).start()
        assert process.stdout is not None
        line = process.stdout.readline()
        if not line:
            self._error = "对话进程没有就绪"
            process.kill()
            return
        message = json.loads(line)
        if message.get("type") != "ready":
            self._error = str(message.get("message") or message)
            process.kill()
            return
        with self._lock:
            self._stop_locked()
            self._process = process
            self._lines = int(message.get("lines") or 0)
            self._ready = True
            self._error = ""
        print(f"ready lines={self._lines}", flush=True)

    @staticmethod
    def _drain(process: subprocess.Popen[str]) -> None:
        if process.stderr is None:
            return
        for line in process.stderr:
            print(line, end="", flush=True)

    def _stop_locked(self) -> None:
        process = self._process
        self._process = None
        if process is None or process.poll() is not None:
            return
        try:
            if process.stdin is not None:
                process.stdin.write('{"type":"exit"}\n')
                process.stdin.flush()
            process.wait(timeout=2)
        except Exception:
            process.kill()


class Handler(BaseHTTPRequestHandler):
    host: DialogueHost

    def do_GET(self) -> None:
        if urlparse(self.path).path != "/health":
            self.send_error(404)
            return
        self._json(
            200 if self.host.ready else 503,
            {"ok": self.host.ready, "lines": self.host.lines, "error": self.host.error},
        )

    def do_POST(self) -> None:
        path = urlparse(self.path).path
        length = int(self.headers.get("Content-Length") or "0")
        raw = self.rfile.read(length).decode("utf-8")
        body = json.loads(raw) if raw else {}
        try:
            if path == "/v1/reply":
                self._json(200, self.host.reply(body))
            elif path == "/v1/remember":
                self.host.remember(body)
                self._json(200, {"ok": True})
            elif path == "/v1/endpoint":
                self.host.save_endpoint(body)
                self._json(200, {"ok": self.host.ready, "error": self.host.error, "lines": self.host.lines})
            else:
                self.send_error(404)
        except Exception as error:
            self._json(500, {"ok": False, "error": str(error)})

    def log_message(self, fmt: str, *args) -> None:
        print(fmt % args, flush=True)

    def _json(self, status: int, payload: dict) -> None:
        data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


def main() -> None:
    host = DialogueHost()
    Handler.host = host
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"listening http://{HOST}:{PORT}", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
