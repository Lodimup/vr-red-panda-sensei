# Red Panda Sensei: Quest app spec (unity/SPEC.md)

Status: 2026-10-28. Sections marked TBD wait for the voice benchmark.
Authority: this file + `sensei-engine/SPEC.md`. The PRD (`docs/prd/`) is background; where it conflicts, these specs win.

## 0 Shape

- Unity 6000.6.5f1 project `unity/RedPandaSensei/`, Android IL2CPP ARM64, OpenXR + Meta XR Core, passthrough. Package `com.supercapybara.redpandasensei`.
- The engine (`Sensei.Engine.dll`) owns scene, tasks, evidence, learner model (engine SPEC Decisions 2). Unity renders engine state and sends player actions. Unity never mutates scene state on its own.
- Assemblies: `Sensei.App.Core` (asmdef, no UnityEngine references except `UnityEngine.Vector3`/`Quaternion` structs allowed: pure logic, EditMode-testable), `Sensei.App` (MonoBehaviours, views), `Sensei.App.Editor` (build script), `Sensei.App.Tests` (EditMode).
- Threading: engine events can arrive on any thread. `UnityEngineEvents : IEngineEvents` copies each event into a `MainThreadQueue`; views only read state on the main thread (`Update`). No engine call blocks the main thread; `RunTurnAsync` runs from an async method and its continuation is not assumed to be on the main thread.

## 1 Boot

- **U1** Boot order, each step shows a line on the status panel: (1) load `sensei.env` from `Application.persistentDataPath` (keys as engine SPEC §2); (2) install voice models (`VoiceModelInstaller`); (3) load `LocalTts`, warm it with one 1-character synth; (4) load recognizer (TBD route); (5) `SenseiEngine.CreateAsync` with `FileProfileStore(persistentDataPath/profiles)`, `ProfileId "default"`; (6) place the practice area (§2); (7) run the session-start turn (§6, U40).
- **U2** Missing key: the app still boots, scene and voice work, the status panel shows `No API key. Push sensei.env (see README).` and talk is disabled with that message. Never crash on missing models: show which model failed.
- **U3** The key is never logged, displayed, or written anywhere by the app.

## 2 Practice area layout (pure, `PracticeLayout`)

Anchor = a pose on the floor plane, placed at boot 0.9 m in front of the head along head yaw (pitch and roll ignored); re-placed when the player long-presses Y (1 s) — U22. All positions below are local to the anchor (x right, y up, z away from player).

- **U4** Mat: a flat cardboard tray quad 1.4 m × 0.5 m at y = 0.80, centre z = 0.
- **U5** Region centres: `left` x = −0.45, `center` x = 0, `right` x = +0.45, all z = 0, y = 0.80. `sensei_side` x = −0.95, z = 0.15, y = 0.80. `learner_hand` = attached to the right controller (offset 0, 0.02, 0.08 in controller space).
- **U6** Within a region, objects not in a container are ordered by `slot` ascending and laid out left to right with 0.16 m spacing, centred on the region centre; more than 4 in a region wrap to a second row at z = +0.14.
- **U7** Objects inside a container: drawn at the container position + (k·0.05 − offset, 0.06, −0.03), scale 0.6, in slot order; still selectable.
- **U8** Object card size: 0.20 m tall standee (the `_card.png`), upright, yawed to face the head (y-axis billboard), bottom edge on the mat.
- **U9** Sensei: standee 0.75 m tall at x = −0.85, z = +0.25, bottom at y = 0.75, floating bob ±0.02 m at 0.4 Hz, y-billboard to head.
- **U10** `PracticeLayout.Compute(SceneSnapshot) → IReadOnlyList<Placement{ InstanceId, CatalogId, Region, LocalPosition, Scale, ContainerId }>` is pure and deterministic. Views tween each card from its old to its new position in 0.25 s.

## 3 Rendering

- **U11** Object cards: `assets/sprites/objects/<catalog_id>_card.png` imported as textures (sync script copies from repo `assets/` into `Assets/Art/`), unlit transparent material, mipmaps on, max size 1024.
- **U12** Sensei: `assets/sprites/sensei/<pose>_card.png`. Ghost fade: shader alpha multiplier ramps linearly from 1.0 at 35 % height to 0.25 at the bottom edge. Poses: `idle_float, listen, think, speak, point, encourage, demonstrate`.
- **U13** Pose rules, priority order: player holding talk → `listen`; engine `OnSenseiState("thinking")` → `think`; engine `speaking` → `speak` pose with a cardboard-puppet bounce: vertical scale 1.0→0.97→1.0 and +0.01 m lift, triggered at each RMS peak > 0.02 over 50 ms windows (max 8 per second); last `OnAnimate(animation)` within 3 s → that pose (`point` image mirrored when the target is to the Sensei's right); else `idle_float`. No mouth-open frame (dot batch RPS4 ruling).
- **U14** Highlight: `OnHighlight(ids, durationMs)` draws a pulsing warm-yellow (#FFD54A) glow ring behind each card (scale 1.15, alpha 0.4–0.9 at 2 Hz) for `durationMs`.
- **U15** Selected object (`selected_id`): thin white outline that stays until selection changes.
- **U16** Spawn: card scales from 0 to 1 in 0.2 s; remove: scales to 0 in 0.15 s then destroyed.

## 4 Text panels (TextMeshPro, Noto Sans JP SDF, dynamic atlas, OFL licence in THIRD_PARTY)

- **U17** Caption panel under the Sensei (world space, 0.6 m wide, faces head): the current utterance's segments as they play. `ja` line large (0.045 m), then reading line (kana) if support shows readings, romaji line if support shows romaji, translation line if support shows translation (use the engine's `SupportSettings` fields; exact field names from engine SPEC §9). `en` segments as plain text. Cleared 4 s after speech ends.
- **U18** Task card above the mat centre: active task instruction (`japanese` large, `english` small per support settings) and a small status chip (`active` / `completed ✓` / `abandoned`). Hidden when no task.
- **U19** Hint card next to the task card from `OnHint(card)`: kind label + fields present. Stays until the next turn ends or the task changes.
- **U20** "You said" line under the caption panel: the last transcript sent to the engine, prefixed `You:`; shows `Listening…` while talk is held and `…` while transcribing.

## 5 Controls (Unity Input System, OpenXR controller bindings)

- **U21** Right trigger with the controller ray on a card → `PlayerSelect(id)`. Ray: line renderer from the right controller, 3 m, hits card colliders.
- **U22** Right grip on a card → grab: card follows the controller. Release over a region zone (box colliders over each region's mat third, 0.30 m tall) → `PlayerMove(id, region, null)`; release over a container card → `PlayerMove(id, null, containerId)`; release elsewhere → snap back. Engine rejection → snap back plus a short buzz (haptic 0.1 s).
- **U23** A (right) held = push-to-talk. Press: if Sensei is speaking or thinking, `CancelGeneration()` and `SpeechPlayer.StopAll(newGeneration)` first (interrupt, A22: playback silent within 300 ms); start `MicCapture.Begin()`. Release: `End()`, transcribe, then `RunTurnAsync(transcript)`. Holds shorter than 0.3 s or empty transcripts are discarded with `Didn't catch that` on the You line.
- **U24** B (right) = Repeat: player event `help_repeat` (§6). X (left) = Hint: `help_hint`. Y (left) short press = toggle the menu panel (§7); long press 1 s = re-place the practice area.
- **U25** Left thumbstick click = Slower toggle: `SetPreferenceByPlayerAsync` pace `slow`/`normal`.

## 6 Player event turns (needs engine slice E1)

- **U40** Session start: after the area is placed, the app runs a player-event turn `session_start` so Sensei greets first and asks what the learner knows (first session) or offers to continue (returning learner).
- **U41** `help_hint`, `help_repeat`, `help_english`, `area_placed` run the same way. They never count as learner utterances for evidence.

Engine slice E1 (separate, engine repo): `Task<TurnResult> RunPlayerEventTurnAsync(string eventKind, CancellationToken ct)`; `eventKind` ∈ {`session_start`, `help_hint`, `help_repeat`, `help_english`, `area_placed`}, else `invalid_arguments`. Creates a synthetic utterance (S92 rules), renders it in the per-turn context as `Player event: <kind>` with a one-line meaning, same budget and tools as a normal turn.

## 7 Menu panel (left hand, world space)

- **U26** Shows: LLM status (`ready` / `no key` / `auth failed` / `quota` / `network` — from the last `TurnResult.error_code`), voice status, recognizer status; starting band buttons (`zero`, `early_beginner`, `developing_beginner`, `intermediate`, `advanced`, labelled in plain English) → `SetStartingBandAsync`; `English help` toggle → `help_english` event; `Session summary` → shows `GetSessionSummary()` (practiced items with independent/supported counts, never a level); `Reset progress` (two-step confirm) → `ResetProfileAsync`.
- **U27** Menu buttons are pressed with the right controller ray + trigger.

## 8 Speech output (`LocalSpeechOutput : ISpeechOutput`)

- **U28** For each segment in order: `LocalTts.SynthesizeAsync(text, lang, speed)` where speed = 1.0 for `normal`, TBD for `slow` (from u0v); then `SpeechPlayer.PlayAsync`. Synthesis of segment k+1 starts while k plays. Captions show segment k while it plays.
- **U29** Returns `completed` when all played, `cancelled` if the token fires or the generation goes stale, `failed` on synthesis error (logged with the segment language and length only).
- **U30** Cache: last 64 (text, lang, speed) clips in memory.

## 9 Speech input

- **U31** Route: TBD after u0v (SenseVoice on-device or OpenRouter audio model). Recognizer failures show `Couldn't hear that (network)` / `(error)` and do not call the engine.

## 10 Failure display

- **U32** `TurnResult.status` `failed` → caption panel shows a one-line English message per `error_code` (`auth_failed`: "The API key was rejected.", `quota_exceeded`: "The model quota is used up.", `network_error`: "No connection to the model.", `provider_error`: "The model service had an error."); Sensei pose `think`→`idle_float`. `budget_exhausted` → nothing extra.

## 11 Build and test

- **U33** `tools/unity/build-android.sh`, `push-env.sh`, `run-quest.sh` (from u0) stay the entry points. `tools/unity/test-editmode.sh` runs EditMode tests in batchmode and exits non-zero on failure.
- **U34** Logcat markers for device checks: `RPS_BOOT step=<n> ok|fail`, `RPS_TURN status=<s> ms=<n> tools=<list>`, `RPS_SPEAK lang=<l> chars=<n> synth_ms=<n>`, `RPS_ASR ms=<n> chars=<n>`, `RPS_PLAYER <select|move> id=<id> ok=<bool>`. No transcript text or key in logcat in release builds; a `RPS_DEBUG_TEXT=1` line in sensei.env enables transcript text for tests.

## 12 Core API (assembly `Sensei.App.Core`, namespace `Sensei.App.Core`; exact names, EditMode-testable)

```csharp
public readonly struct Placement {
    public string InstanceId { get; } public string CatalogId { get; } public string Region { get; }
    public string? ContainerId { get; } public UnityEngine.Vector3 LocalPosition { get; } public float Scale { get; }
}
public static class PracticeLayout {
    public const float MatY = 0.80f, RegionSpacing = 0.45f, SlotSpacing = 0.16f, RowDepth = 0.14f;
    public static IReadOnlyList<Placement> Compute(Sensei.Engine.World.SceneSnapshot scene);   // U5-U7, U10; learner_hand items get LocalPosition = Vector3.zero (view attaches them to the controller)
    public static UnityEngine.Vector3 RegionCentre(string region);                               // U5
    public static string? RegionAt(UnityEngine.Vector3 localPoint);                              // U22 drop zones: |x - centre| <= 0.225, z in [-0.25, 0.25], y in [0.75, 1.10] -> left/center/right, else null
}
public enum SenseiPose { IdleFloat, Listen, Think, Speak, Point, Encourage, Demonstrate }
public sealed class PoseState {                         // U13, pure; time in seconds supplied by caller
    public void SetTalkHeld(bool held);
    public void SetEngineState(string state);           // "idle" | "thinking" | "speaking"
    public void OnAnimate(string animation, double now); // engine names: idle_float, listen, think, speak, point, encourage, demonstrate
    public SenseiPose Current(double now);
}
public sealed class MainThreadQueue {                   // thread-safe
    public void Post(System.Action action);
    public int Drain(int max = 64);                     // runs up to max actions in FIFO order on the caller's thread, returns count
}
public static class TranscriptGate {                    // U23
    public const float MinHoldSeconds = 0.3f;
    public static bool ShouldSend(float holdSeconds, string? transcript);   // false if hold < 0.3 or transcript null/whitespace
}
public static class TurnMessages {                       // U32
    public static string? ForTurn(string status, string? errorCode);       // failed+auth_failed -> "The API key was rejected." etc.; budget_exhausted/completed/cancelled -> null; failed with unknown code -> "Something went wrong. Try again."
}
```
(Check the engine for the real namespace of `SceneSnapshot` and use it; the test writer uses the same.)
