"""Local SenseVoice recognizer; model files stay in the user cache."""
import hashlib
import io
import os
from pathlib import Path
import tarfile
import threading
import urllib.request

import numpy as np

NAME = "sherpa-onnx-sense-voice-funasr-nano-int8-2025-12-17"
URL = f"https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/{NAME}.tar.bz2"
SHA256 = "257936ea9a64cbe33200274e6367fc26d373ff6ca58b996b15108ffd6b9f6148"


class Recognizer:
    def __init__(self):
        import sherpa_onnx
        root = Path(os.environ.get("XDG_CACHE_HOME", Path.home() / ".cache")) / "sensei" / NAME
        model, tokens = root / "model.int8.onnx", root / "tokens.txt"
        if not model.is_file() or not tokens.is_file():
            root.mkdir(parents=True, exist_ok=True)
            with urllib.request.urlopen(URL, timeout=120) as response:
                data = response.read()
            if hashlib.sha256(data).hexdigest() != SHA256:
                raise ValueError("ASR model checksum mismatch")
            with tarfile.open(fileobj=io.BytesIO(data), mode="r:bz2") as archive:
                for target in (model, tokens):
                    member = archive.getmember(f"{NAME}/{target.name}")
                    if not member.isfile():
                        raise ValueError("invalid ASR archive")
                    source = archive.extractfile(member)
                    if source is None:
                        raise ValueError("invalid ASR archive")
                    temporary = target.with_suffix(target.suffix + ".tmp")
                    temporary.write_bytes(source.read())
                    temporary.replace(target)
        self.lock = threading.Lock()
        self.recognizer = sherpa_onnx.OfflineRecognizer.from_sense_voice(
            model=str(model), tokens=str(tokens), num_threads=2, sample_rate=16000,
            language="auto", use_itn=False)

    def transcribe(self, pcm: bytes) -> str:
        if len(pcm) % 2:
            raise ValueError("invalid PCM")
        stream = self.recognizer.create_stream()
        stream.accept_waveform(16000, np.frombuffer(pcm, dtype="<i2").astype(np.float32) / 32768.0)
        with self.lock:
            self.recognizer.decode_stream(stream)
        return stream.result.text.strip()
