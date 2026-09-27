"""Parameter-only GPT-SoVITS v2Pro inference for the desktop pet.

The process stays up, reads one JSON object per line from stdin, and writes
wav files. Library logs go to stderr so the protocol stream stays clean.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import tempfile
import threading


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Companion pet TTS inference")
    parser.add_argument("--root", required=True)
    parser.add_argument("--gpt", required=True)
    parser.add_argument("--sovits", required=True)
    parser.add_argument("--bert", required=True)
    parser.add_argument("--hubert", required=True)
    parser.add_argument("--device", default="cuda")
    return parser.parse_args()


def _allow_long_references(TTS) -> None:
    """The upstream pack rejects reference audio outside 3–10 seconds.

    The pet library starts at 7 seconds and keeps going past 10, so only the
    short-audio guard stays.
    """
    import librosa
    import numpy as np
    import torch

    def _set_prompt_semantic(self, ref_wav_path: str):
        zero_wav = np.zeros(
            int(self.configs.sampling_rate * 0.3),
            dtype=np.float16 if self.configs.is_half else np.float32,
        )
        with torch.no_grad():
            wav16k, _sr = librosa.load(ref_wav_path, sr=16000)
            if wav16k.shape[0] < 48000:
                raise OSError("参考音频短于3秒")
            wav16k = torch.from_numpy(wav16k)
            zero_wav_torch = torch.from_numpy(zero_wav)
            wav16k = wav16k.to(self.configs.device)
            zero_wav_torch = zero_wav_torch.to(self.configs.device)
            if self.configs.is_half:
                wav16k = wav16k.half()
                zero_wav_torch = zero_wav_torch.half()

            wav16k = torch.cat([wav16k, zero_wav_torch])
            hubert_feature = self.cnhuhbert_model.model(wav16k.unsqueeze(0))["last_hidden_state"].transpose(1, 2)
            codes = self.vits_model.extract_latent(hubert_feature)
            self.prompt_cache["prompt_semantic"] = codes[0, 0].to(self.configs.device)

    TTS._set_prompt_semantic = _set_prompt_semantic


def _ignore_missing_japanese_dictionary() -> None:
    """Chinese lines are split with a Japanese word-frequency check.

    That check imports the ipadic dictionary. This runtime does not ship it,
    and the missing import used to abort the whole sentence before any audio
    was written.
    """
    import wordfreq
    from wordfreq import word_frequency as original

    def word_frequency(word, lang, wordlist="best", minimum=0.0):
        if str(lang).lower().startswith("ja"):
            return minimum
        return original(word, lang, wordlist=wordlist, minimum=minimum)

    wordfreq.word_frequency = word_frequency
    import split_lang.detect_lang.detector as detector

    detector.word_frequency = word_frequency


def _prepare_imports(root: str):
    os.chdir(root)
    sys.path.insert(0, root)
    sys.path.insert(0, os.path.join(root, "GPT_SoVITS"))
    from GPT_SoVITS.TTS_infer_pack.TTS import TTS, TTS_Config

    return TTS, TTS_Config


def main() -> int:
    args = _parse_args()
    protocol = os.fdopen(os.dup(sys.stdout.fileno()), "w", encoding="utf-8", buffering=1)
    sys.stdout = sys.stderr
    sys.stdin.reconfigure(encoding="utf-8")

    def emit(payload: dict) -> None:
        protocol.write(json.dumps(payload, ensure_ascii=False) + "\n")
        protocol.flush()

    _ignore_missing_japanese_dictionary()
    TTS, TTS_Config = _prepare_imports(args.root)
    _allow_long_references(TTS)
    config = TTS_Config(
        {
            "custom": {
                "bert_base_path": args.bert,
                "cnhuhbert_base_path": args.hubert,
                "device": args.device,
                "is_half": args.device != "cpu",
                "t2s_weights_path": args.gpt,
                "version": "v2Pro",
                "vits_weights_path": args.sovits,
            }
        }
    )
    # Weight loading rewrites the config path. Keep that write out of the
    # GPT-SoVITS checkout so the desktop install's yaml stays untouched.
    temp_config = tempfile.NamedTemporaryFile(suffix=".yaml", delete=False)
    temp_config.close()
    config.configs_path = temp_config.name
    pipeline = TTS(config)
    emit({"ready": True})

    pending: dict | None = None
    stopped = False
    gate = threading.Condition()

    def reader() -> None:
        nonlocal pending, stopped
        for line in sys.stdin:
            text = line.strip()
            if not text:
                continue
            try:
                message = json.loads(text)
            except json.JSONDecodeError as error:
                emit({"ok": False, "error": f"bad request: {error}"})
                continue
            with gate:
                command = message.get("cmd")
                if command == "exit":
                    stopped = True
                    pending = None
                    pipeline.stop()
                    gate.notify()
                    return
                if command == "cancel":
                    pending = None
                    pipeline.stop()
                    gate.notify()
                    continue
                # A new line waits its turn. Stopping here used to abort the
                # sentence already on the GPU whenever the pet was clicked again.
                pending = message
                gate.notify()
        with gate:
            stopped = True
            gate.notify()

    threading.Thread(target=reader, name="voice-requests", daemon=True).start()

    while True:
        with gate:
            while pending is None and not stopped:
                gate.wait()
            if stopped and pending is None:
                return 0
            request = pending
            pending = None

        request_id = request.get("id")
        try:
            if _synthesize(pipeline, request):
                emit({"id": request_id, "ok": True, "path": request.get("out_path")})
            else:
                emit({"id": request_id, "ok": False, "error": "cancelled"})
        except Exception as error:
            emit({"id": request_id, "ok": False, "error": str(error)})


def _clamped(value, default: float, low: float, high: float) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return default
    if number != number:
        return default
    return min(high, max(low, number))


def _synthesize(pipeline, request: dict) -> bool:
    import numpy as np
    import soundfile as sf

    text = str(request.get("text") or "").strip()
    out_path = request.get("out_path")
    if not text or not out_path:
        raise ValueError("text and out_path are required")

    generator = pipeline.run(
        {
            "text": text,
            "text_lang": request.get("text_lang") or "zh",
            "ref_audio_path": request["ref_audio_path"],
            "prompt_text": request.get("prompt_text") or "",
            "prompt_lang": request.get("prompt_lang") or "zh",
            "text_split_method": "cut2",
            "batch_size": 1,
            "speed_factor": _clamped(request.get("speed_factor"), 1.0, 0.5, 2.0),
            "temperature": _clamped(request.get("temperature"), 1.0, 0.2, 1.5),
            "repetition_penalty": _clamped(request.get("repetition_penalty"), 1.35, 1.0, 2.0),
            "seed": -1,
            "parallel_infer": True,
            "repetition_penalty": 1.35,
            "streaming_mode": False,
            "return_fragment": False,
        }
    )
    sample_rate, audio = next(generator)
    if pipeline.stop_flag:
        return False
    audio = np.asarray(audio)
    if audio.ndim > 1:
        audio = audio.reshape(-1)
    if audio.size == 0 or int(np.max(np.abs(audio))) < 32:
        raise RuntimeError("合成结果没有声音")
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    sf.write(out_path, audio, int(sample_rate))
    return True


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(0)
