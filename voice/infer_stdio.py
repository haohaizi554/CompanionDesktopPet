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
    parser.add_argument("--warmup-ref", default="")
    parser.add_argument("--warmup-prompt", default="")
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

    if str(args.device).startswith("cuda"):
        import torch

        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
        torch.set_float32_matmul_precision("high")
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
    emit({"ready": True})
    with gate:
        if pending is None and not stopped:
            gate.wait(timeout=0.8)
        busy = pending is not None or stopped
    if not busy:
        _warmup(pipeline, args.warmup_ref, args.warmup_prompt)

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


def _content_chars(text: str) -> int:
    return sum(1 for char in text if char.isalnum() or "\u4e00" <= char <= "\u9fff")


def _prepare_text(text: str) -> str:
    """A leading pause is what the sampler drops, instead of the first clause."""
    text = text.strip()
    if text and text[0] not in "。，、！？,.!?":
        text = "。" + text
    if text and text[-1] not in "。，、！？,.!?…":
        text += "。"
    return text


def _split_method(text: str) -> str:
    # cut0 keeps one breath. cut2/cut5 split on commas and those short pieces get swallowed.
    return "cut0" if len(text) <= 80 else "cut1"


def _audible(audio) -> bool:
    import numpy as np

    audio = np.asarray(audio)
    return audio.size > 0 and int(np.max(np.abs(audio))) >= 32


def _duration_floor(text: str, speed: float) -> float:
    chars = _content_chars(text)
    if chars <= 0:
        return 0.0
    return max(0.45, chars * 0.055 / max(speed, 0.5))


def _keeps_first_take(text: str, sample_count: int, sample_rate: int, speed: float) -> bool:
    """A slightly short clip is kept. Only a swallowed take is synthesized again."""
    floor = _duration_floor(text, speed)
    if floor <= 0 or sample_rate <= 0 or sample_count <= 0:
        return True
    duration = sample_count / float(sample_rate)
    return duration + 1e-6 >= floor * 0.62


def _long_enough(text: str, audio, sample_rate: int, speed: float) -> bool:
    return _keeps_first_take(text, int(getattr(audio, "size", 0) or 0), sample_rate, speed)


def _collect(generator):
    import numpy as np

    pieces = []
    sample_rate = 32000
    for sample_rate, audio in generator:
        audio = np.asarray(audio)
        if audio.ndim > 1:
            audio = np.squeeze(audio)
        audio = audio.reshape(-1)
        if _audible(audio):
            pieces.append(audio)
    if not pieces:
        return int(sample_rate), None
    if len(pieces) == 1:
        return int(sample_rate), pieces[0]
    return int(sample_rate), np.concatenate(pieces)


def _trim_edges(audio, sample_rate: int):
    """Drop the dead air a leading pause leaves, and the tail after the last word.

    Piper and Kokoro do the same before playback. A short pad stays so the
    attack is not cut off. A clip that is quiet all the way through is kept.
    """
    import numpy as np

    samples = np.asarray(audio).reshape(-1)
    if samples.size == 0 or sample_rate <= 0:
        return samples
    level = 0.004 if np.issubdtype(samples.dtype, np.floating) else 128
    loud = np.flatnonzero(np.abs(samples) >= level)
    if loud.size == 0:
        return samples
    pad = int(sample_rate * 0.03)
    start = max(0, int(loud[0]) - pad)
    end = min(int(samples.size), int(loud[-1]) + 1 + pad)
    if end - start < int(sample_rate * 0.05):
        return samples
    return samples[start:end]


def _warmup(pipeline, ref_path: str, prompt: str) -> None:
    """One discarded line fills the reference cache and the CUDA kernels.

    CosyVoice warms the speaker cache the same way. A line already waiting
    skips this, so waking the process to speak does not synthesize twice.
    """
    if not ref_path or not os.path.isfile(ref_path):
        return
    try:
        generator = pipeline.run(
            {
                "text": _prepare_text("嗯"),
                "text_lang": "zh",
                "ref_audio_path": ref_path,
                "prompt_text": prompt or "",
                "prompt_lang": "zh",
                "text_split_method": "cut0",
                "batch_size": 1,
                "split_bucket": False,
                "speed_factor": 1.0,
                "temperature": 1.0,
                "top_k": 15,
                "top_p": 1.0,
                "repetition_penalty": 1.35,
                "seed": 1,
                "parallel_infer": True,
                "streaming_mode": False,
                "return_fragment": False,
            }
        )
        for _sample_rate, _audio in generator:
            pass
    except Exception as error:
        print(f"warmup skipped: {error}", file=sys.stderr)


def _synthesize(pipeline, request: dict) -> bool:
    import soundfile as sf

    raw = str(request.get("text") or "").strip()
    out_path = request.get("out_path")
    if not raw or not out_path:
        raise ValueError("text and out_path are required")

    text = _prepare_text(raw)
    speed = _clamped(request.get("speed_factor"), 1.0, 0.5, 2.0)
    temperature = _clamped(request.get("temperature"), 1.0, 0.2, 1.5)
    repetition = _clamped(request.get("repetition_penalty"), 1.35, 1.0, 2.0)
    top_k = int(_clamped(request.get("top_k"), 15, 1, 50))
    top_p = _clamped(request.get("top_p"), 1.0, 0.1, 1.0)
    best_rate = None
    best_audio = None
    for seed in (-1, 1):
        generator = pipeline.run(
            {
                "text": text,
                "text_lang": request.get("text_lang") or "zh",
                "ref_audio_path": request["ref_audio_path"],
                "prompt_text": request.get("prompt_text") or "",
                "prompt_lang": request.get("prompt_lang") or "zh",
                "text_split_method": _split_method(raw),
                "batch_size": 1,
                "split_bucket": False,
                "speed_factor": speed,
                "temperature": temperature,
                "top_k": top_k,
                "top_p": top_p,
                "repetition_penalty": repetition,
                "seed": seed,
                "parallel_infer": True,
                "streaming_mode": False,
                "return_fragment": False,
            }
        )
        sample_rate, audio = _collect(generator)
        if getattr(pipeline, "stop_flag", False):
            return False
        if audio is None:
            continue
        if best_audio is None or audio.size > best_audio.size:
            best_rate = sample_rate
            best_audio = audio
        if _long_enough(raw, audio, sample_rate, speed):
            break
    if best_audio is None or best_rate is None:
        raise RuntimeError("合成结果没有声音")
    best_audio = _trim_edges(best_audio, int(best_rate))
    os.makedirs(os.path.dirname(os.path.abspath(out_path)), exist_ok=True)
    sf.write(out_path, best_audio, int(best_rate))
    return True


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except KeyboardInterrupt:
        raise SystemExit(0)
