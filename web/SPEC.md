# Red Panda Sensei: web build spec (web/SPEC.md)

Status: wave-1 contract, 2026-10-31. The WebXR rewrite of the Unity app.
Authority order: `web/PROTOCOL.md` for the wire, this file for web
behaviour, `sensei-engine/SPEC.md` for game-master semantics (its S-story
rules bind the Python port unchanged), `unity/SPEC.md` as the behaviour
reference this port mirrors (its U-stories are cited as "mirrors U<n>").
Handler rulings recorded in the source specs keep their force here.

## 0 Shape

- Server: `server/` — Python 3.12, FastAPI, one process. It serves the
  static client, owns one engine instance per WebSocket session, runs
  ASR and TTS in-process, and stores learner profiles on disk.
  RULING(Handler 2026-10-30): the backend is Python — FastAPI +
  PydanticAI — so the local Kitten TTS lives in the same server; the
  C# engine is not referenced, its behaviour and prompts are ported.
- Client: `client/` — Vite + TypeScript + three.js, WebXR
  `immersive-ar` on Quest Browser, and a full desktop mode (mouse +
  keyboard) with identical behaviour. Text rendering: troika-three-text
  with Noto Sans JP.
- The server owns scene, tasks, evidence and the learner model (engine
  Decision 2). The client renders server snapshots and sends player
  actions; it never mutates scene state on its own (mirrors unity §0).
- No key, transcript, or raw audio is ever logged by server or client;
  the API key exists only in the server process environment (mirrors
  U3, engine S77).

## 1 Boot and session

- **W1** Boot order, each step reflected in a status line on the
  client's status panel: (1) connect WebSocket, send `hello` with the
  persisted learner id; (2) on `welcome`, build the practice area (§2);
  (3) enter desktop mode immediately, and offer "Enter AR" when an
  immersive-ar session is available; (4) send player event
  `session_start` (mirrors U1/U40). Audio output unlocks on the first
  user gesture (browser policy); the greeting turn waits for that
  gesture if none has happened yet.
- **W2** `welcome.llm == "no_key"`: the app still boots, scene and
  panels work, the status panel shows the no-key line and talk is
  disabled with that message. ASR/TTS failures at load show which
  component failed; boot never dead-ends silently (mirrors U2).
- **W3** Reconnect: a dropped socket is retried with backoff; a
  successful reconnect re-runs W1 against the same learner id (profile
  restored server-side; scene restarts empty, engine S59/S8).

## 2 Practice area layout (pure, client-side)

The anchor is a floor pose. In AR it is placed 0.9 m in front of the
head along head yaw when the session starts, and re-placed on the
menu's re-place action or a 1 s long-press of the menu button (mirrors
unity §2). On desktop the anchor is fixed in a small room scene. All
positions are local to the anchor (x right, y up, z away from player).

- **W4** Mat: flat tray 1.4 m × 0.5 m at y = 0.80, centre z = 0.
- **W5** Region centres: `left` x = −0.45, `center` x = 0, `right`
  x = +0.45, z = 0, y = 0.80. `sensei_side` x = −0.95, z = 0.15,
  y = 0.80. `learner_hand` attaches to the right hand/controller
  (offset 0, 0.02, 0.08 in that space).
- **W6** Within a region, loose objects order by `slot` ascending; the
  first 4 form row 1 (z = 0), the rest row 2 (z = +0.14); each row is
  centred on the region centre independently: item i of n sits at
  x = centre + (i − (n − 1)/2) · 0.16 (mirrors U6).
- **W7** Objects inside a container, in slot order k = 0..m−1, sit at
  the container's position + ((k − (m − 1)/2) · 0.05, +0.06, −0.03),
  scale 0.6; still selectable (mirrors U7).
- **W8** Object cards: 0.20 m tall standees, upright, yaw-billboarded
  to the head, bottom edge on the mat (mirrors U8).
- **W9** Sensei: standee 0.75 m tall at x = −0.85, z = +0.25, bottom
  y = 0.75, gentle bob ±0.02 m at 0.4 Hz, yaw-billboarded (mirrors U9).
- **W10** Layout is a pure function of the scene snapshot
  (deterministic, unit-tested on desktop); rendered cards tween from
  old to new placement in 0.25 s (mirrors U10).

## 3 Rendering

- **W11** Object cards use the card art in `assets/sprites/objects/`
  (one image per object; engine S119). Sensei poses use
  `assets/sprites/sensei/` with the ghost-fade treatment of the Unity
  build (mirrors U11/U12).
- **W12** Pose priority: learner holding talk → `listen`; server state
  `thinking` → `think`; `speaking` → `speak` with the cardboard-puppet
  bounce driven by output level; a recent `animate` message → that
  pose (`point` mirrored when the target is to Sensei's right); else
  `idle_float` (mirrors U13).
- **W13** Highlight: a `highlight` message draws a pulsing warm-yellow
  glow behind each named card for the given duration (mirrors U14).
  The selected card keeps a thin white outline until selection changes
  (mirrors U15). Spawns scale in over 0.2 s; removals scale out over
  0.15 s (mirrors U16).

## 4 Panels

- **W14** Caption panel under Sensei (0.6 m wide, faces the head): the
  current utterance's sentence as it plays — Japanese line large, then
  reading, romaji and translation lines, each shown only when the
  chunk's `captions` flags allow it (PROTOCOL §4). English sentences
  render as plain text. Cleared 4 s after speech ends (mirrors U17).
- **W15** Task card above the mat centre: the active task's
  instruction (Japanese large, English small per support settings) and
  a status chip (`active` / `completed ✓` / `abandoned`). Hidden when
  there is no task (mirrors U18).
- **W16** Hint card next to the task card from a `hint` message: kind
  label plus the fields present. Stays until the next turn ends or the
  task changes (mirrors U19).
- **W17** "You said" line under the caption panel: `Listening…` while
  talk is held, `…` while transcribing, then `You: <transcript>` from
  the server's `transcript` message; `notice` codes render their
  matching short line here (mirrors U20/U23).

## 5 Input

Three input paths with identical semantics; every path is exercised by
the acceptance suite (PROTOCOL §6).

- **W18 Hands (WebXR hand tracking).** Pinch-and-hold on the talk
  button = push-to-talk (press and release mirror W23). Index pinch
  begun on a card grabs it; the card follows the pinch point; releasing
  the pinch over a region drops it there (`player_move` with region),
  over a container card into that container, anywhere else snaps back.
  A pinch tap (short pinch, no grab movement) on a card selects it.
- **W19 Controllers.** Trigger on a card = select; grip on a card =
  grab with the same drop rules as W18; A (right) held = push-to-talk;
  B = repeat (`repeat` message); X = hint (`player_event help_hint`);
  Y short press = menu toggle, Y long press (1 s) = re-place area;
  left thumbstick click = slower pace toggle (preference pace
  slow/normal) (mirrors U21–U25).
- **W20 Desktop.** Space held = push-to-talk (microphone via
  getUserMedia; the test harness substitutes a WAV file). Click a card
  = select; click-drag a card = grab and drop with the same rules;
  keys R = repeat, H = hint, M = menu, L = slower toggle.
- **W21** Drop zones are the mat thirds (the `RegionAt` geometry of
  unity SPEC §12, including its shared-edge rule). A server-rejected
  move (`action_result.ok == false`) snaps the card back; in XR it also
  fires a short controller buzz where the platform supports it
  (mirrors U22).
- **W22** Talk press while Sensei is speaking or thinking interrupts:
  the client stops playback immediately and sends `talk_start`; the
  server cancels the generation. Playback must reach silence within
  300 ms of the press (mirrors U23). Holds under 300 ms never reach
  the engine (PROTOCOL §3).

## 6 Turns and the speech pipeline (server)

- **W23** A turn runs: ASR transcript → `transcript` message → agent
  turn (PydanticAI port of the engine loop: same system prompt content,
  same 16 tools, same budgets — 8 tool calls and 4 model rounds per
  turn, same error-code mapping — engine S61–S68, S143) → tool effects
  stream out as the PROTOCOL §5 messages in causal order.
- **W24** ASR: sherpa-onnx running the SenseVoice FunASR-nano model
  server-side, ITN off (the model and measured error rates of unity
  U31 carry over; recognition happens on the server because the model
  ships with the server, not the browser). Input is the uploaded
  16 kHz mono PCM (PROTOCOL §4). ASR failure → `notice couldnt_hear`,
  no turn.
- **W25** TTS: the local Kitten TTS model, in the server process (no
  hosted TTS API). Japanese text is passed with normalization off
  (the model's normalizer is English-tuned); output is 24 kHz PCM.
  One configured voice for Sensei. Synthesis results are cached by
  (text, voice, pace); the cache is what makes repeat and replay
  instant.
- **W26** Per-sentence streaming: a `speak` tool result's segments are
  split into sentences; sentence k+1 is synthesized while sentence k
  plays; chunks go out in order under the utterance's generation
  (PROTOCOL §4). The client plays chunks gaplessly in arrival order
  and shows the caption of the sentence currently playing.
- **W27** Interrupt: a new generation (talk press, `cancel`, a new
  turn) invalidates in-flight synthesis and playback; the server sends
  `speech_stop` and drops unsent chunks for the stale generation;
  already-cached audio stays cached (mirrors engine S27).
- **W28** Replay: `repeat` and the `replay_speech` tool both serve the
  stored utterance's audio from cache where possible (engine S39/S142
  semantics, including `nothing_to_replay`).

## 7 Failure display

- **W29** A `turn_ended` with status `failed` shows a one-line English
  message on the caption panel chosen by `error_code` — the same four
  texts as unity U32 (`auth_failed`, `quota_exceeded`, `network_error`,
  `provider_error`); `llm_not_configured` shows the no-key line of W2.
  `budget_exhausted` shows nothing extra. Sensei's pose returns to
  idle (mirrors U32).

## 8 Menu and profile

- **W30** Menu panel (left-hand side in XR, overlay panel on desktop):
  LLM status (from the last turn result / `welcome`), voice status,
  recognizer status; starting-band buttons (`zero` … `advanced`,
  plain-English labels) → `set_band`; English-help toggle →
  `player_event help_english`; session summary → `session_summary`
  (practiced items with independent/supported counts, never a level);
  reset progress with a two-step confirm → `reset_profile`
  (mirrors U26). In XR, menu buttons are pressed with the opposite
  hand's pinch or the controller ray + trigger (mirrors U27).
- **W31** Slower pace is a player preference (pinned) set through
  `set_preference`; Sensei's own support changes arrive as `support`
  messages and re-render captions/task card immediately (engine S41).

## 9 Server configuration and storage

- **W32** Configuration by environment: `SENSEI_LLM_PROVIDER`,
  `SENSEI_LLM_API_KEY`, `SENSEI_LLM_MODEL`,
  `SENSEI_LLM_BASE_URL`, `SENSEI_LLM_REASONING_EFFORT` (default
  `minimal`), `SENSEI_LLM_MAX_OUTPUT_TOKENS` (default 2000) — the same
  names and defaults as engine §2/S125 so existing `sensei.env` files
  keep working. The default model stays
  `meta/muse-spark-1.3-contributor` via OpenRouter. Provider spend is
  bounded by the key's own cap; the demo deployment additionally sits
  behind a passcode (W34).
- **W33** Profiles: one JSON file per learner id under the server's
  profile directory (engine §10 semantics: atomic write via temp file
  + rename, corrupt file is an error, never silently replaced; the
  file never contains a key or raw audio).

## 10 Serving and deployment

- **W34** Development: the server runs on the Mac and is reached from
  the headset over `tailscale serve` (HTTPS) on the tailnet. Demo: the
  same server image behind Dokploy at a supercapybara.com subdomain,
  gated by a passcode the client asks for before `hello` completes.
  The Unity build is frozen; nothing in this spec changes it.

## 11 Verification

- **W35** Nothing is "ready" until the hidden acceptance suite passes
  against the integrated build on the desktop path (headed Chromium,
  emulated XR with hands and controllers, fake microphone fed from
  WAV files, an audio tap asserting non-silent output for every Sensei
  line), followed by the Quest Browser headset smoke: open the URL,
  enter AR, complete one greeting turn with audio, verified in the
  server log. Scenarios: A01 first session, A10 interrupt/repeat, A11
  returning learner, H1 hands-only, C1 controllers-only, D1 desktop —
  per PROTOCOL §6.
- **W36** Builders run targeted tests plus build/typecheck per slice;
  the full suite and the E2E pass run once, on the integrated result,
  coordinated by the orchestrator.
