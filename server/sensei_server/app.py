"""WebSocket transport and session lifecycle."""
from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager, suppress
import json
import logging
import os
from pathlib import Path
import uuid

from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles

from .profile import BANDS, DEFAULT_SUPPORT, PROFILE_ID, ProfileCorrupt, ProfileStore, new_profile, now
from .scene import Scene, SceneError
from .tasks import TaskBook
from .evidence import EvidenceBook
from .sanitize import clean
from .tts import FakeSynthesizer, QwenSynthesizer, sentences

FAKE = os.getenv("SENSEI_FAKE_LLM") == "1" and os.getenv("SENSEI_FAKE_ASR") == "1"
if FAKE and os.getenv("SENSEI_LLM_API_KEY"):
    raise RuntimeError("Fake mode cannot run with an LLM API key")

BAND_DEFAULTS = {
    "zero": (True, True, True, True, "english_explanations", "pre_n5"),
    "early_beginner": (True, True, True, True, "mixed", "n5"),
    "developing_beginner": (True, True, False, True, "mixed", "n4"),
    "intermediate": (True, True, False, False, "mixed", "n3"),
    "advanced": (True, False, False, False, "japanese_only", "n2"),
}

@asynccontextmanager
async def lifespan(app: FastAPI):
    app.state.tts = None
    if os.getenv("SENSEI_TTS", "").lower() == "off":
        pass
    elif FAKE:
        app.state.tts = FakeSynthesizer()
    else:
        try:
            app.state.tts = await asyncio.to_thread(QwenSynthesizer, os.environ["SENSEI_VOICE_REF"])
        except Exception as exc:
            # Do not log paths, audio, or exception text.
            logging.getLogger(__name__).warning("TTS initialization failed (%s)", type(exc).__name__)
    app.state.asr = None
    app.state.asr_status = "ready" if FAKE else "loading"
    app.state.next_transcript = "hello"
    app.state.sessions = set()
    async def load():
        try:
            from .asr import Recognizer
            app.state.asr = await asyncio.to_thread(Recognizer)
            app.state.asr_status = "ready"
        except Exception:
            app.state.asr_status = "failed"
        for session in list(app.state.sessions):
            with suppress(Exception):
                await session.send("status", asr=app.state.asr_status)
    job = None if FAKE else asyncio.create_task(load())
    yield
    if job:
        job.cancel()
        with suppress(asyncio.CancelledError):
            await job

app = FastAPI(lifespan=lifespan)

@app.get("/healthz")
async def healthz():
    return {"status": "ok"}

if FAKE:
    @app.post("/__fake__/transcript")
    async def fake_transcript(body: dict):
        if not isinstance(body.get("text"), str):
            from fastapi import HTTPException
            raise HTTPException(status_code=422)
        app.state.next_transcript = body["text"]
        return {"ok": True}


class Session:
    def __init__(self, ws: WebSocket, ident: str):
        self.ws = ws
        self.ident = ident
        self.store = ProfileStore(Path(os.getenv("SENSEI_PROFILE_DIR", str(Path.home() / ".local/share/sensei/profiles"))))
        self.profile = self.store.load(ident) or new_profile(ident)
        self.store.save(self.profile)
        self.scene = Scene()
        self.session_id = "s-" + uuid.uuid4().hex
        self.turn_id = 0
        self.generation = 0
        self.tasks = TaskBook(self.scene)
        self.evidence = EvidenceBook(self)
        self.last_line = None
        self.utterance_id = 0
        self.running = None
        self.send_lock = asyncio.Lock()
        self.audio = None
        self.history = []
        self.preference_changes = []
        self.profile["sessions"].append({"session_id": self.session_id, "started_at": now(),
                                         "preference_changes": self.preference_changes, "next_suggestion": None})
        self.save()

    @property
    def task(self):
        return self.tasks.view(self.tasks.active) if self.tasks.active else None

    async def task_changed(self, task):
        await self.send("task", task=task)

    async def evaluate_task(self, event):
        self.evidence.scene_event(event)
        changed = self.tasks.evaluate(event)
        if changed:
            await self.task_changed(changed)
        return changed

    def summary(self):
        evidence = [e for e in self.profile["evidence"] if not e.get("superseded")]
        current = [e for e in evidence if e.get("session_id") == self.session_id]
        targets = {}
        for item in current:
            target = item.get("target", {})
            key, dimension = target.get("key"), target.get("dimension")
            if not isinstance(key, str) or not isinstance(dimension, str):
                continue
            ident = (key, dimension)
            targets.setdefault(ident, []).append(item)
        practiced, unresolved = [], []
        for (key, dimension), items in targets.items():
            all_items = [e for e in evidence if e.get("target", {}).get("key") == key and e.get("target", {}).get("dimension") == dimension]
            independent = sum(e.get("outcome") == "independent_success" for e in items)
            unresolved_count = sum(e.get("outcome") == "unresolved" for e in items)
            supported = sum(e.get("outcome") in ("completed_by_demonstration", "success_after_model", "success_after_hint", "success_after_repetition") for e in items)
            independent_sessions = {e.get("session_id") for e in all_items if e.get("outcome") == "independent_success"}
            outcomes = {e.get("outcome") for e in all_items}
            label = ("stable" if len(independent_sessions) >= 2 else "used_independently" if independent_sessions else
                     "used_with_help" if outcomes & {"completed_by_demonstration", "success_after_model", "success_after_hint", "success_after_repetition"} else "practiced")
            practiced.append({"key": key, "dimension": dimension, "label": label, "independent": independent,
                              "supported": supported, "unresolved": unresolved_count})
            if items[-1].get("outcome") == "unresolved":
                unresolved.append(key)
        return {"session_id": self.session_id, "practiced": practiced, "unresolved_keys": unresolved,
                "next_suggestion": None, "preference_changes": self.preference_changes}

    async def send(self, kind: str, **data):
        async with self.send_lock:
            await self.ws.send_json({"type": kind, "v": 1, **data})

    async def snapshot(self):
        await self.send("scene", snapshot=self.scene.snapshot(self.profile["support"], self.task))

    async def line(self, generation, segments):
        if generation != self.generation:
            return
        captions = {k: self.profile["support"][k] for k in ("show_japanese", "show_reading", "show_romaji", "show_translation")}
        self.last_line = segments
        self.utterance_id += 1
        utterance = f"u{self.utterance_id}"
        await self.send("sensei_state", state="speaking")
        await self.send("line", generation_id=generation, segments=segments, captions=captions)
        tts = getattr(app.state, "tts", None)
        if tts is None:
            return
        chunks = sentences(segments)
        for chunk_index, (segment_index, text, segment) in enumerate(chunks):
            try:
                pcm = await asyncio.to_thread(tts.synthesize, text, segment["lang"])
            except Exception:
                # A synthesis failure leaves the caption usable.
                return
            async def send_chunk():
                async with self.send_lock:
                    if generation != self.generation:
                        return
                    header = {"type": "speak_chunk", "v": 1, "utterance_id": utterance,
                              "generation_id": generation, "segment_index": segment_index,
                              "chunk_index": chunk_index, "lang": segment["lang"], "text": text,
                              "reading": segment.get("reading"), "romaji": segment.get("romaji"),
                              "translation": segment.get("translation"), "format": "pcm_s16le",
                              "sample_rate": 24000, "channels": 1, "final": chunk_index == len(chunks) - 1}
                    await self.ws.send_json(header)
                    await self.ws.send_bytes(pcm)
            # Header and binary are one protocol transfer, even if interrupted.
            transfer = asyncio.create_task(send_chunk())
            try:
                await asyncio.shield(transfer)
            except asyncio.CancelledError:
                await transfer
                raise

    async def cancel(self):
        running, self.running = self.running, None
        if running and not running.done():
            running.cancel()
        if self.generation:
            await self.send("speech_stop", generation_id=self.generation)
        if running:
            with suppress(asyncio.CancelledError):
                await running

    async def start(self, text: str, event=False):
        await self.cancel()
        self.turn_id += 1
        self.generation += 1
        turn, generation = self.turn_id, self.generation
        if not event:
            self.evidence.utterance(text)
        await self.send("turn_started", turn_id=turn, generation_id=generation)
        await self.send("sensei_state", state="thinking")
        self.running = asyncio.create_task(self.run_turn(turn, generation, text, event))
        await asyncio.sleep(0)

    async def run_turn(self, turn, generation, text, event):
        status, code = "completed", None
        try:
            if FAKE:
                if event and text == "help_hint":
                    await self.send("hint", card={"kind": "partial_phrase", "content": {"english": "Try asking for an object."}})
                elif event and text == "session_start":
                    await self.line(generation, [{"lang": "ja", "text": "こんにちは！"}, {"lang": "en", "text": "Hello, what Japanese do you know?"}])
                elif text.startswith("fail "):
                    status, code = "failed", text[5:]
                elif text == "spawn apple":
                    self.scene.spawn([{"catalog_id": "apple", "region": "center"}], turn)
                    await self.snapshot()
                    await self.line(generation, [{"lang": "ja", "text": "りんごです。"}])
                elif text == "long speech":
                    for i in range(6):
                        await self.line(generation, [{"lang": "ja", "text": f"こんにちは {i+1}"}])
                        if i < 5:
                            await asyncio.sleep(.25)
                else:
                    await self.line(generation, [{"lang": "en", "text": "Let's practice together."}])
            elif not os.getenv("SENSEI_LLM_API_KEY"):
                status, code = "failed", "llm_not_configured"
            else:
                from .agent import run_agent, error_code
                try:
                    await run_agent(self, generation, text, event)
                except Exception as exc:
                    code = error_code(exc)
                    status = "budget_exhausted" if code == "budget_exhausted" else "failed"
                    if status == "budget_exhausted":
                        code = None
        except asyncio.CancelledError:
            status, code = "cancelled", None
        except Exception:
            status, code = "failed", "provider_error"
        finally:
            await self.send("turn_ended", result={"turn_id": turn, "generation_id": generation, "status": status, "error_code": code})
            await self.send("sensei_state", state="idle")

    async def repeat(self):
        await self.cancel()
        if self.last_line is None:
            await self.send("notice", code="nothing_to_repeat")
        else:
            self.evidence.assistance("repetition")
            self.running = asyncio.create_task(self.line(self.generation, self.last_line))

    def save(self):
        self.profile["updated_at"] = now()
        self.store.save(self.profile)

    async def preference(self, patch, player=True):
        if not isinstance(patch, dict) or not patch or set(patch) - set(DEFAULT_SUPPORT):
            return False
        for key, value in patch.items():
            if key.startswith("show_") and type(value) is not bool:
                return False
            if key == "pace" and value not in ("normal", "slow") or key == "correction_style" and value not in ("light", "detailed") or key == "language_mode" and value not in ("english_explanations", "mixed", "japanese_only"):
                return False
            if not player and key in self.profile["pinned"]:
                return False
        for key, value in patch.items():
            previous = self.profile["support"][key]
            if previous != value:
                self.preference_changes.append({"field": key, "from": previous, "to": value, "by": "player" if player else "sensei"})
            self.profile["support"][key] = value
            if player and key not in self.profile["pinned"]:
                self.profile["pinned"].append(key)
        self.save()
        await self.send("support", support=self.profile["support"])
        await self.snapshot()
        return True

    async def set_band(self, band):
        if not isinstance(band, str) or band not in BANDS:
            return
        values = BAND_DEFAULTS[band]
        keys = ("show_japanese", "show_reading", "show_romaji", "show_translation", "language_mode")
        for key, value in zip(keys, values):
            if key not in self.profile["pinned"]:
                self.profile["support"][key] = value
        old = self.profile["working_level"]
        self.profile["starting_band"] = band
        self.profile["working_level"] = values[-1]
        if old != values[-1]:
            self.profile["level_history"].append({"from": old, "to": values[-1], "reason": "band", "at": now(), "attempt_count": len(self.profile["evidence"])})
        self.save()
        await self.send("support", support=self.profile["support"])
        await self.snapshot()

    async def action(self, msg):
        try:
            ident = msg.get("instance_id")
            if not isinstance(ident, str):
                raise SceneError("invalid_arguments")
            if msg["type"] == "player_select":
                event = self.scene.select(ident, self.turn_id)
            else:
                region, container = msg.get("region"), msg.get("container_id")
                if container is not None and not isinstance(container, str):
                    raise SceneError("invalid_arguments")
                event = self.scene.move(ident, region, container, "player", self.turn_id)
            await self.evaluate_task(event)
            await self.snapshot()
            await self.send("action_result", ok=True, error_code=None, revision=self.scene.revision)
        except SceneError as exc:
            await self.send("action_result", ok=False, error_code=exc.code, revision=self.scene.revision)

    async def message(self, msg):
        kind = msg["type"]
        if kind == "talk_start":
            await self.cancel()
            self.audio = None
        elif kind == "audio_begin":
            self.audio = bytearray() if (msg.get("kind"), msg.get("format"), msg.get("sample_rate"), msg.get("channels")) == ("utterance", "pcm_s16le", 16000, 1) else None
        elif kind == "talk_end":
            audio, self.audio = self.audio, None
            hold = msg.get("hold_ms")
            if type(hold) is not int or hold < 300:
                await self.send("notice", code="didnt_catch")
            elif audio is None or not audio:
                await self.send("notice", code="couldnt_hear")
            else:
                try:
                    if FAKE:
                        text = app.state.next_transcript
                        app.state.next_transcript = "hello"
                    elif app.state.asr is None:
                        raise RuntimeError("ASR unavailable")
                    else:
                        text = await asyncio.to_thread(app.state.asr.transcribe, bytes(audio))
                    if not text.strip():
                        await self.send("notice", code="didnt_catch")
                    else:
                        await self.send("transcript", text=clean(text), lang="und", final=True)
                        await self.start(text)
                except Exception:
                    await self.send("notice", code="couldnt_hear")
        elif kind in ("player_select", "player_move"):
            await self.action(msg)
        elif kind == "player_event":
            event = msg.get("kind")
            if event == "help_repeat":
                await self.repeat()
            elif event in ("session_start", "help_hint", "help_english", "area_placed"):
                await self.start(event, True)
        elif kind == "repeat":
            await self.repeat()
        elif kind == "cancel":
            await self.cancel()
        elif kind == "set_preference":
            await self.preference(msg.get("patch"))
        elif kind == "set_band":
            await self.set_band(msg.get("band"))
        elif kind == "session_summary":
            await self.send("summary", summary=self.summary())
        elif kind == "reset_profile":
            await self.cancel()
            self.store.delete(self.ident)
            self.profile = new_profile(self.ident)
            self.preference_changes = []
            self.profile["sessions"].append({"session_id": self.session_id, "started_at": now(),
                                             "preference_changes": self.preference_changes, "next_suggestion": None})
            self.save()
            if self.scene.instances:
                ids = [obj["id"] for obj in self.scene.instances]
                self.scene.remove(ids, self.turn_id)
            self.scene.selected_id = None
            self.tasks = TaskBook(self.scene)
            self.evidence = EvidenceBook(self)
            self.last_line = None
            self.history.clear()
            await self.send("support", support=self.profile["support"])
            await self.snapshot()

@app.websocket("/ws")
async def websocket(ws: WebSocket):
    await ws.accept()
    session = None
    try:
        while True:
            frame = await ws.receive()
            if frame["type"] == "websocket.disconnect":
                break
            if frame.get("bytes") is not None:
                if session and session.audio is not None:
                    session.audio.extend(frame["bytes"])
                    if len(session.audio) > 8 * 1024 * 1024:
                        await session.send("error", code="message_too_large", detail="Audio upload too large")
                        await ws.close(code=1009)
                        break
                continue
            raw = frame.get("text")
            if raw is None:
                continue
            if len(raw.encode("utf-8")) > 65536:
                await ws.send_json({"type": "error", "v": 1, "code": "message_too_large", "detail": "Message too large"})
                await ws.close(code=1009)
                break
            try:
                msg = json.loads(raw)
            except json.JSONDecodeError:
                continue
            if not isinstance(msg, dict):
                continue
            if type(msg.get("v")) is not int or msg["v"] != 1:
                await ws.send_json({"type": "error", "v": 1, "code": "protocol_version", "detail": "Unsupported protocol version"})
                await ws.close()
                break
            if not isinstance(msg.get("type"), str):
                continue
            if session is None:
                if msg["type"] != "hello":
                    continue
                ident = msg.get("learner_id")
                if not isinstance(ident, str) or not PROFILE_ID.fullmatch(ident):
                    await ws.send_json({"type": "error", "v": 1, "code": "invalid_learner_id", "detail": "Invalid learner ID"})
                    await ws.close()
                    break
                try:
                    session = Session(ws, ident)
                except (ProfileCorrupt, OSError):
                    await ws.send_json({"type": "error", "v": 1, "code": "profile_error", "detail": "Profile unavailable"})
                    await ws.close()
                    break
                app.state.sessions.add(session)
                await session.send("welcome", session_id=session.session_id, scene=session.scene.snapshot(session.profile["support"], None), support=session.profile["support"], task=None, llm="ready" if FAKE or os.getenv("SENSEI_LLM_API_KEY") else "no_key", asr=app.state.asr_status, voice="ready" if app.state.tts is not None else "unavailable")
            else:
                await session.message(msg)
    except WebSocketDisconnect:
        pass
    finally:
        if session:
            app.state.sessions.discard(session)
            with suppress(Exception):
                await session.cancel()

client_dir = os.getenv("SENSEI_CLIENT_DIR")
if client_dir and Path(client_dir).is_dir():
    app.mount("/", StaticFiles(directory=client_dir, html=True), name="client")
