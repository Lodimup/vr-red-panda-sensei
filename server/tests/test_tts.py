import asyncio
import threading

import numpy as np

from fastapi.testclient import TestClient

from sensei_server.app import Session, app
from sensei_server.tts import CachedSynthesizer, FakeSynthesizer


class Stub(CachedSynthesizer):
    def __init__(self):
        super().__init__()
        self.calls = []

    def generate(self, text, lang):
        self.calls.append((text, lang))
        return text.encode()


class Socket:
    def __init__(self):
        self.frames = []

    async def send_json(self, msg):
        self.frames.append(msg)

    async def send_bytes(self, data):
        self.frames.append(data)


def test_sentence_order_and_cached_replay(tmp_path, monkeypatch):
    monkeypatch.setenv('SENSEI_PROFILE_DIR', str(tmp_path))
    stub, socket = Stub(), Socket()
    monkeypatch.setattr(app.state, 'tts', stub, raising=False)
    session = Session(socket, 'tts')
    session.generation = 1
    segments = [{'lang': 'ja', 'text': 'こんにちは。元気？'}, {'lang': 'en', 'text': 'Hello! Good day.'}]
    asyncio.run(session.line(1, segments))
    asyncio.run(session.line(1, segments))
    assert stub.calls == [('こんにちは。', 'ja'), ('元気？', 'ja'), ('Hello!', 'en'), ('Good day.', 'en')]
    lines = [i for i, x in enumerate(socket.frames) if isinstance(x, dict) and x['type'] == 'line']
    headers = [x for x in socket.frames if isinstance(x, dict) and x['type'] == 'speak_chunk']
    assert lines[0] < socket.frames.index(headers[0])
    assert [h['segment_index'] for h in headers[:4]] == [0, 0, 1, 1]
    assert [h['chunk_index'] for h in headers[:4]] == [0, 1, 2, 3]
    assert [h['final'] for h in headers[:4]] == [False, False, False, True]
    assert headers[0]['utterance_id'] != headers[4]['utterance_id']
    for i, frame in enumerate(socket.frames):
        if isinstance(frame, dict) and frame['type'] == 'speak_chunk':
            assert socket.frames[i + 1] == frame['text'].encode()


def test_cancel_drops_inflight_synthesis(tmp_path, monkeypatch):
    monkeypatch.setenv('SENSEI_PROFILE_DIR', str(tmp_path))
    started, release = threading.Event(), threading.Event()
    class Slow(Stub):
        def generate(self, text, lang):
            started.set()
            release.wait(5)
            return super().generate(text, lang)
    async def run():
        socket = Socket()
        monkeypatch.setattr(app.state, 'tts', Slow(), raising=False)
        session = Session(socket, 'cancel')
        session.generation = 1
        session.running = asyncio.create_task(session.line(1, [{'lang': 'en', 'text': 'First. Second.'}]))
        await asyncio.to_thread(started.wait, 5)
        await session.cancel()
        release.set()
        await asyncio.sleep(.05)
        assert any(isinstance(f, dict) and f['type'] == 'speech_stop' and f['generation_id'] == 1 for f in socket.frames)
        assert not any(isinstance(f, dict) and f['type'] == 'speak_chunk' for f in socket.frames)
    asyncio.run(run())


def test_fake_waveform():
    pcm = FakeSynthesizer().synthesize('test', 'en')
    samples = np.frombuffer(pcm, dtype='<i2').astype(float) / 32767
    assert len(samples) == 12000
    expected = .3 * np.sin(2 * np.pi * 440 * np.arange(12000) / 24000)
    assert np.max(np.abs(samples - expected)) < .0001


def test_startup_warm_failure_and_disabled(tmp_path, monkeypatch):
    import sensei_server.app as service
    import sensei_server.asr as asr
    monkeypatch.setenv('SENSEI_PROFILE_DIR', str(tmp_path))
    monkeypatch.setenv('SENSEI_VOICE_REF', 'reference.wav')
    monkeypatch.setattr(service, 'FAKE', False)
    monkeypatch.setattr(asr, 'Recognizer', lambda: None)
    calls = []
    def warm(ref):
        calls.append(ref)
        return Stub()
    monkeypatch.setattr(service, 'QwenSynthesizer', warm)
    with TestClient(app) as client, client.websocket_connect('/ws') as ws:
        ws.send_json({'v': 1, 'type': 'hello', 'learner_id': 'ready'})
        assert ws.receive_json()['voice'] == 'ready'
        assert calls == ['reference.wav']
    def fail(ref):
        raise RuntimeError('unavailable')
    monkeypatch.setattr(service, 'QwenSynthesizer', fail)
    with TestClient(app) as client, client.websocket_connect('/ws') as ws:
        ws.send_json({'v': 1, 'type': 'hello', 'learner_id': 'failed'})
        assert ws.receive_json()['voice'] == 'unavailable'
    monkeypatch.setenv('SENSEI_TTS', 'off')
    monkeypatch.setattr(service, 'QwenSynthesizer', lambda ref: (_ for _ in ()).throw(AssertionError()))
    with TestClient(app) as client, client.websocket_connect('/ws') as ws:
        ws.send_json({'v': 1, 'type': 'hello', 'learner_id': 'off'})
        assert ws.receive_json()['voice'] == 'unavailable'


def test_fake_mode_never_initializes_qwen(tmp_path, monkeypatch):
    import sensei_server.app as service
    monkeypatch.setenv('SENSEI_PROFILE_DIR', str(tmp_path))
    monkeypatch.delenv('SENSEI_TTS', raising=False)
    monkeypatch.setattr(service, 'FAKE', True)
    def fail(ref):
        raise AssertionError('real model must not load')
    monkeypatch.setattr(service, 'QwenSynthesizer', fail)
    with TestClient(app) as client, client.websocket_connect('/ws') as ws:
        ws.send_json({'v': 1, 'type': 'hello', 'learner_id': 'fake'})
        assert ws.receive_json()['voice'] == 'ready'
