import importlib.util
import io
import json
from pathlib import Path
import struct
import tempfile
import unittest
import wave

spec = importlib.util.spec_from_file_location("worker", Path(__file__).with_name("worker.py"))
worker = importlib.util.module_from_spec(spec)
spec.loader.exec_module(worker)


class WorkerTests(unittest.TestCase):
    def make_wav(self, path, samples, rate=16000, channels=1):
        with wave.open(str(path), "wb") as output:
            output.setnchannels(channels)
            output.setsampwidth(2)
            output.setframerate(rate)
            output.writeframes(struct.pack("<" + "h" * len(samples), *samples))

    def test_chunks_bounded_and_silence_skips_inference(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "audio.wav"
            self.make_wav(path, [4000] * 16000 + [0] * 16000 + [5000] * 8000)
            calls, events = [], []
            def recognize(pcm):
                calls.append(len(pcm))
                return "中文结果", "Chinese"
            text, language = worker.transcribe_wav(path, recognize, events.append, chunk_seconds=1)
            self.assertEqual(calls, [32000, 16000])
            self.assertEqual(text, "中文结果\n中文结果")
            self.assertEqual(language, "Chinese")
            self.assertEqual(events[-1]["completedSeconds"], 2.5)

    def test_rejects_wrong_format_and_url(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "wrong.wav"
            self.make_wav(path, [1] * 100, rate=44100)
            with self.assertRaises(ValueError):
                worker.transcribe_wav(path, lambda _: ("", ""), lambda _: None)
            with self.assertRaises(ValueError):
                worker.read_request(io.StringIO(json.dumps({"audioPath": "https://example.com/a.wav"})))

    def test_empty_audio_returns_no_invented_text(self):
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / "silence.wav"
            self.make_wav(path, [0] * 16000)
            def forbidden(_):
                raise AssertionError("silence must not invoke generative ASR")
            text, _ = worker.transcribe_wav(path, forbidden, lambda _: None)
            self.assertEqual(text, "")


if __name__ == "__main__":
    unittest.main()
