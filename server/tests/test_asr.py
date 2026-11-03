import email.parser
import email.policy
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import io
import json
import threading
import wave

import pytest

from sensei_server import asr


def serve(reply, status=200):
    seen = {}

    class Handler(BaseHTTPRequestHandler):
        def do_POST(self):
            body = self.rfile.read(int(self.headers["Content-Length"]))
            seen["auth"] = self.headers["Authorization"]
            message = email.parser.BytesParser(policy=email.policy.HTTP).parsebytes(
                f"Content-Type: {self.headers['Content-Type']}\r\n\r\n".encode() + body)
            seen["parts"] = {part.get_param("name", header="content-disposition"): part.get_content()
                             for part in message.iter_parts()}
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(json.dumps(reply).encode())

        def log_message(self, *args):
            pass

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return server, f"http://127.0.0.1:{server.server_port}/v1/asr/transcribe", seen


def test_muse_recognizer_posts_wav_and_returns_transcript():
    server, url, seen = serve({"sessionId": "s", "transcript": " りんごをください ", "turns": []})
    try:
        pcm = (b"\x01\x00" * 1600)
        text = asr.MuseRecognizer("test-key", url=url).transcribe(pcm)
    finally:
        server.shutdown()
    assert text == "りんごをください"
    assert seen["auth"] == "Bearer test-key"
    request = seen["parts"]["request"]
    request = json.loads(request)
    assert request["model"] == "muse-voice-transcribe-1.0"
    assert request["audioEncoding"] == "WAV" and request["mode"] == "PUSH_TO_TALK"
    assert "Japanese" in request["languageBias"]
    with wave.open(io.BytesIO(seen["parts"]["audio"])) as audio:
        assert (audio.getnchannels(), audio.getsampwidth(), audio.getframerate()) == (1, 2, 16000)
        assert audio.readframes(audio.getnframes()) == pcm


def test_muse_recognizer_rejects_bad_response_and_http_errors():
    server, url, _ = serve({"error": "nope"})
    try:
        with pytest.raises(ValueError):
            asr.MuseRecognizer("k", url=url).transcribe(b"\x00\x00")
    finally:
        server.shutdown()
    server, url, _ = serve({"error": "rate"}, status=429)
    try:
        with pytest.raises(Exception):
            asr.MuseRecognizer("k", url=url).transcribe(b"\x00\x00")
    finally:
        server.shutdown()
    with pytest.raises(ValueError):
        asr.MuseRecognizer("k").transcribe(b"\x00")


def test_create_recognizer_prefers_muse_when_key_set(monkeypatch):
    monkeypatch.setenv("SENSEI_ASR_API_KEY", "k")
    assert isinstance(asr.create_recognizer(), asr.MuseRecognizer)
    monkeypatch.delenv("SENSEI_ASR_API_KEY")
    monkeypatch.setattr(asr, "Recognizer", lambda: "sensevoice")
    assert asr.create_recognizer() == "sensevoice"
