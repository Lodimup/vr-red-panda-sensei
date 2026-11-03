"""Speech recognition: Muse Voice Transcribe (Meta Model API), or local SenseVoice as fallback."""
import hashlib
import io
import json
import os
from pathlib import Path
import tarfile
import threading
import urllib.request
import uuid
import wave

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


MUSE_URL = "https://api.meta.ai/v1/asr/transcribe"
MUSE_MODEL = "muse-voice-transcribe-1.0"


class MuseRecognizer:
    """Muse Voice Transcribe (Meta Model API), batch push-to-talk endpoint."""

    def __init__(self, api_key: str, url: str = MUSE_URL, timeout: float = 20.0):
        if not api_key:
            raise ValueError("missing Muse API key")
        self.api_key, self.url, self.timeout = api_key, url, timeout

    def transcribe(self, pcm: bytes) -> str:
        if len(pcm) % 2:
            raise ValueError("invalid PCM")
        wav = io.BytesIO()
        with wave.open(wav, "wb") as out:
            out.setnchannels(1)
            out.setsampwidth(2)
            out.setframerate(16000)
            out.writeframes(pcm)
        settings = {"model": MUSE_MODEL, "audioEncoding": "WAV", "mode": "PUSH_TO_TALK",
                    "languageBias": ["Japanese", "English"]}
        boundary = uuid.uuid4().hex
        body = b"".join([
            f'--{boundary}\r\nContent-Disposition: form-data; name="request"\r\n'
            f"Content-Type: application/json\r\n\r\n{json.dumps(settings)}\r\n".encode(),
            f'--{boundary}\r\nContent-Disposition: form-data; name="audio"; filename="turn.wav"\r\n'
            f"Content-Type: audio/wav\r\n\r\n".encode(), wav.getvalue(), f"\r\n--{boundary}--\r\n".encode()])
        request = urllib.request.Request(self.url, data=body, method="POST", headers={
            "Authorization": f"Bearer {self.api_key}", "Accept": "application/json",
            "Content-Type": f"multipart/form-data; boundary={boundary}"})
        with urllib.request.urlopen(request, timeout=self.timeout) as response:
            result = json.load(response)
        text = result.get("transcript") if isinstance(result, dict) else None
        if not isinstance(text, str):
            raise ValueError("invalid ASR response")
        return text.strip()


def create_recognizer():
    """Muse Voice Transcribe when SENSEI_ASR_API_KEY is set, else local SenseVoice."""
    key = os.getenv("SENSEI_ASR_API_KEY")
    return MuseRecognizer(key) if key else Recognizer()
