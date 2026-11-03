"""Local sherpa-onnx online Zipformer worker. JSONL protocol on stdin/stdout."""
import argparse
import base64
import contextlib
import json
import os
from pathlib import Path
import struct
import sys


def emit(value):
    sys.stdout.write(json.dumps(value, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def load_model(path):
    path = Path(path)
    tokens = path / "tokens.txt"
    encoder = path / "encoder.int8.onnx"
    decoder = path / "decoder.onnx"
    joiner = path / "joiner.int8.onnx"
    # ModelScope's bilingual package keeps the upstream epoch names and BPE vocabulary.
    if not all(p.is_file() for p in (encoder, decoder, joiner)):
        encoder = path / "encoder-epoch-99-avg-1.int8.onnx"
        decoder = path / "decoder-epoch-99-avg-1.int8.onnx"
        joiner = path / "joiner-epoch-99-avg-1.int8.onnx"
        tokens = path / "tokens.txt"
    missing = [p.name for p in (tokens, encoder, decoder, joiner) if not p.is_file()]
    if missing:
        raise FileNotFoundError("流式中文模型文件不完整：" + ", ".join(missing))
    os.environ["HF_HUB_OFFLINE"] = "1"
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    import sherpa_onnx
    kwargs = dict(
        tokens=str(tokens), encoder=str(encoder), decoder=str(decoder), joiner=str(joiner),
        num_threads=max(1, min(4, os.cpu_count() or 1)), sample_rate=16000,
        enable_endpoint_detection=True, rule1_min_trailing_silence=1.2,
        rule2_min_trailing_silence=0.6, rule3_min_utterance_length=20.0,
        provider="cpu")
    if (path / "bpe.model").is_file():
        kwargs.update(model_type="zipformer", modeling_unit="bpe", bpe_vocab=str(path / "bpe.model"))
    recognizer = sherpa_onnx.OnlineRecognizer.from_transducer(**kwargs)
    return recognizer


def decode(recognizer, stream):
    while recognizer.is_ready(stream):
        recognizer.decode_stream(stream)


def resample(samples, source_rate=44100, target_rate=16000):
    if source_rate == target_rate:
        return samples
    size = max(1, round(len(samples) * target_rate / source_rate))
    return [samples[min(len(samples) - 1, round(i * source_rate / target_rate))] for i in range(size)]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    args = parser.parse_args()
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    try:
        with contextlib.redirect_stdout(sys.stderr):
            recognizer = load_model(args.model)
        stream = recognizer.create_stream()
        previous = ""
        paused = False
        emit({"type": "ready"})
        for line in sys.stdin:
            try:
                request = json.loads(line)
                kind = request.get("type")
                if kind == "audio":
                    if paused:
                        continue
                    raw = base64.b64decode(request.get("pcm", ""), validate=True)
                    if len(raw) % 2:
                        raise ValueError("PCM16 音频数据长度无效。")
                    samples = [v / 32768.0 for v in struct.unpack("<" + "h" * (len(raw) // 2), raw)]
                    if not samples:
                        continue
                    stream.accept_waveform(16000, resample(samples, 44100, 16000))
                    decode(recognizer, stream)
                    text = recognizer.get_result(stream)
                    endpoint = recognizer.is_endpoint(stream)
                    if text != previous or endpoint:
                        emit({"type": "transcript", "text": text, "isFinal": endpoint, "state": "listening"})
                        previous = "" if endpoint else text
                    if endpoint:
                        recognizer.reset(stream)
                elif kind == "pause":
                    paused = True
                    emit({"type": "state", "state": "paused"})
                elif kind == "resume":
                    paused = False
                    emit({"type": "state", "state": "listening"})
                elif kind == "finish":
                    stream.input_finished()
                    decode(recognizer, stream)
                    text = recognizer.get_result(stream)
                    if text:
                        emit({"type": "transcript", "text": text, "isFinal": True, "state": "stopped"})
                    emit({"type": "state", "state": "stopped"})
                    return 0
                else:
                    raise ValueError("未知流式转写指令。")
            except Exception as error:
                emit({"type": "error", "message": str(error)[:1000]})
        return 0
    except Exception as error:
        emit({"type": "error", "message": str(error)[:1000]})
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
