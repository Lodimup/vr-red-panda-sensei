# sensei-engine specification

`sensei-engine` is the LLM game-master half of Red Panda Sensei, kept separate from the Unity app so that it can be driven and tested from a command line. It owns the authoritative practice scene, the object catalog, task predicates, the 16 game-master tools, the learner model and the agent loop. Unity (later) only renders, captures input and plays audio, through the ports defined here.

The product requirements are in `../docs/prd/`. Where this document and the PRD disagree, this document wins for the engine; the differences are deliberate and listed in "Decisions" at the end.

Every behaviour carries a story id (`S1`, `S2`, ...). Tests reference those ids.

---

## 1 Layout and build

| Path | What |
|---|---|
| `sensei-engine/SenseiEngine.sln` | Solution. |
| `sensei-engine/src/Sensei.Engine/` | Library, `netstandard2.1`, `LangVersion` latest, nullable enabled. Namespace root `Sensei.Engine`. |
| `sensei-engine/src/Sensei.Engine.Cli/` | Console app, `net10.0`, `AssemblyName` `sensei`. |
| `sensei-engine/tests/Sensei.Engine.Tests/` | xUnit, `net10.0`. |

- **S1** `dotnet build sensei-engine/SenseiEngine.sln -warnaserror` and `dotnet test sensei-engine/SenseiEngine.sln` succeed with no network access and no API key.
- **S2** The library references `Microsoft.Agents.AI` and `Microsoft.Agents.AI.OpenAI` version **1.26.0** (Microsoft Agent Framework). It does not reference any Unity assembly.
- **S3** IL2CPP readiness rules, checked by tests that inspect the source tree:
  - no tool is created with `AIFunctionFactory.Create`; every tool is an `AIFunction` subclass whose `JsonSchema` is an explicit, checked-in JSON schema;
  - every type serialized or deserialized by the engine goes through one `System.Text.Json` source-generated context (`SenseiJsonContext`); no call to a `JsonSerializer` overload that takes only a `Type` or generic `T` without `JsonTypeInfo`;
  - no `dynamic`, no `Reflection.Emit`, no `Activator.CreateInstance` from a model-chosen name.

## 2 Configuration: LLM provider

`Sensei.Engine.Llm.LlmSettings` and `Sensei.Engine.Llm.ChatClientFactory`.

| Env var | Meaning | Default |
|---|---|---|
| `SENSEI_LLM_PROVIDER` | `openrouter`, `meta` or `openai_compatible` | `openrouter` |
| `SENSEI_LLM_API_KEY` | Bearer key | none, required to talk to a model |
| `SENSEI_LLM_MODEL` | Model id | `meta/muse-spark-1.3-contributor` |
| `SENSEI_LLM_BASE_URL` | Override base URL; required for `openai_compatible` | provider preset |

Presets: `openrouter` → `https://openrouter.ai/api/v1`; `meta` → `https://api.meta.ai/v1`.

- **S4** `LlmSettings.FromEnvironment(IReadOnlyDictionary<string,string?> env)` applies the table above. Unknown provider → `LlmConfigurationException` whose message names the allowed values. Missing key → `LlmConfigurationException` with message `SENSEI_LLM_API_KEY is not set`. `openai_compatible` without base URL → `LlmConfigurationException`. Values are trimmed; empty counts as missing.
- **S5** `LlmSettings.ToString()` never contains the key; it shows `provider=<p> model=<m> base_url=<u> api_key=[set]`.
- **S6** `ChatClientFactory.Create(LlmSettings)` returns a `Microsoft.Extensions.AI.IChatClient` backed by the OpenAI chat-completions client pointed at the base URL, model and key. Unity code builds `LlmSettings` directly (the player's key) through the public constructor `LlmSettings(string provider, string apiKey, string? model = null, string? baseUrl = null)`, which applies the same validation and defaults.

## 3 Public engine API

```csharp
namespace Sensei.Engine;

public sealed class SenseiEngineOptions {
    public required IChatClient? ChatClient { get; init; }   // null = tools usable, RunTurnAsync fails with llm_not_configured
    public required ISpeechOutput Speech { get; init; }
    public required IProfileStore ProfileStore { get; init; }
    public string ProfileId { get; init; } = "default";
    public IEngineEvents? Events { get; init; }
    public ITraceSink? Trace { get; init; }
    public TimeProvider? Clock { get; init; }
}

public sealed class SenseiEngine : IAsyncDisposable {
    public static Task<SenseiEngine> CreateAsync(SenseiEngineOptions options, CancellationToken ct = default);
    public string SessionId { get; }
    public IReadOnlyList<string> ToolNames { get; }
    public SceneSnapshot GetScene();
    public LearnerProfileView GetProfile();          // deep copy, safe to keep
    public TaskView? GetActiveTask();

    // conversation
    public Task<TurnResult> RunTurnAsync(string transcript, CancellationToken ct = default);
    public Task<TurnResult> CorrectLastTranscriptAsync(string correctedTranscript, CancellationToken ct = default);
    public void CancelGeneration();

    // manual turns: the same validation path the agent uses, for tests and the CLI
    public void BeginTurn(string transcript);
    public void MarkRoundStart();
    public Task<ToolResult> InvokeToolAsync(string toolName, string argumentsJson, string? callId = null, CancellationToken ct = default);

    // player actions (actor = player); never wait for the LLM
    public PlayerActionResult PlayerSelect(string instanceId);
    public PlayerActionResult PlayerMove(string instanceId, string? region, string? containerId);
    public Task SetPreferenceByPlayerAsync(SupportPatch patch, CancellationToken ct = default);
    public Task SetStartingBandAsync(string band, CancellationToken ct = default);
    public SessionSummary GetSessionSummary();       // read-only, does not end anything
    public Task ResetProfileAsync(CancellationToken ct = default);
}
```

- **S7** `CreateAsync` loads the profile `ProfileId` from the store, or creates a new one (version 1, no starting band, working level `pre_n5`, zero-band support defaults) and saves it. It appends a session record `{session_id, started_at}` to the profile and saves. `SessionId` has the form `s-<yyyyMMddHHmmss>-<4 lowercase hex>`.
- **S8** The scene starts empty at revision 0 on every engine instance. The scene and the active task are never persisted (the practice area is placed again after a restart, PRD §4).
- **S9** All public members are thread-safe: player actions may arrive from another thread while a turn runs. Scene mutations are serialized by one lock.
- **S10** `ToolNames` is exactly, in this order: `inspect_scene, spawn_objects, move_object, remove_objects, highlight_objects, animate_sensei, speak, start_challenge, update_challenge, show_hint, set_support, record_attempt, end_activity, query_learner, replay_speech, remember_note`.

## 4 Catalog

`Sensei.Engine.Catalog.ObjectCatalog` (static, data in code).

| catalog_id | japanese | reading | romaji | english | variants | container |
|---|---|---|---|---|---|---|
| water | 水 | みず | mizu | water | default | no |
| tea | お茶 | おちゃ | ocha | tea | default | no |
| cup | コップ | コップ | koppu | cup | blue, red | no |
| apple | りんご | りんご | ringo | apple | default | no |
| banana | バナナ | バナナ | banana | banana | default | no |
| rice_ball | おにぎり | おにぎり | onigiri | rice ball | default | no |
| book | 本 | ほん | hon | book | green, yellow | no |
| pen | ペン | ペン | pen | pen | default | no |
| bag | かばん | かばん | kaban | bag | default | yes |
| box | 箱 | はこ | hako | box | default | yes |
| key | 鍵 | かぎ | kagi | key | default | no |
| umbrella | 傘 | かさ | kasa | umbrella | default | no |

- **S11** The catalog has exactly these 12 entries. The first listed variant is the default. Sprites are 32×32 (PRD §7); the engine stores only ids.

## 5 Scene

Regions: `left`, `center`, `right` (the practice area, ordered left to right in the task frame), `learner_hand`, `sensei_side`.

`ObjectInstance`: `id`, `catalog_id`, `variant`, `region`, `slot`, `container_id` (null unless inside a container).

- **S12** Instance ids are `<catalog_id>_<n>`, `n` counting from 1 per catalog id per engine instance, never reused even after removal.
- **S13** At most **16** instances exist in the scene at once, counting those inside containers and hands.
- **S14** `revision` starts at 0 and increases by exactly 1 per committed mutating call (a `spawn_objects` call that creates 3 objects is one revision) and per committed player move. Selection, highlight, animation, speech and hints do not change the revision.
- **S15** `slot`: each of `left`, `center`, `right` keeps a counter; an object entering the region gets the next value. Objects in containers or in `learner_hand`/`sensei_side` keep a slot but it is ignored for ordering.
- **S16** An instance inside a container has `container_id` set and its `region` equal to the container's region; moving a container moves its contents' `region` too.
- **S17** Every committed change appends a `SceneEvent`: `event_id` (from 1, increasing), `revision` (after the change), `actor` (`sensei` | `player`), `kind` (`spawned`, `moved`, `removed`, `selected`, `task_completed`), `instance_ids`, `from`, `to` (region name or container id), `purpose` (moves by Sensei only), `turn_id`.
- **S18** `PlayerSelect(id)`: unknown id → `PlayerActionResult{Ok=false, ErrorCode="unknown_object"}`; otherwise sets `selected_id`, appends a `selected` event, no revision change.
- **S19** `PlayerMove(id, region, containerId)`: exactly one of `region`/`containerId`; same destination rules as `move_object` (S30); commits with actor `player`, increments revision, then evaluates the active task. Returns `Ok`, `ErrorCode`, `Revision`.
- **S20** `SceneSnapshot` (also the `data` of `inspect_scene`): `revision`, `selected_id`, `instances` (each with `id, catalog_id, variant, region, slot, container_id, japanese, reading, english`), `active_task` (TaskView or null), `support` (current support settings).

## 6 Tool calls: shared rules

Every tool is invoked as Sensei, through the same path whether the caller is the agent loop or `InvokeToolAsync`.

**ToolResult** (returned to the model as JSON, snake_case): `call_id`, `tool`, `status` (`success` | `failure` | `cancelled`), `error_code` (null on success), `detail` (short human-readable reason, null on success), `revision` (scene revision after the call), `affected_ids` (array, may be empty), `data` (tool-specific object or null).

**Error codes** (exact strings): `invalid_arguments`, `unknown_tool`, `unknown_object`, `unknown_catalog_id`, `unsupported_asset`, `capacity_exceeded`, `invalid_destination`, `stale_scene`, `impossible_task`, `task_already_active`, `no_active_task`, `object_in_active_task`, `preference_pinned`, `speech_limit_exceeded`, `excerpt_not_in_transcript`, `unknown_event`, `turn_budget_exhausted`, `nothing_to_replay`, `note_limit_exceeded`, `looks_like_credential`, `speech_failed`, `no_turn`. A cancelled call has status `cancelled` and error_code `cancelled`.

- **S21 Strict arguments.** Arguments are a JSON object. Unknown properties, missing required properties, wrong JSON types, and out-of-range values → `failure`/`invalid_arguments`, `detail` naming the offending property path. Enum-like strings are matched exactly (lowercase); an unknown value → `invalid_arguments` with `detail` listing allowed values. Malformed JSON → `invalid_arguments`.
- **S22 No mutation on failure.** A call that returns `failure` or `cancelled` changes nothing: no revision change, no event, no assistance record, no evidence, no profile write.
- **S23 Turns.** Tools need a current turn. `BeginTurn(transcript)` starts one: increments `turn_id` (from 1), sets the generation to a new value, records the transcript as a learner utterance, sets the round base (S26), resets the per-turn budgets. `RunTurnAsync` does the same internally. `InvokeToolAsync` before any turn → `failure`/`no_turn`.
- **S24 Idempotency.** A `call_id` already used in this engine instance returns the cached `ToolResult` unchanged (same object values), creates no new events, and does not count against the budget. When `callId` is null the engine assigns `m<n>`.
- **S25 Budget.** Per turn: at most **8** tool calls (cached duplicates excluded). Call 9 and later → `failure`/`turn_budget_exhausted`. Validation failures (every failure code except `stale_scene`, `speech_failed` and `turn_budget_exhausted`) are counted; after the **2nd** validation failure in a turn the turn is exhausted and every later call in that turn → `turn_budget_exhausted`. (One repair attempt, PRD §10.)
- **S26 Stale scene.** The engine keeps a round base = the id of the last scene event when the current model round began (`BeginTurn`, each model request in the agent loop, or `MarkRoundStart()`). A mutating tool (`spawn_objects`, `move_object`, `remove_objects`, `start_challenge`, `update_challenge`) fails with `stale_scene` if a **player** mutation event (a `moved` event with actor `player`) has an id greater than the round base. Its `data` is a fresh `SceneSnapshot`. Sensei's own earlier calls in the same round never make the scene stale.
- **S27 Cancellation.** `CancelGeneration()` advances the generation. Any call made for an older generation → `cancelled`, no effect. After `CancelGeneration()`, `InvokeToolAsync` returns `cancelled` until the next `BeginTurn`. An in-flight `speak` is cancelled through its `CancellationToken` and returns `cancelled`.
- **S28 Trace.** Every call, including failures, cancelled calls and cached duplicates, writes one `TraceEntry` to `ITraceSink`: `at, session_id, turn_id, generation_id, round, call_id, tool, arguments` (sanitized JSON string), `status, error_code, revision, elapsed_ms, duplicate` (bool). Sanitizing replaces any substring matching the credential pattern (S56) with `[redacted]`. No raw audio, no API key, ever.

## 7 World tools

All ids below refer to instance ids unless named `catalog_id`.

**inspect_scene** `{}` → `data` = `SceneSnapshot`. Never mutates. (**S29**)

**spawn_objects** `{ "objects": [ { "catalog_id": str, "variant"?: str, "region"?: "left"|"center"|"right"|"sensei_side", "quantity"?: 1-4 } ] }`, 1–6 entries, total quantity ≤ 6.
- **S30a** Defaults: variant = catalog default, region = `center`, quantity = 1.
- **S30b** Unknown `catalog_id` → `unknown_catalog_id`; variant not in that entry's list → `unsupported_asset`; region `learner_hand` → `invalid_arguments`; scene would exceed 16 → `capacity_exceeded` (nothing spawned).
- **S30c** Success: `affected_ids` lists new ids in argument order; one `spawned` event; revision +1.

**move_object** `{ "object_id": str, "destination": { "region"?: region, "container_id"?: str }, "purpose": "fulfill_request"|"demonstrate"|"arrange" }`
- **S31a** Exactly one of `region`/`container_id`, else `invalid_arguments`. Unknown object or container → `unknown_object`.
- **S31b** `invalid_destination` when: `container_id` names a non-container; the object is itself a container and the destination is a container; the destination is the object itself; the object is already at that destination (same region and no container, or same container).
- **S31c** Success: one `moved` event with `from`, `to`, `purpose`; revision +1; `affected_ids` = `[object_id]` plus the ids of its contents if it is a container. Moving into a container sets `container_id` and region; moving to a region clears `container_id`.
- **S31d** `purpose: demonstrate` records assistance `demonstration` for the active task's skill targets.

**remove_objects** `{ "object_ids": [str, 1-16] }`
- **S32a** Unknown id → `unknown_object`. An id referenced by an active task predicate (`object_id`, `reference_id` or `container_id`) → `object_in_active_task`. A container whose contents are not all listed too → `invalid_arguments` with detail `container_not_empty`.
- **S32b** Success: one `removed` event, revision +1, `affected_ids` = removed ids; a removed `selected_id` becomes null.

**highlight_objects** `{ "object_ids": [str, 1-4], "duration_ms"?: 500-8000 }` (default 2000)
- **S33** Unknown id → `unknown_object`. Success raises `IEngineEvents.OnHighlight`. If any highlighted id is referenced by an active task predicate (directly, or by matching a predicate selector), assistance `highlight_answer` is recorded for the task's skill targets and `data.recorded_assistance` = `"highlight_answer"`; otherwise `data.recorded_assistance` = null.

**animate_sensei** `{ "animation": "idle_float"|"listen"|"think"|"speak"|"point"|"encourage"|"demonstrate", "target_id"?: str }`
- **S34** Unknown target → `unknown_object`. Success raises `OnAnimate`. No revision change.

**start_challenge**
```json
{ "family": "discover_request"|"listen_arrange"|"converse_roleplay",
  "instruction": { "japanese"?: str<=80, "english"?: str<=200 },
  "skill_targets": [ { "dimension": "vocabulary"|"listening"|"production"|"reading"|"interaction"|"register",
                       "key": str 1-40, "japanese"?: str<=30 } ],        // 1-3
  "predicates": [ Predicate ],                                            // 1-3
  "scenario_properties"?: [ { "object_id": str, "name": /^[a-z_]{1,24}$/, "value": str 1-24 } ],  // 0-4
  "role"?: str<=40,
  "is_fresh_variation"?: bool }
```
Predicate (`type` decides which fields are allowed; any other field → `invalid_arguments`):

| type | fields | true when |
|---|---|---|
| `selected` | `object_id` | `selected_id == object_id` |
| `in_region` | `object_id` or `selector`, `region` | the object (or at least one selector match) is in `region` and not in a container |
| `contained_in` | `object_id` or `selector`, `container_id` | the object (or a match) has `container_id` |
| `quantity` | `selector`, `region` or `container_id`, `count` 1-8, `comparison`? `eq`(default)/`gte`/`lte` | number of selector matches at that place compares to `count` |
| `left_of` / `right_of` | `object_id`, `reference_id` | both in `left`/`center`/`right`, not in containers, and (region index, slot) of `object_id` is less than (left_of) / greater than (right_of) that of `reference_id` |
| `unchanged` | `object_id` | no `moved` or `removed` event for it since the task started |
| `before` | `object_id`, `reference_id` | both moved since task start, and the first such move of `object_id` has a smaller event id |

`selector` = `{ "catalog_id": str, "variant"?: str, "property"?: { "name": str, "value": str } }`; `property` must match a `scenario_properties` entry of this task.

- **S35a** Exactly one of `instruction.japanese`/`english` may be absent, not both. A task already `active` → `task_already_active`.
- **S35b** `impossible_task` (detail says which) when: a referenced object or container does not exist or a `container_id` is not a container; `object_id == reference_id`; a selector's `catalog_id` or `variant` is not in the catalog, or its `property` is not declared; a `quantity` count with `eq`/`gte` exceeds the number of instances matching the selector in the whole scene; every predicate is already true at start (a task containing only `unchanged` predicates is allowed); a `scenario_properties.object_id` does not exist.
- **S35c** Success: `TaskView` with `task_id` `t<n>`, `status` `active`, `family`, `skill_targets`, `instruction`, `predicates`, `scenario_properties`, `role`, `is_fresh_variation`, `source_revision`, `start_event_id`, `assistance_baseline` (copy of support settings). Returned in `data`; raises `OnTaskChanged`.
- **S35d Evaluation.** After every committed mutation (Sensei or player), the active task's predicates are evaluated. When all are true: status `completed`, `completed_by` = actor of the event that made them true, `completed_event_id` set, a `task_completed` event appended (no revision change), `OnTaskChanged` raised. The tool result of the Sensei call that completed it has `data.task_status = "completed"`.

**update_challenge** `{ "task_id": str, "action": "revise"|"abandon", "reason": str 1-120, "instruction"?: ..., "predicates"?: [...] }`
- **S36** No active task, or `task_id` not the active one → `no_active_task`. `revise` needs `instruction` and/or `predicates`, validated exactly as `start_challenge` (except `task_already_active`), and keeps `task_id`; `start_event_id` and `source_revision` reset to now. `abandon` → status `abandoned`. Either way `OnTaskChanged`.

## 8 Speech tools

`ISpeechOutput`:
```csharp
public interface ISpeechOutput {
    Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct);
}
public sealed record SpeechSegment(string Lang, string Text, string? Reading, string? Romaji, string? Translation);
public sealed record SpeechRequest(string UtteranceId, long GenerationId, IReadOnlyList<SpeechSegment> Segments, string Pace);
public sealed record SpeechOutcome(string Status, string? Error);   // "completed" | "cancelled" | "failed"
```

**speak** `{ "segments": [ { "lang": "ja"|"en", "text": str, "reading"?: str, "romaji"?: str, "translation"?: str } ], "pace"?: "normal"|"slow" }` (1–6 segments; pace default = support `pace`)
- **S37a** Length is counted in Unicode scalar values. `ja` text 1–60; `en` text 1–160; over → `speech_limit_exceeded`.
- **S37b** A `ja` segment must contain at least one kana or kanji character; an `en` segment must contain none (hiragana, katakana, CJK ideographs) → else `invalid_arguments` with detail `split_language_segments`.
- **S37c** Per turn, spoken characters (all `speak` calls plus `show_hint` with `speak: true`) total ≤ 480; a call that would pass it → `speech_limit_exceeded`, nothing spoken.
- **S37d** Success: utterance id `u<n>`; the request is passed to `ISpeechOutput` with the current generation; the result `status` is `success` when the outcome is `completed`, `cancelled` when `cancelled`, `failure`/`speech_failed` when `failed`. `data` = `{ utterance_id, outcome }`. The utterance is kept in speech history (last 20) for replay and for audio-model detection.
- **S37e Audio model.** If the active task has a skill target with `japanese`, and a `ja` segment's text contains that `japanese` string (NFKC, whitespace removed), assistance `audio_model` is recorded for that target.

**show_hint** `{ "kind": "partial_phrase"|"model_answer"|"explanation"|"vocabulary_card"|"translation", "content": { "japanese"?: str<=60, "reading"?: str<=60, "romaji"?: str<=80, "english"?: str<=160 }, "target_key"?: str, "speak"?: bool }`
- **S38a** `content` needs at least one field. Raises `OnHint` with a `HintCard`. A `vocabulary_card` never creates or names a scene object.
- **S38b** Assistance recorded: kind name (`translation` is recorded as `translation_hint`) for `target_key` if given, else for every skill target of the active task; with no active task and no `target_key` nothing is recorded.
- **S38c** `speak: true` speaks `japanese` as a `ja` segment and `english` as an `en` segment, through the same rules and limits as `speak` (S37); a speech failure makes the whole call fail with no assistance recorded. `data` = `{ card, utterance_id?, outcome? }`.

**replay_speech** `{ "utterance_id"?: str, "pace"?: "normal"|"slow" }`
- **S39** Replays a stored utterance (default: the latest) through `ISpeechOutput` with a new utterance id. Unknown id or empty history → `nothing_to_replay`. Records assistance `repetition` for the active task's skill targets. Does not count toward the 480-character limit.

## 9 Support, learner model and evidence

**Support settings**: `show_japanese`, `show_reading`, `show_romaji`, `show_translation` (bool), `pace` (`normal`|`slow`), `correction_style` (`light`|`detailed`), `language_mode` (`english_explanations`|`mixed`|`japanese_only`).

Starting bands: `zero`, `early_beginner`, `developing_beginner`, `intermediate`, `advanced`.

| band | japanese | reading | romaji | translation | language_mode | initial working level |
|---|---|---|---|---|---|---|
| zero | true | true | true | true | english_explanations | pre_n5 |
| early_beginner | true | true | true | true | mixed | n5 |
| developing_beginner | true | true | false | true | mixed | n4 |
| intermediate | true | true | false | false | mixed | n3 |
| advanced | true | false | false | false | japanese_only | n2 |

Defaults for a new profile: the `zero` row, `pace` `normal`, `correction_style` `light`, band null.

**Hidden JLPT working level** (RULING(Handler 2026-10-27): the learner gets a JLPT level behind the scenes so difficulty is correct): `pre_n5`, `n5`, `n4`, `n3`, `n2`, `n1`. It calibrates Sensei's vocabulary and grammar through the agent context. It is never shown to the learner as a score (PRD §3): it does not appear in `SessionSummary`, `end_activity` results, hint cards or speech produced by the engine. It is visible to the model (context, `query_learner`) and to the builder (CLI `/profile`).

- **S40 Pinning.** `SetPreferenceByPlayerAsync(SupportPatch)` sets the given fields and marks them pinned; saves the profile.
- **S41 set_support** `{ "reason": "learner_request"|"adaptation", "show_japanese"?, "show_reading"?, "show_romaji"?, "show_translation"?, "pace"?, "correction_style"?, "language_mode"?, "difficulty"?: "easier"|"harder", "starting_band"?: band, "self_reported_jlpt"?: "n5"|"n4"|"n3"|"n2"|"n1" }`
  - at least one field besides `reason`, else `invalid_arguments`;
  - with `reason: adaptation`, changing a pinned field → `preference_pinned` and nothing changes; `starting_band` with `adaptation` is only allowed while the band is null;
  - with `reason: learner_request` any field may change; pinned fields stay pinned with the new value;
  - setting `starting_band` applies that band's row to every non-pinned display field and to `language_mode`, and sets the working level to the band's level (reason `band`);
  - `self_reported_jlpt` sets the working level to that level (reason `self_report`) when no evidence exists yet, or when `reason` is `learner_request`;
  - `difficulty: harder|easier` moves the working level one step up or down, clamped to `pre_n5..n1` (reason `learner_request` or `adaptation`, following `reason`);
  - success saves the profile, raises `OnSupportChanged`, and `data` = new support settings + pinned field names + `working_level`. Changes are recorded in the session's `preference_changes`.
- **S42** `SetStartingBandAsync(band)` behaves like `set_support` with `starting_band` and `reason: learner_request`, as the player.

**Assistance kinds**: `transcript_visible`, `translation_visible`, `partial_phrase`, `model_answer`, `explanation`, `vocabulary_card`, `translation_hint`, `highlight_answer`, `demonstration`, `repetition`, `audio_model`. Each record: `kind, target_key, task_id, turn_id, event_id` (last scene event id when recorded).

**record_attempt**
```json
{ "target": { "dimension": dim, "key": str 1-40, "japanese"?: str<=30 },
  "transcript_excerpt": str 1-120,
  "communicative_success": bool,
  "language_assessment": "correct"|"minor_issue"|"meaning_blocking_error"|"unclear_input",
  "event_ids"?: [int],
  "note"?: str<=160,
  "is_fresh_variation"?: bool }
```
- **S43** `transcript_excerpt` must occur in a learner transcript of the current or previous turn, compared after NFKC, lowercasing and collapsing whitespace → else `excerpt_not_in_transcript`. Unknown `event_ids` → `unknown_event`.
- **S44 Relevant aids** by target dimension:
  - `listening`: `transcript_visible`, `translation_visible`, `highlight_answer`, `demonstration`, `repetition`
  - `vocabulary`: `model_answer`, `partial_phrase`, `vocabulary_card`, `audio_model`, `demonstration`
  - `production`: `model_answer`, `partial_phrase`, `audio_model`, `demonstration`
  - `reading`: `translation_visible`, `translation_hint`, `audio_model`, `highlight_answer`
  - `interaction`, `register`: `model_answer`, `demonstration`

  Aids considered: assistance records for this target key made after the later of (the active task's `start_event_id`, this key's previous committed attempt), plus `audio_model` from the current or previous turn. For `listening` targets, `show_japanese == true` at attempt time counts as `transcript_visible` and `show_translation == true` as `translation_visible`; for `reading` targets `show_translation == true` counts as `translation_visible`.
- **S45 Outcome** (first rule that applies): `unclear_input` → `unresolved`; `communicative_success == false` → `confirmed_incorrect`; aid `demonstration` → `completed_by_demonstration`; aid `model_answer` or `audio_model` → `success_after_model`; any other aid except `repetition` → `success_after_hint`; aid `repetition` → `success_after_repetition`; else `independent_success`.
- **S46 Evidence record**: `attempt_id` (`a<n>` per profile), `session_id`, `turn_id`, `target`, `transcript_excerpt`, `event_ids`, `actors` (actors of those events), `engine_outcome` (`task_completed` | `task_active` | `no_task`), `task_id`, `language_assessment`, `communicative_success`, `assistance` (kinds counted), `outcome`, `is_fresh_variation`, `uncertainty` (`high` when `unresolved`, else `low`), `rule_version` `"rules-v1"`, `superseded` (false), `at`. Saved to the profile at once.
- **S47** One commit per `(turn_id, dimension, key)`: a second `record_attempt` for the same triple in the same turn returns `success` with the first attempt's data plus `"duplicate": true` and writes nothing.
- **S48 Transcript correction.** `CorrectLastTranscriptAsync(text)` marks every attempt recorded in the previous learner turn as `superseded: true` (excluded from labels, rules and summaries), then runs a normal turn with `text`; the agent context states that the previous transcript was corrected from X to Y.
- **S49 Labels** per target key, over non-superseded, non-`unresolved` attempts: `stable` (independent successes in ≥2 different sessions) > `used_independently` (≥1 `independent_success`) > `used_with_help` (≥1 other success outcome) > `practiced` (any attempt).
- **S50 Recommendations** (`rules-v1`), per target over its last 4 resolved attempts: `step_up` when ≥3 are `independent_success` and ≥1 of those 3 is a fresh variation; `offer_simpler` when ≥2 are `confirmed_incorrect`.
- **S51 Working level from evidence**, checked after every committed attempt, over resolved attempts since the last level change: if there are ≥8 and ≥6 are `independent_success` with ≥2 fresh variations → up one (reason `evidence_up`); else if ≥3 of the last 6 are `confirmed_incorrect` across ≥2 different target keys → down one (reason `evidence_down`). Clamped. Every change appends `{from, to, reason, at}` to `level_history`.
- **S52 Review queue**: target keys whose latest resolved outcome is `confirmed_incorrect` or whose label is `used_with_help`, plus keys labelled `used_independently` only within the current session.

**query_learner** `{ "target_key"?: str }` → `data` = `{ starting_band, working_level, support, pinned, targets: [ { key, dimension, label, last_outcome, attempts } ], review_due, recommendations: [ { key, kind } ], notes }`; with `target_key`, `targets` has only that key (empty if unknown). (**S53**)

**remember_note** `{ "key": /^[a-z][a-z0-9_]{0,31}$/, "value": str 1-120 }` (**S54**): stores or replaces the note; a 31st distinct key → `note_limit_exceeded`; a value matching the credential pattern → `looks_like_credential`. Saves the profile.

**end_activity** `{ "next_suggestion": str 1-160 }` (**S55**): ends the active task if any (status `ended`), builds the `SessionSummary` from committed evidence of this session, raises `OnSummary`, saves the profile, returns the summary in `data`. Free conversation continues afterwards.

`SessionSummary`: `session_id`, `practiced` (`[ { key, dimension, label, independent, supported, unresolved } ]`), `unresolved_keys`, `next_suggestion` (latest from `end_activity`, else null), `preference_changes` (`[ { field, from, to, by } ]`). It never contains the working level. `GetSessionSummary()` returns the same shape without ending anything.

- **S56 Credential pattern**: case-insensitive `sk-[a-z0-9_-]{8,}`, or `api[_ -]?key`, or `password`, or any run of 32+ characters from `[A-Za-z0-9+/_=-]`.

## 10 Profile storage

```csharp
public interface IProfileStore {
    Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct);
    Task SaveAsync(LearnerProfile profile, CancellationToken ct);
    Task DeleteAsync(string profileId, CancellationToken ct);
}
```
- **S57** `FileProfileStore(string directory)` stores `<directory>/<profileId>.json` via the source-generated context, writing to a temp file and renaming. `profileId` must match `^[a-z0-9_-]{1,40}$` (else `ArgumentException`). A corrupt file → `ProfileCorruptException` (never silently replaced).
- **S58** Profile JSON fields: `version` (1), `profile_id`, `created_at`, `updated_at`, `starting_band`, `self_reported_jlpt`, `working_level`, `level_history`, `support`, `pinned`, `evidence`, `notes`, `sessions` (each `{ session_id, started_at, preference_changes, next_suggestion }`). It never contains an API key or raw audio.
- **S59** Restart: a new engine on the same store and profile id restores band, support, pins, evidence, labels, notes and working level (PRD A11).
- **S60** `ResetProfileAsync()` deletes the stored profile and replaces it with a new default profile (S7) in place; the scene is cleared to revision 0's empty state (revision keeps increasing; it never goes back).

## 11 Agent loop

`Sensei.Engine.Agent.SenseiAgent`, used by `RunTurnAsync`.

- **S61** Built on `Microsoft.Agents.AI.ChatClientAgent` (MAF) over the configured `IChatClient`, with the 16 tools from §7–§9 as its tools, an `AIContextProvider` that injects the per-turn context as instructions (not persisted in history), and function-invocation middleware that writes the trace. Concurrent tool invocation is off: calls from one response execute in order.
- **S62 System prompt** (`SystemPrompt.cs`) states at least: Sensei is the ghost red panda game master; every effect, including all speech, happens only through tools; plain assistant text is never spoken or shown; never claim an action happened unless its tool result was `success`; Japanese goes in `ja` segments and English in `en` segments; one focused correction at a time; separate communicative success from the language target; demonstrations and shown answers are help, not achievement; never mention the JLPT working level or any score to the learner; use only catalog objects for scene actions and a vocabulary card for anything else; a JLPT-level guide (what vocabulary and grammar to use at `pre_n5` … `n1`); the catalog table.
- **S63 Per-turn context** (injected each model request): learner transcript; whether the previous turn was interrupted or corrected; starting band, working level, language mode, support settings and pins; recommendations and review due; notes; scene snapshot; active task; selected object; player events since the previous turn; remaining tool-call budget and model rounds.
- **S64 Rounds.** At most **4** model requests per turn, enforced by a delegating chat client below MAF's function invocation: a 5th request is not sent to the provider; it returns an empty assistant message so the loop ends, and the turn status is `budget_exhausted`. The round base (S26) is set before each request.
- **S65 History.** Conversation history keeps the last 12 learner turns (a reducer cuts only at learner-message boundaries). Before each turn, any function call in history without a result gets a synthetic result `{"status":"cancelled","error_code":"cancelled"}`, so the provider never receives an unanswered tool call (PRD §10).
- **S66 Cancellation.** `CancelGeneration()` during `RunTurnAsync` cancels the provider request and any in-flight speech; `RunTurnAsync` returns status `cancelled` (it does not throw); later tool calls from that generation are `cancelled`.
- **S67** `TurnResult`: `turn_id`, `generation_id`, `status` (`completed` | `cancelled` | `failed` | `budget_exhausted`), `error_code` (`llm_not_configured`, `auth_failed`, `quota_exceeded`, `network_error`, `provider_error`; null otherwise), `error_detail`, `tool_calls` (trace entries of this turn), `spoken` (`[ { utterance_id, segments, outcome } ]`), `unspoken_text` (any plain assistant text, trace only), `rounds`, `input_tokens`, `output_tokens`. HTTP 401/403 → `auth_failed`, 402/429 → `quota_exceeded`, connection failures and timeouts → `network_error`, other provider errors → `provider_error`. The scene and profile are unchanged by a failed request itself.
- **S68** Only one turn runs at a time; a new `RunTurnAsync` cancels the running one first and waits for it to settle.

## 12 Events port

```csharp
public interface IEngineEvents {
    void OnSceneChanged(SceneEvent e, SceneSnapshot scene);
    void OnHighlight(IReadOnlyList<string> ids, int durationMs);
    void OnAnimate(string animation, string? targetId);
    void OnHint(HintCard card);
    void OnSupportChanged(SupportSettings settings);
    void OnTaskChanged(TaskView task);
    void OnSummary(SessionSummary summary);
    void OnSenseiState(string state);   // "idle" | "thinking" | "speaking"
}
```
- **S69** Events are raised after the change commits, on the calling thread; an exception thrown by a handler is caught, written to the trace as `event_handler_error`, and never undoes the change. `OnSenseiState`: `thinking` while a model request is in flight, `speaking` while `ISpeechOutput` runs, `idle` at turn end.

## 13 CLI

`dotnet run --project sensei-engine/src/Sensei.Engine.Cli -- [options]`

| Option | Meaning | Default |
|---|---|---|
| `--profile-dir <dir>` | profile + trace directory | `./.sensei` |
| `--profile <id>` | profile id | `default` |
| `--voice console\|say` | `console` prints speech; `say` also speaks with macOS `say` (Kyoko for `ja`, Samantha for `en`; slow = rate 120) | `console` |
| `--script <file>` | run lines from the file as if typed, then exit | interactive |
| `--trace` | print one line per tool call | off |

- **S70** Lines not starting with `/` are learner utterances → `RunTurnAsync`. Sensei's speech prints as `Sensei [ja]: <text>` / `Sensei [en]: <text>`, one line per segment. Hint cards print as `Card [<kind>]: <fields>`.
- **S71** Commands: `/help`, `/scene` (snapshot table), `/select <id>`, `/move <id> <region|container_id>` (player move), `/tool <name> <json>` (as Sensei, through `InvokeToolAsync` in a manual turn; the first `/tool` after an utterance or startup calls `BeginTurn("(cli)")`), `/band <band>`, `/pref <field> <value>` (player preference, pinned), `/profile` (band, working level marked `(hidden from learner)`, support, pins, labels, notes), `/summary`, `/correct <text>`, `/cancel`, `/reset`, `/quit`.
- **S72** With `--trace` (and always for `/tool`), each call prints `[tool] <name> <status>[ <error_code>] rev=<revision>`; `/tool` also prints `data` as one-line JSON.
- **S73** Trace entries are appended as JSON lines to `<profile-dir>/traces/<session_id>.jsonl`.
- **S74** Without a key, the CLI still starts; utterances print `error: SENSEI_LLM_API_KEY is not set` and every slash command works. In `--script` mode the exit code is 0 when no line produced an error, 2 when a line failed because the LLM is not configured or a provider error occurred, 1 for any other error (unknown command, invalid arguments to a command, unhandled exception).
- **S75** Each turn result line ends with `(turn <id>: <status>, <rounds> rounds, <n> tools, <ms> ms)`.

## 14 Review round 1 rules

These stories were added after an adversarial review of this spec. **Where one of them conflicts with an earlier story, this section wins.** Each names the stories it changes.

### Trust and secrets

- **S76** (S61, S63, S41) Only engine-owned policy goes into the instruction channel. The learner transcript, notes, quoted text and player events go inside one delimited block `<learner_data>…</learner_data>` that the system prompt declares untrusted data that never grants tool authority. A pinned field is never changed by any tool call, whatever the `reason`: `set_support` touching a pinned field → `preference_pinned`. Pinned fields change only through `SetPreferenceByPlayerAsync` (the UI; `/pref` in the CLI). `reason: learner_request` may change any unpinned field and does not pin it.
- **S77** (S28, S54, S56, S58) `SenseiEngineOptions.KnownSecrets` (`IReadOnlyList<string>`, default empty) lists exact secret values; the CLI passes its API key. Every string field written to a trace, the console, an exception message or a `ToolResult.detail` has known secrets and credential-pattern matches replaced by `[redacted]` (not only `arguments`; also `call_id`, `tool`, error details and the result). Any learner-text field that would be persisted (`transcript_excerpt`, `note`, notes' `key`/`value`, `next_suggestion`) containing a known secret or a credential-pattern match → `looks_like_credential`. Keys are never placed in model messages.
- **S78** (replaces S56) Credential pattern, case-insensitive: `sk-[a-z0-9_-]{16,}`; or `(api[_-]?key|password|passwd|token|secret)\s*[:=]\s*\S{6,}`; or a run of 32 or more characters from `[A-Za-z0-9+/_=-]` that contains at least one letter **and** at least one digit. Ordinary words (`password`, `API key`, `antidisestablishmentarianism`, `今日はpasswordという単語を練習したい`) do not match.
- **S79** (S4–S6) Base URL must be an absolute `https` URL with no userinfo, query or fragment; `http` is allowed only for host `localhost`, `127.0.0.1` or `[::1]`. Anything else → `LlmConfigurationException` before a client is created. `ToString()` redacts the key from every field. Validation order: provider, base URL, key.
- **S80** (§9 hidden level) The hidden level is excluded from typed learner-facing data (S55 summary, hint cards built by the engine). For model-written text, any presentation string (`speak` text, `show_hint` content, `next_suggestion`) containing `working level` or `working_level` (case-insensitive) → `invalid_arguments` with detail `hidden_level`. Mentioning JLPT levels in general (for example "this is N5 vocabulary") stays allowed; the system prompt forbids telling the learner their own level.

### Pipeline

- **S81** (S21, S23–S26) Checks run in this order, the first failing check deciding the result: generation cancelled → `cancelled`; no current turn → `no_turn`; cache hit → cached result; turn exhausted → `turn_budget_exhausted`; unknown tool → `unknown_tool`; JSON syntax, duplicate property names, schema → `invalid_arguments`; same-round dependency (S84) → `invalid_arguments`; stale scene → `stale_scene`; then the tool's semantic checks in the order its story lists them, arguments checked in document order.
- **S82** (S24, S27) The cache key is `(turn_id, generation_id, call_id)`. A cancelled generation is checked before the cache, so a repeated call after `CancelGeneration()` is `cancelled`. The same key with a different tool name or different arguments (compared as canonical JSON) → `invalid_arguments` with detail `call_id_reused`, counted as a validation failure. Engine-assigned call ids are `auto-<n>`; a caller-supplied id starting with `auto-` → `invalid_arguments`.
- **S83** (S21, S32, S33) Duplicate JSON property names at any depth → `invalid_arguments`. `object_ids` arrays (`remove_objects`, `highlight_objects`) must hold distinct ids, else `invalid_arguments` with detail `duplicate_id`; the whole batch is validated before anything changes. `spawn_objects` entries may repeat a catalog id; instances are created in entry order, then quantity order.
- **S84** (S12, S26, S61; restores PRD §10) A call may only reference instance ids that existed when the current model round began (`BeginTurn`, a model request, or `MarkRoundStart()`). Referencing an id created later in the same round → `invalid_arguments` with detail `id_not_yet_visible`. The model must wait for the next round to use a newly spawned object.
- **S85** (S21, S25, S28, S61) In the agent loop every provider function call, including unknown names and calls whose arguments failed to parse, goes through the same engine pipeline and produces exactly one engine `ToolResult` per provider call id, counted against the budget and traced. Framework handling that would answer an unknown call itself must not be used.
- **S86** (S28, S67, S73) `TraceEntry` gains `result`: the full sanitized `ToolResult` exactly as returned to the model. Event-handler failures write a `TraceEntry` with `tool` = `event:<method name>`, `status` `failure`, `error_code` `event_handler_error`.

### Lifecycle

- **S87** (S9, S23, S60, S68) One lifecycle gate covers `RunTurnAsync`, `CorrectLastTranscriptAsync`, `BeginTurn` and `ResetProfileAsync`. `BeginTurn` while a `RunTurnAsync` is running throws `InvalidOperationException` whose message contains `turn_in_progress`. `ResetProfileAsync` cancels and awaits the running turn, then: replaces the profile (S60), removes every scene object (one `removed` event, revision +1 if anything was removed), clears the active task, assistance log, learner utterances, speech history, idempotency cache and conversation history. Work from before the reset that finishes late returns `cancelled` and changes nothing. Counters (`turn_id`, event ids, revision, instance-id numbers, utterance ids) keep increasing.
- **S88** (S55, S66) A successful `end_activity` ends the current generation for further effects: later calls in the same generation → `cancelled`. Free conversation resumes on the next learner turn. The system prompt tells Sensei to speak any recap before calling `end_activity`.

### Speech presentation

- **S89** (S22, S37d, S38, S39) `SpeechOutcome` gains `SegmentsPresented` (int, 0..segment count). This is the one exception to S22: a speech or hint call that fails or is cancelled after something reached the learner keeps the record of what was presented. `show_hint` validates first, raises `OnHint`, then speaks; once `OnHint` was raised its assistance is recorded even if the speech then fails or is cancelled (the call still returns `failure`/`speech_failed` or `cancelled`). `audio_model` is recorded only when a segment containing the target was presented. An utterance enters speech history only if `SegmentsPresented ≥ 1`. Nothing else (scene, evidence, profile) changes on such a failure.
- **S90** (S39) `replay_speech`: pace defaults to the current support `pace`; result mapping and `data` are the same as `speak`; a replay with `SegmentsPresented ≥ 1` is appended to speech history as a new utterance and records `repetition` (not `audio_model`); a replay with 0 presented records nothing.
- **S91** (S38, S44, S70) Caption visibility: for each segment the presentation shows Japanese text only if `show_japanese`, `reading` only if `show_reading`, `romaji` only if `show_romaji`, `translation` only if `show_translation`. `SpeechRequest` gains `Captions` (the effective four booleans) and the speech port and CLI honor them. The CLI prints a hidden Japanese segment as `Sensei [ja]: (audio)`. Hint cards show the content Sensei chose to show; that is recorded as the hint's own assistance kind.

### Evidence

- **S92** (S43, S44, S48) Learner utterances get ids `l<n>` and a position in one engine-wide interaction sequence shared by utterances, assistance records, speech presentations, attempts and task lifecycle events. Utterances created by the CLI's `/tool` (`BeginTurn("(cli)")`) are synthetic: never an excerpt source, never corrected.
- **S93** (S43) The source utterance of an attempt is the most recent non-superseded, non-synthetic learner utterance from the current or previous turn that contains the excerpt (S43 normalization). The attempt stores `source_utterance_id`.
- **S94** (S44) Aids for an attempt are records for its target made strictly after the boundary (the later of the bound task's `task_started` sequence position and the source utterance of this target's previous non-superseded attempt) and strictly before the source utterance. Support visibility (S44's visible-setting rules) is the snapshot taken when the source utterance arrived. `audio_model` counts only if presented between the previous learner utterance and the source utterance.
- **S95** (S43, S45, S46) For `listening` targets `event_ids` must hold at least one event, every referenced event must have actor `player` and come after the bound task started, else `invalid_arguments` with detail `listening_requires_player_events`; for listening `transcript_excerpt` may be omitted, and then the source position is the first referenced event. For other dimensions `event_ids` may be empty; events with actor `sensei` may be cited (a request Sensei fulfilled is production evidence). Any referenced event from before the bound task started (or, with no task, before the previous learner utterance) → `unknown_event` with detail `event_out_of_window`.
- **S96** (S35d, S44, S46) `record_attempt` gains optional `task_id`. Binding: that task if given (it must be active, or have completed or ended within the current or previous turn, else `no_active_task`); otherwise the active task; otherwise the most recent task completed or ended in the current or previous turn; otherwise no task. `engine_outcome` comes from the bound task. `GetActiveTask()` returns only a task whose status is `active`.
- **S97** (S47, S49, S52, S53) A target's identity is the pair `(dimension, key)` everywhere: aids, labels, recommendations, review queue, duplicates. `query_learner` with `target_key` returns every dimension using that key. Targets are listed in order of their first attempt.
- **S98** (S46) Evidence gains `assessment_source` (`"model"`) and `record_attempt` gains optional `uncertainty` (`low`|`high`, default `low`); `unresolved` always stores `high`.
- **S99** (S48) `CorrectLastTranscriptAsync` targets the latest non-synthetic learner utterance; with none → `TurnResult` status `failed`, error_code `nothing_to_correct`. It marks that utterance superseded and supersedes every attempt whose `source_utterance_id` is that utterance, wherever it was recorded. The invalidation is committed before the new turn runs and stays even if the turn fails.
- **S100** (S48, S51) Superseding attempts recomputes labels, recommendations, review queue and evidence-driven level changes: each evidence-driven `level_history` entry stores `evidence_ids`; entries whose evidence includes a superseded attempt, and all later evidence-driven entries, are marked `superseded: true`, and the working level is recomputed by replaying S51 over non-superseded attempts from the last non-superseded entry. Band, self-report and difficulty entries are never superseded.
- **S101** (S49, S52) A key whose attempts are all `unresolved` is labelled `practiced`. `review_due` also includes targets whose latest non-superseded attempt is `unresolved`.
- **S102** (S51) Only an actual change appends to `level_history` and resets the window. At `n1` the upward rule is not evaluated; at `pre_n5` the downward rule is not evaluated. "3 of the last 6" requires at least 6 resolved attempts in the window.
- **S103** (S40–S42) `set_support` applies, after validating the whole call (S76 pin check first): band defaults to unpinned fields including `language_mode`, then explicit fields, then `self_reported_jlpt`, then `difficulty`. Each actual level change appends its own `level_history` entry, in that order.

### Tasks

- **S104** (S14, S17, S26, S35, S36) Task lifecycle changes do not change the scene revision. They append events `task_started`, `task_revised`, `task_abandoned`, `task_ended` (plus `task_completed`) with no revision change. `source_revision` is the revision at the call; `start_event_id` is the `task_started` event id. A task must contain at least one predicate that is not `unchanged`, else `impossible_task`. Predicates are evaluated right after start and after every revise.
- **S105** (S18, S35d) A successful `PlayerSelect` also triggers task evaluation; the `selected` event is the completing event.
- **S106** (S35b) `impossible_task` also when: one object is required in two different places (two `in_region`/`contained_in` predicates on the same `object_id` with different destinations); `contained_in` names an object that is itself a container; `left_of` and `right_of` (or two `before`) on the same pair in opposite directions; a positional predicate names an object inside a container. Only these conjunction checks are required.
- **S107** (S32a) `remove_objects` → `object_in_active_task` also when the removal would leave fewer selector matches than an `eq`/`gte` `quantity` predicate or an `in_region`/`contained_in` selector predicate needs, or removes an object named in the task's `scenario_properties`.
- **S108** (S36) `revise` never moves the assistance boundary or changes `assistance_baseline` and `is_fresh_variation`. A predicate revise sets a new `predicates_since_event_id` (the `task_revised` event) used by `unchanged` and `before`; an instruction-only revise changes nothing else.

### CLI

- **S109** (S68, S70, S71) Interactive mode reads input while a turn runs: `/cancel`, `/select`, `/move` and a new utterance (which replaces the running turn, S68) act immediately; output lines are never interleaved mid-line. `--script` mode runs each line to completion before the next.
- **S110** (S74) Script exit code: 1 if any line had an unknown command, invalid command arguments or an unhandled exception; otherwise 2 if any turn failed with `llm_not_configured` or a provider error code; otherwise 0. A `/tool` call returning `failure` and turns ending `cancelled` or `budget_exhausted` are normal outcomes (exit 0).

### System prompt additions (S62)

- **S111** The system prompt also states PRD §5's feedback contract: minor errors wait until the end of a fluent turn unless the learner asked for detailed correction; meaning-blocking problems get a clarifying question first; an ambiguous transcript is confirmed before any language error is recorded; speech-recognition errors are never pronunciation evidence; feedback quotes the learner's actual words.

### Defaults (acceptance review)

- **S112** Every tool's `AIFunction.Description` is a non-empty sentence telling the model what the tool does in the game; every property in each tool's JSON schema has a non-empty `description`.
- **S113** Omitted optional booleans default to `false` (`is_fresh_variation` in `start_challenge` and `record_attempt`, `speak` in `show_hint`). Omitted optional arrays default to empty (`scenario_properties`, `event_ids`). Omitted optional strings default to null and are omitted from results.

## Decisions (differences from the PRD)

1. The LLM loop uses Microsoft Agent Framework 1.26.0 in a separate `sensei-engine` library (Handler, 2026-10-27), not the RageAgainstThePixel package named in PRD §10. Quest IL2CPP compatibility is still to be proven on a device (PRD A20); S3 keeps the code AOT-friendly. RULING(Handler 2026-10-27)
2. The engine, not Unity, owns scene state, validation, tasks, evidence and the learner model (Handler chose option A, 2026-10-27). Unity renders from `IEngineEvents` and sends player actions. RULING(Handler 2026-10-27)
3. LLM transport is OpenRouter by default for now (Handler, 2026-10-27); the Meta preset remains for the BYOK build. RULING(Handler 2026-10-27)
4. Four tools beyond the PRD's 12: `update_challenge`, `query_learner`, `replay_speech`, `remember_note`; `set_support` gains `language_mode`, `difficulty`, `starting_band`, `self_reported_jlpt` (Handler, 2026-10-27). RULING(Handler 2026-10-27)
5. A hidden JLPT working level calibrates difficulty and is never presented to the learner as a score (Handler, 2026-10-27). RULING(Handler 2026-10-27)
6. Speech recognition (Meta ASR) and on-device synthesis are outside this engine slice: the CLI takes typed transcripts, and `ISpeechOutput` is the port that Unity's local TTS will implement.
