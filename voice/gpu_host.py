"""让手机把要念的句子发到这台电脑，由这台电脑的显卡合成。

手机不跑 Torch。本进程拉起现有的 infer_stdio.py，设备固定为 cuda。
"""

from __future__ import annotations

import json
import queue
import subprocess
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

HOST = "0.0.0.0"
PORT = 8765
SPEAK_TIMEOUT_SECONDS = 75
ROOT = Path(__file__).resolve().parents[1]
VOICE = ROOT / "voice"
PACK = VOICE / "packs" / "jiayi"
ENGINE = VOICE / "engine"
PYTHON = VOICE / "python" / "python.exe"
OUT = VOICE / "gpu-out"
LOG = VOICE / "gpu-host.log"


def _select_ref(refs: list, tone: str, text: str, trigger: str) -> dict:
    """跟桌面 VoicePack.Select 同一套：语气、长短、问句、提醒。"""
    if tone.lower() == "dry_warm":
        tone = "gentle"
    pool = [ref for ref in refs if tone and _has(ref, "tones", tone)]
    if not pool:
        pool = [ref for ref in refs if ref.get("fallback")] or list(refs)
    han = sum(1 for char in text if "\u4e00" <= char <= "\u9fff")

    def fits(ref: dict) -> bool:
        seconds = float(ref.get("seconds") or 0)
        if seconds <= 0:
            return True
        if han <= 18:
            return seconds <= 11.5
        if han >= 36:
            return seconds >= 12
        return seconds < 16

    fitted = [ref for ref in pool if fits(ref)]
    if fitted:
        pool = fitted
    trigger_name = trigger.lower()
    if trigger_name in {"latenight", "evening"}:
        pool = _prefer(pool, "night")
    if any(mark in text for mark in ("？", "?", "吗", "呢")):
        pool = _prefer(pool, "question")
    elif any(mark in text for mark in ("别", "不要", "小心", "注意", "千万")):
        pool = _prefer(pool, "warning")
    if len(pool) <= 1:
        return pool[0]
    hash_value = 2166136261
    for char in text:
        hash_value ^= ord(char)
        hash_value = (hash_value * 16777619) & 0xFFFFFFFF
    return pool[hash_value % len(pool)]


def _prefer(pool: list, situation: str) -> list:
    matched = [ref for ref in pool if _has(ref, "situations", situation)]
    return matched or pool


def _has(ref: dict, key: str, expected: str) -> bool:
    return any(str(item).lower() == expected.lower() for item in ref.get(key) or [])


def _infer_pids() -> list[int]:
    completed = subprocess.run(
        [
            "powershell",
            "-NoProfile",
            "-Command",
            "Get-CimInstance Win32_Process -Filter \"Name='python.exe'\" | "
            "Where-Object { $_.CommandLine -like '*infer_stdio.py*' } | "
            "ForEach-Object { $_.ProcessId }",
        ],
        capture_output=True,
        text=True,
        encoding="utf-8",
        errors="replace",
        check=False,
    )
    return [int(line) for line in (completed.stdout or "").splitlines() if line.strip().isdigit()]


def _manifest() -> dict:
    return json.loads((PACK / "manifest.json").read_text(encoding="utf-8"))


def _reference(manifest: dict) -> dict:
    refs = manifest["refs"]
    for ref in refs:
        if ref.get("fallback"):
            return ref
    return refs[0]


class GpuVoice:
    def __init__(self) -> None:
        self._manifest = _manifest()
        self._ref = _reference(self._manifest)
        self._lock = threading.Lock()
        self._admit = threading.Lock()
        self._seq = 0
        self._id = 0
        self._ready = False
        self._error = ""
        self._process: subprocess.Popen[str] | None = None
        self._results: queue.Queue[str | None] = queue.Queue()
        self._reader_started = False
        self._gpu = self._probe_gpu()
        OUT.mkdir(parents=True, exist_ok=True)
        threading.Thread(target=self._boot, name="gpu-boot", daemon=True).start()

    @property
    def ready(self) -> bool:
        return self._ready

    @property
    def error(self) -> str:
        return self._error

    @property
    def gpu(self) -> str:
        return self._gpu

    def speak(self, body: dict) -> Path:
        if not self._ready or self._process is None or self._process.poll() is not None:
            raise RuntimeError(self._error or "显卡模型还没就绪")
        text = str(body.get("text") or "").strip()
        if not text:
            raise ValueError("text is required")
        with self._admit:
            self._seq += 1
            ticket = self._seq
        with self._lock:
            with self._admit:
                if ticket != self._seq:
                    raise RuntimeError("superseded")
            self._id += 1
            request_id = self._id
            out_path = OUT / f"{request_id}.wav"
            ref = _select_ref(
                self._manifest["refs"],
                str(body.get("tone") or ""),
                text,
                str(body.get("trigger") or ""),
            )
            ref_audio = (PACK / ref["audio"]).resolve()
            request = {
                "id": request_id,
                "text": text,
                "text_lang": self._manifest.get("textLang") or "zh",
                "ref_audio_path": str(ref_audio),
                "prompt_text": ref.get("prompt") or "",
                "prompt_lang": ref.get("promptLang") or "zh",
                "out_path": str(out_path),
                "speed_factor": body.get("speed_factor", 1),
                "temperature": body.get("temperature", 1),
                "repetition_penalty": body.get("repetition_penalty", 1.35),
                "top_k": body.get("top_k", 15),
                "top_p": body.get("top_p", 1),
            }
            assert self._process.stdin is not None
            assert self._process.stdout is not None
            self._start_result_reader()
            self._process.stdin.write(json.dumps(request, ensure_ascii=False) + "\n")
            self._process.stdin.flush()
            self._wait_result(request_id)
            if not out_path.is_file() or out_path.stat().st_size < 44:
                raise RuntimeError("语音没有返回音频")
            return out_path

    def _probe_gpu(self) -> str:
        completed = subprocess.run(
            [str(PYTHON), "-c", "import torch; print(torch.cuda.get_device_name(0) if torch.cuda.is_available() else '')"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            check=False,
        )
        name = (completed.stdout or "").strip()
        if not name:
            raise RuntimeError((completed.stderr or "这台电脑的 CUDA 不可用").strip())
        return name

    def _boot(self) -> None:
        announced = 0
        while True:
            foreign = _infer_pids()
            if not foreign:
                break
            if foreign[0] != announced:
                self._error = "桌面版正在占用这张显卡，手机这条先等它空出来"
                print(f"{self._error} pid={foreign[0]}", flush=True)
                announced = foreign[0]
            time.sleep(3)
        gpt = PACK / self._manifest["gpt"]
        sovits = PACK / self._manifest["sovits"]
        bert = ENGINE / "GPT_SoVITS" / "pretrained_models" / "chinese-roberta-wwm-ext-large"
        hubert = ENGINE / "GPT_SoVITS" / "pretrained_models" / "chinese-hubert-base"
        ref_audio = (PACK / self._ref["audio"]).resolve()
        command = [
            str(PYTHON),
            str(VOICE / "infer_stdio.py"),
            "--root",
            str(ENGINE),
            "--gpt",
            str(gpt),
            "--sovits",
            str(sovits),
            "--bert",
            str(bert),
            "--hubert",
            str(hubert),
            "--device",
            "cuda",
            "--warmup-ref",
            str(ref_audio),
            "--warmup-prompt",
            self._ref.get("prompt") or "",
        ]
        log = LOG.open("a", encoding="utf-8")
        self._process = subprocess.Popen(
            command,
            stdin=subprocess.PIPE,
            stdout=subprocess.PIPE,
            stderr=log,
            text=True,
            encoding="utf-8",
            bufsize=1,
        )
        assert self._process.stdout is not None
        line = self._process.stdout.readline()
        if not line:
            self._error = "语音进程没有就绪"
            return
        message = json.loads(line)
        if not message.get("ready"):
            self._error = str(message)
            return
        self._start_result_reader()
        self._error = ""
        self._ready = True
        print(f"ready cuda {self._gpu}", flush=True)

    def _start_result_reader(self) -> None:
        if self._reader_started or self._process is None or self._process.stdout is None:
            return
        stdout = self._process.stdout
        self._reader_started = True

        def pump() -> None:
            try:
                for line in stdout:
                    self._results.put(line)
            finally:
                self._results.put(None)

        threading.Thread(target=pump, name="voice-results", daemon=True).start()

    def _wait_result(self, request_id: int) -> dict:
        deadline = time.monotonic() + SPEAK_TIMEOUT_SECONDS
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError("语音合成超时，没有返回结果")
            try:
                line = self._results.get(timeout=remaining)
            except queue.Empty:
                raise TimeoutError("语音合成超时，没有返回结果")
            if not line:
                raise RuntimeError("语音进程退出了")
            try:
                message = json.loads(line)
            except json.JSONDecodeError as error:
                raise RuntimeError(f"语音返回的不是结果：{error}") from error
            if message.get("id") != request_id:
                continue
            if not message.get("ok"):
                raise RuntimeError(str(message.get("error") or "合成失败"))
            return message


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    voice: GpuVoice

    def end_headers(self) -> None:
        self.close_connection = True
        self.send_header("Connection", "close")
        super().end_headers()

    def do_GET(self) -> None:
        if urlparse(self.path).path != "/health":
            self.send_error(404)
            return
        payload = {
            "ok": self.voice.ready,
            "device": "cuda",
            "gpu": self.voice.gpu,
            "error": self.voice.error,
        }
        self._json(200 if self.voice.ready else 503, payload)

    def do_POST(self) -> None:
        if urlparse(self.path).path != "/v1/speak":
            self.send_error(404)
            return
        length = int(self.headers.get("Content-Length") or "0")
        raw = self.rfile.read(length).decode("utf-8")
        try:
            body = json.loads(raw) if raw else {}
            path = self.voice.speak(body)
            data = path.read_bytes()
            if len(data) < 44:
                raise RuntimeError("语音没有返回音频")
        except Exception as error:
            self._json(500, {"ok": False, "error": str(error)})
            return
        self.send_response(200)
        self.send_header("Content-Type", "audio/wav")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

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
    voice = GpuVoice()
    Handler.voice = voice
    server = ThreadingHTTPServer((HOST, PORT), Handler)
    print(f"listening http://{HOST}:{PORT} device=cuda", flush=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
