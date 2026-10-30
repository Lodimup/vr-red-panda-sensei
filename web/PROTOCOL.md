# Red Panda Sensei: web protocol (web/PROTOCOL.md)

Status: wave-1 contract, 2026-10-31. This file is the wire contract between
`sensei-server` (Python, FastAPI) and the WebXR client. The behaviour the
protocol carries is specified in `web/SPEC.md`; the game-master semantics
(tool rules, budgets, evidence, learner model) are the engine stories in
`sensei-engine/SPEC.md`, which the Python server ports faithfully. Where
this file and the engine spec disagree about engine semantics, the engine
spec wins; where they disagree about the wire, this file wins.

## 0 Transport

- One WebSocket per session: `GET /ws`. The server also serves the static
  client at `/` and a health endpoint at `/healthz`.
- Text frames are JSON messages (one object per frame). Audio travels as
  binary frames, each immediately preceded by a JSON header frame that
  describes it (see §4). No other binary framing exists.
- Every JSON message has `type` (string) and `v: 1`. Unknown message types
  are ignored by both sides; a message whose `v` is not understood is a
  fatal protocol error (`error` with code `protocol_version`, then close).
- Message size limits: JSON frames ≤ 64 KiB; one audio upload ≤ 8 MiB
  total; one TTS chunk ≤ 2 MiB. Violations close the socket with code
  1009 after an `error` frame when the connection state allows one.
- Ordering: server messages for one session are sent in causal order.
  Client messages are processed in arrival order; player actions never
  wait behind a running turn.

## 1 Session lifecycle

1. Client opens the socket and sends `hello`:
   `{ "type": "hello", "v": 1, "learner_id": "<id>", "client": "webxr" }`
   `learner_id` is an opaque id the client persists in browser storage
   (localStorage). It must match `^[a-z0-9_-]{1,40}$`; anything else is
   rejected with `error` code `invalid_learner_id` and close. The server
   keys the learner profile on disk by this id. A first-time id creates a
   fresh profile (engine S7 semantics: version 1, no band, zero-band
   support defaults).
2. Server replies `welcome`:
   `{ "type": "welcome", "session_id": "s-...", "scene": <SceneSnapshot>,
      "support": <SupportSettings>, "task": <TaskView|null>,
      "llm": "ready" | "no_key" }`
   `llm: "no_key"` mirrors engine behaviour without a key: the scene works,
   talk turns fail with `llm_not_configured`, and the client shows the
   no-key status (web SPEC W2).
3. The client then places the practice area and sends the player event
   `session_start` (§3). The greeting turn runs like any other turn.
4. Either side may close. On reconnect the client sends `hello` again; the
   server starts a new engine session against the same stored profile
   (engine S59 restore semantics). The scene is NOT restored: it starts
   empty at revision 0 and the client re-runs `area_placed` /
   `session_start` as its boot flow dictates.

## 2 Shared shapes

Field names are snake_case, exactly as in the engine spec.

- `SceneSnapshot`: `{ revision, selected_id, instances: [ { id,
  catalog_id, region, slot, container_id, japanese, reading, english } ],
  active_task: <TaskView|null>, support: <SupportSettings> }`
  (engine S20; no `variant` field exists, per engine S119).
- `SupportSettings`: `{ show_japanese, show_reading, show_romaji,
  show_translation, pace: "normal"|"slow", correction_style:
  "light"|"detailed", language_mode:
  "english_explanations"|"mixed"|"japanese_only" }` (engine §9).
- `TaskView`: as engine S35c (`task_id`, `status`, `family`,
  `skill_targets`, `instruction`, `predicates`, `scenario_properties`,
  `role`, `is_fresh_variation`, ...). The client renders only
  `instruction`, `status`, `family` and `skill_targets` labels; the rest
  is carried for completeness and debugging.
- `SpeechSegment`: `{ lang: "ja"|"en", text, reading?, romaji?,
  translation? }` (engine §8).
- `HintCard`: `{ kind, content: { japanese?, reading?, romaji?,
  english? }, target_key? }` (engine S38).
- `TurnResult` (subset the client consumes): `{ turn_id, generation_id,
  status: "completed"|"cancelled"|"failed"|"budget_exhausted",
  error_code: string|null }` (engine S67). Error codes keep the engine's
  exact strings (`auth_failed`, `quota_exceeded`, `network_error`,
  `provider_error`, `llm_not_configured`).

## 3 Client → server messages

- `talk_start` `{ }` — the learner pressed push-to-talk. If a turn or
  speech is in flight, the server cancels the current generation first
  (interrupt semantics, engine S27/S66), then begins capture arming.
  The client starts recording immediately on the same press; `talk_start`
  is a signal, not a gate.
- `audio` header + binary frames (§4) — one utterance: 16 kHz mono
  signed-16-bit PCM captured while talk was held, sent on release.
  Followed by `talk_end` `{ "hold_ms": <int> }`. Holds shorter than
  300 ms, or audio that transcribes to empty text, are discarded
  server-side with a `notice` (`didnt_catch`) and no turn runs
  (mirrors unity U23 / engine TranscriptGate).
- `player_select` `{ "instance_id": "<id>" }` → server replies
  `action_result` (below). Mirrors engine `PlayerSelect` (S18).
- `player_move` `{ "instance_id": "<id>", "region": <region|null>,
  "container_id": <id|null> }` — exactly one of `region` /
  `container_id` is non-null. Mirrors engine `PlayerMove` (S19).
- `player_event` `{ "kind": "<kind>" }` — kind ∈ `session_start`,
  `help_hint`, `help_repeat`, `help_english`, `area_placed` (engine
  S135). Runs a player-event turn. `help_repeat` is a UI convenience:
  the server may answer it with the direct replay path (engine S142)
  instead of a model turn; the wire result is identical.
- `repeat` `{ }` — replay the latest presented speech directly, no
  model turn (engine S142). If there is nothing to replay, the server
  sends `notice` (`nothing_to_repeat`).
- `set_preference` `{ "patch": { ...SupportSettings fields... } }` —
  player-set support fields; the server applies them as pinned player
  preferences (engine S40).
- `set_band` `{ "band": "<band>" }` — starting band buttons (engine
  S42). Bands: `zero`, `early_beginner`, `developing_beginner`,
  `intermediate`, `advanced`.
- `session_summary` `{ }` — server replies `summary` (engine S55 shape;
  never contains the hidden working level).
- `reset_profile` `{ }` — two-step confirm happens client-side; this
  message is the confirmed request (engine S60/S87).
- `cancel` `{ }` — cancel the current generation (same as the
  server-side effect of `talk_start` during speech, without arming
  capture).

## 4 Audio frames

An audio transfer (either direction) is: one JSON header frame, then
one or more binary frames, concatenated in order.

- Upload header: `{ "type": "audio_begin", "kind": "utterance",
  "format": "pcm_s16le", "sample_rate": 16000, "channels": 1 }`,
  then the PCM bytes, then the `talk_end` message of §3 (which is the
  end marker; there is no `audio_end` for uploads).
- Download header (one per spoken sentence chunk):
  `{ "type": "speak_chunk", "utterance_id": "u<n>", "generation_id": <n>,
    "segment_index": <i>, "chunk_index": <k>, "lang": "ja"|"en",
    "text": "<sentence text>", "reading": ..., "romaji": ...,
    "translation": ..., "captions": { "show_japanese": bool,
    "show_reading": bool, "show_romaji": bool, "show_translation": bool },
    "format": "pcm_s16le", "sample_rate": 24000, "channels": 1,
    "final": bool }`
  followed by exactly one binary frame with that chunk's PCM.
  `final: true` marks the last chunk of the utterance.
  Sentence chunking, streaming and caching follow web SPEC W28–W30
  (engine S135–S142 behaviour: per-sentence synthesis, next sentence
  synthesized while the current one plays, cache keyed by text + voice +
  pace). The `captions` booleans are the effective caption visibility
  (engine S91) so the client never re-derives them.
- Generation discipline: the client plays chunks only for the current
  generation. A chunk whose `generation_id` is older than the latest
  `turn_started` / `generation` notice is dropped silently. When the
  server cancels a generation it sends `speech_stop`
  `{ "type": "speech_stop", "generation_id": <n> }`; the client must
  reach silence within 300 ms (unity U23 / A22 semantics).

## 5 Server → client messages (non-audio)

- `welcome` (§1).
- `transcript` `{ "text": "<asr result>", "final": true }` — what the
  learner is about to be understood as saying; the client shows it on
  the "You said" line. Sent before the turn runs. An ASR failure sends
  `notice` (`couldnt_hear`) instead and no turn runs (unity U31).
- `turn_started` `{ "turn_id": <n>, "generation_id": <n> }`.
- `turn_ended` `{ "result": <TurnResult> }` — includes `failed` results
  with their `error_code`; the client renders the one-line failure
  message from the code (web SPEC W32, texts as unity U32).
- `scene` `{ "snapshot": <SceneSnapshot> }` — full snapshot after every
  committed change (spawn/move/remove/select/task evaluation); the
  client renders from snapshots only, never from deltas.
- `highlight` `{ "instance_ids": [...], "duration_ms": <int> }`
  (engine OnHighlight).
- `animate` `{ "animation": "<pose>", "target_id": <id|null> }`
  (engine OnAnimate; pose names as unity U12).
- `hint` `{ "card": <HintCard> }` (engine OnHint).
- `task` `{ "task": <TaskView> }` (engine OnTaskChanged).
- `support` `{ "support": <SupportSettings> }` (engine OnSupportChanged).
- `summary` `{ "summary": <SessionSummary> }` (engine OnSummary / reply
  to `session_summary`).
- `sensei_state` `{ "state": "idle"|"thinking"|"speaking" }`
  (engine OnSenseiState, S69).
- `action_result` `{ "ok": bool, "error_code": string|null,
  "revision": <int> }` — reply to `player_select` / `player_move`
  (engine PlayerActionResult, S18/S19). On `ok: false` for a move the
  client snaps the card back and buzzes (unity U22).
- `speech_stop` (§4).
- `notice` `{ "code": "didnt_catch"|"couldnt_hear"|"nothing_to_repeat" }`
  — transient UI notices; the client shows the matching line briefly.
- `error` `{ "code": "<string>", "detail": "<short text>" }` —
  protocol-level errors only (engine turn failures travel in
  `turn_ended`). `detail` never contains a key, a transcript, or any
  secret (engine S77 discipline applies to everything the server emits).

## 6 Conformance

A server or client claims conformance when the hidden acceptance suite
(outside this repo) passes against it: session lifecycle and profile
restore, one full greeting turn with streamed non-silent audio, player
select/move round trips with snapshot verification, interrupt during
speech reaching silence inside 300 ms, replay, player-event turns, and
the failure-code mapping. Desktop, controller and hand input paths are
all exercised against this same protocol; there is no second protocol
for tests.
