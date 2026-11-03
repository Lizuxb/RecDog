"""One local recording per process. stdout is JSONL; dependency logs go to stderr."""
import argparse
import contextlib
import json
import math
import os
from pathlib import Path
import struct
import sys
import time
import wave


def read_request(source):
    line = source.readline(65537)
    if len(line) > 65536:
        raise ValueError("转写请求过长。")
    request = json.loads(line)
    path = request.get("audioPath")
    if not isinstance(path, str) or "://" in path or not Path(path).is_absolute():
        raise ValueError("只允许本机 WAV 文件。")
    if not Path(path).is_file():
        raise ValueError("待转写音频不存在。")
    return Path(path)


def transcribe_wav(path, recognize, emit, chunk_seconds=15):
    texts, language = [], ""
    with wave.open(str(path), "rb") as audio:
        if (audio.getframerate(), audio.getnchannels(), audio.getsampwidth(), audio.getcomptype()) != (16000, 1, 2, "NONE"):
            raise ValueError("转写输入必须是 16kHz 单声道 PCM16 WAV。")
        total = audio.getnframes()
        if not total:
            raise ValueError("音频为空。")
        emit({"type": "progress", "completedSeconds": 0, "totalSeconds": total / 16000})
        consumed = 0
        while consumed < total:
            pcm = audio.readframes(int(chunk_seconds * 16000))
            if not pcm:
                raise ValueError("音频文件被截断。")
            samples = struct.unpack("<" + "h" * (len(pcm) // 2), pcm)
            rms = math.sqrt(sum(value * value for value in samples) / len(samples)) / 32768
            if rms > 0.0001:  # digital silence filter, not speech VAD
                text, detected = recognize(pcm)
                if text.strip():
                    texts.append(text.strip())
                language = detected or language
            consumed += len(pcm) // 2
            emit({"type": "progress", "completedSeconds": consumed / 16000, "totalSeconds": total / 16000})
    return "\n".join(texts), language


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    args = parser.parse_args()
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")
    protocol = sys.stdout
    def emit(value):
        protocol.write(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n")
        protocol.flush()
    try:
        audio = read_request(sys.stdin)
        model_path = Path(args.model).resolve()
        for name in ["config.json", "model.safetensors", "tokenizer_config.json", "vocab.json", "merges.txt"]:
            if not (model_path / name).is_file():
                raise ValueError("模型缺少文件：" + name)
        if (model_path / "model.safetensors").stat().st_size < 1_000_000:
            raise ValueError("模型权重尚未下载完整，请先下载 Git LFS 文件。")
        os.environ.update(HF_HUB_OFFLINE="1", TRANSFORMERS_OFFLINE="1", HF_DATASETS_OFFLINE="1", HF_HUB_DISABLE_TELEMETRY="1", TOKENIZERS_PARALLELISM="false")
        emit({"type": "loading"})
        started = time.monotonic()
        with contextlib.redirect_stdout(sys.stderr):
            import numpy as np
            import torch
            from qwen_asr import Qwen3ASRModel
            torch.set_num_threads(min(4, os.cpu_count() or 1))
            model = Qwen3ASRModel.from_pretrained(str(model_path), dtype=torch.float32, device_map="cpu", local_files_only=True, max_inference_batch_size=1, max_new_tokens=448)
            def recognize(pcm):
                values = np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768
                with torch.inference_mode():
                    result = model.transcribe(audio=(values, 16000), language="Chinese")[0]
                return result.text, result.language
            text, language = transcribe_wav(audio, recognize, emit)
        emit({"type": "completed", "text": text, "language": language, "elapsedSeconds": round(time.monotonic() - started, 3)})
        return 0
    except Exception as error:
        emit({"type": "error", "message": str(error)[:2000]})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
