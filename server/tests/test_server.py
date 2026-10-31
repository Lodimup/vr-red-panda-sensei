import os
os.environ["SENSEI_FAKE_LLM"] = "1"
os.environ["SENSEI_FAKE_ASR"] = "1"
os.environ.pop("SENSEI_LLM_API_KEY", None)

from fastapi.testclient import TestClient
from sensei_server.app import app


def receive_until(ws, kind):
    seen = []
    while True:
        frame = ws.receive()
        if frame.get("bytes") is not None:
            continue
        import json
        item = json.loads(frame["text"])
        seen.append(item)
        if item["type"] == kind:
            return seen


def send(ws, message_type, **fields):
    ws.send_json({"v": 1, "type": message_type, **fields})


def talk(client, ws, text, hold=400):
    client.post("/__fake__/transcript", json={"text": text})
    send(ws, "talk_start")
    send(ws, "audio_begin", kind="utterance", format="pcm_s16le", sample_rate=16000, channels=1)
    ws.send_bytes(b"\0\0")
    send(ws, "talk_end", hold_ms=hold)


def test_protocol_roundtrip(tmp_path, monkeypatch):
    monkeypatch.setenv("SENSEI_PROFILE_DIR", str(tmp_path))
    with TestClient(app) as client:
        assert client.get("/healthz").status_code == 200
        with client.websocket_connect("/ws") as ws:
            send(ws, "hello", learner_id="test", client="webxr")
            welcome = ws.receive_json()
            assert welcome["llm"] == "ready" and welcome["scene"]["revision"] == 0
            send(ws, "repeat")
            assert ws.receive_json()["code"] == "nothing_to_repeat"
            send(ws, "player_event", kind="session_start")
            greeting = receive_until(ws, "turn_ended")
            assert [s["lang"] for m in greeting if m["type"] == "line" for s in m["segments"]] == ["ja", "en"]
            assert greeting[-1]["result"]["status"] == "completed"
            receive_until(ws, "sensei_state")
            talk(client, ws, "spawn apple")
            spawn = receive_until(ws, "turn_ended")
            transcript = next(m for m in spawn if m["type"] == "transcript")
            assert transcript == {"type": "transcript", "v": 1, "text": "spawn apple", "lang": "und", "final": True}
            assert spawn.index(transcript) < next(i for i, m in enumerate(spawn) if m["type"] == "turn_started")
            assert next(m for m in spawn if m["type"] == "scene")["snapshot"]["instances"][0]["id"] == "apple_1"
            receive_until(ws, "sensei_state")
            send(ws, "player_move", instance_id="apple_1", region="left", container_id="apple_1")
            result = receive_until(ws, "action_result")[-1]
            assert result["error_code"] == "invalid_arguments" and result["revision"] == 1
            send(ws, "player_move", instance_id="apple_1", region="left", container_id=None)
            move = receive_until(ws, "action_result")
            assert move[-1]["ok"]
            assert next(m for m in move if m["type"] == "scene")["snapshot"]["instances"][0]["region"] == "left"
            send(ws, "set_preference", patch={"show_romaji": False})
            assert receive_until(ws, "support")[-1]["support"]["show_romaji"] is False
            receive_until(ws, "scene")
            talk(client, ws, "long speech")
            first = receive_until(ws, "line")[-1]
            send(ws, "cancel")
            cancelled = receive_until(ws, "turn_ended")
            assert cancelled[-1]["result"]["status"] == "cancelled"
            assert not any(m["type"] == "line" for m in cancelled)
            assert first["generation_id"] == cancelled[-1]["result"]["generation_id"]
            receive_until(ws, "sensei_state")
            talk(client, ws, "fail auth_failed")
            assert receive_until(ws, "turn_ended")[-1]["result"]["error_code"] == "auth_failed"
            receive_until(ws, "sensei_state")
            talk(client, ws, "", hold=400)
            assert receive_until(ws, "notice")[-1]["code"] == "didnt_catch"
            talk(client, ws, "hello", hold=299)
            assert receive_until(ws, "notice")[-1]["code"] == "didnt_catch"
        with client.websocket_connect("/ws") as ws:
            send(ws, "hello", learner_id="test", client="webxr")
            restored = ws.receive_json()
            assert restored["support"]["show_romaji"] is False
            assert restored["scene"]["instances"] == []
            send(ws, "reset_profile")
            receive_until(ws, "scene")
        with client.websocket_connect("/ws") as ws:
            send(ws, "hello", learner_id="../bad", client="webxr")
            assert ws.receive_json()["code"] == "invalid_learner_id"
        with client.websocket_connect("/ws") as ws:
            ws.send_json({"type": "hello", "v": 2})
            assert ws.receive_json()["code"] == "protocol_version"


def test_selection_and_move_snapshots_precede_action_results(tmp_path, monkeypatch):
    monkeypatch.setenv("SENSEI_PROFILE_DIR", str(tmp_path))
    with TestClient(app) as client, client.websocket_connect("/ws") as ws:
        send(ws, "hello", learner_id="move", client="webxr")
        ws.receive_json()
        talk(client, ws, "spawn apple")
        receive_until(ws, "turn_ended")
        receive_until(ws, "sensei_state")

        send(ws, "player_select", instance_id="apple_1")
        selected = receive_until(ws, "action_result")
        assert [m["type"] for m in selected] == ["scene", "action_result"]
        assert selected[0]["snapshot"]["selected_id"] == "apple_1"

        send(ws, "player_move", instance_id="apple_1", region="left", container_id=None)
        moved = receive_until(ws, "action_result")
        assert [m["type"] for m in moved] == ["scene", "action_result"]
        assert moved[0]["snapshot"]["instances"][0]["region"] == "left"
        assert moved[0]["snapshot"]["revision"] == moved[1]["revision"] == 2
        assert moved[1]["ok"] is True


def test_real_agent_tools_against_model_protocol(tmp_path, monkeypatch):
    import asyncio
    from pydantic_ai.messages import ModelResponse, TextPart, ToolCallPart
    from pydantic_ai.models.function import FunctionModel
    import sensei_server.agent as adapter
    from sensei_server.app import Session

    calls = []
    def model(messages, info):
        calls.append({t.name for t in info.function_tools})
        if len(calls) == 1:
            return ModelResponse(parts=[
                ToolCallPart("speak", {"segments": [{"lang": "ja", "text": "こんにちは"}]}),
                ToolCallPart("spawn_objects", {"objects": [{"catalog_id": "apple"}]}),
                ToolCallPart("set_support", {"reason": "learner_request", "pace": "slow"}),
            ])
        if len(calls) == 2:
            return ModelResponse(parts=[
                ToolCallPart("move_object", {"object_id": "apple_1", "destination": {"region": "left"}, "purpose": "arrange"}),
                ToolCallPart("show_hint", {"kind": "explanation", "content": {"english": "Apple"}}),
                ToolCallPart("remove_object", {"instance_id": "apple_1"}),
            ])
        return ModelResponse(parts=[TextPart("")])

    class Socket:
        def __init__(self):
            self.messages = []
        async def send_json(self, msg):
            self.messages.append(msg)
        async def send_bytes(self, data):
            pass

    monkeypatch.setenv("SENSEI_PROFILE_DIR", str(tmp_path))
    monkeypatch.setenv("SENSEI_LLM_API_KEY", "test-key")
    monkeypatch.setattr(adapter, "OpenAIChatModel", lambda *args, **kwargs: FunctionModel(model))
    socket = Socket()
    session = Session(socket, "modeltest")
    session.generation = session.turn_id = 1
    asyncio.run(adapter.run_agent(session, 1, "session_start", True))
    assert {"speak", "spawn_objects", "move_object", "remove_object", "show_hint", "set_support"} <= calls[0]
    assert any(m["type"] == "line" for m in socket.messages)
    assert session.scene.find("apple_1") is None
    assert [event["kind"] for event in session.scene.events] == ["spawned", "moved", "removed"]
    assert any(m["type"] == "hint" for m in socket.messages)
    assert session.profile["support"]["pace"] == "slow"


def test_speech_tool_schema_and_recovery_from_wrong_segment_shape(tmp_path, monkeypatch):
    import asyncio
    from pydantic_ai.messages import ModelResponse, TextPart, ToolCallPart
    from pydantic_ai.models.function import FunctionModel
    import sensei_server.agent as adapter
    from sensei_server.app import Session

    requests = []

    def model(messages, info):
        requests.append(messages)
        schema = next(tool.parameters_json_schema for tool in info.function_tools if tool.name == "speak")
        segment = schema["$defs"]["SpeechSegment"]
        assert set(segment["required"]) == {"lang", "text"}
        assert segment["properties"]["lang"]["enum"] == ["ja", "en"]
        if len(requests) == 1:
            return ModelResponse(parts=[ToolCallPart("speak", {"segments": [{"ja": "こんにちは"}]})])
        if len(requests) == 2:
            return ModelResponse(parts=[ToolCallPart("speak", {"segments": [
                {"lang": "ja", "text": "こんにちは"}, {"lang": "en", "text": "Hello!"},
            ]})])
        return ModelResponse(parts=[TextPart("")])

    class Socket:
        def __init__(self):
            self.messages = []

        async def send_json(self, msg):
            self.messages.append(msg)
        async def send_bytes(self, data):
            pass

    monkeypatch.setenv("SENSEI_PROFILE_DIR", str(tmp_path))
    monkeypatch.setenv("SENSEI_LLM_API_KEY", "test-key")
    monkeypatch.setattr(adapter, "OpenAIChatModel", lambda *args, **kwargs: FunctionModel(model))
    socket = Socket()
    session = Session(socket, "speechschema")
    session.generation = session.turn_id = 1
    asyncio.run(adapter.run_agent(session, 1, "session_start", True))

    lines = [message for message in socket.messages if message["type"] == "line"]
    assert len(requests) == 3
    assert len(lines) == 1
    assert [segment["lang"] for segment in lines[0]["segments"]] == ["ja", "en"]


def test_mode_guards():
    import subprocess
    import sys
    from pathlib import Path
    env = os.environ.copy()
    env.update(SENSEI_FAKE_LLM="1", SENSEI_FAKE_ASR="1", SENSEI_LLM_API_KEY="test-key")
    run = subprocess.run([sys.executable, "-c", "import sensei_server.app"], cwd=Path(__file__).parents[1], env=env, capture_output=True)
    assert run.returncode != 0
    env.pop("SENSEI_LLM_API_KEY")
    env["SENSEI_FAKE_ASR"] = "0"
    run = subprocess.run([sys.executable, "-c", "from sensei_server.app import app; assert not any(r.path == '/__fake__/transcript' for r in app.routes)"], cwd=Path(__file__).parents[1], env=env, capture_output=True)
    assert run.returncode == 0


def test_asr_initialization_failure_keeps_no_key_server_usable(tmp_path, monkeypatch):
    import sensei_server.app as service
    import sensei_server.asr as asr
    monkeypatch.setattr(service, "FAKE", False)
    monkeypatch.delenv("SENSEI_LLM_API_KEY", raising=False)
    monkeypatch.setenv("SENSEI_PROFILE_DIR", str(tmp_path))
    def fail():
        raise RuntimeError("model unavailable")
    monkeypatch.setattr(asr, "Recognizer", fail)
    with TestClient(app) as client:
        with client.websocket_connect("/ws") as ws:
            send(ws, "hello", learner_id="no_key", client="webxr")
            welcome = ws.receive_json()
            assert welcome["llm"] == "no_key"
            if welcome["asr"] != "failed":
                assert receive_until(ws, "status")[-1]["asr"] == "failed"
            send(ws, "player_event", kind="session_start")
            result = receive_until(ws, "turn_ended")[-1]["result"]
            assert result["status"] == "failed" and result["error_code"] == "llm_not_configured"
