## 11 Game master and tool contracts

### Authority and event flow

Red Panda Sensei decides what teaching event to initiate and expresses that decision as a tool call. Unity is the executor and source of truth. Natural-language claims never mutate the scene, start a challenge, reveal a hint, or award progress by themselves. Successful JSON parsing is not authorization to bypass game rules. \[S4, S5\]

Every Sensei-directed effect uses a registered tool, including speech and character gestures. Direct player actions use the same underlying game-action handlers with actor=player, so they produce consistent evidence without waiting for an LLM response. Head tracking, continuous dragging, animation playback, and listening indicators remain immediate local runtime behavior; the model is not called every frame.

| **Record** | **Required fields** |
| --- | --- |
| LearnerProfile | Version; starting preference; help language; text visibility; correction style; skill evidence; review queue; recent goals. |
| ObjectInstance | Instance ID; catalog ID; pose relative to practice area; container ID; selectable state. |
| TaskSpec | Task ID; activity family; skill targets; source scene revision; existing object IDs; instruction; bounded success predicates; assistance baseline. |
| ToolCallEnvelope | Provider tool-call ID; registered name; typed arguments; local session, turn, and generation IDs; expected revision; idempotency key; actor. |
| ToolResult | Call ID; success, failure, or cancelled status; actual affected IDs; resulting revision; relevant events; error code and corrective detail when needed. |
| AttemptEvidence | Attempt ID; target; transcript excerpt; actual events and actor; engine outcome; language assessment and source; assistance; uncertainty; rule version. |
| SessionSummary | Practiced targets; supported and independent outcomes; unresolved items; next suggestion; user preference changes. |

### P0 game master tools

Tool names below are the implementation contract. Each has a concrete C# argument class, an explicit JSON schema, a validator, and an asynchronous handler. The tools can operate only on bundled assets, declared UI elements, and recorded session state.

| **Tool** | **What happens in the game** | **Validation or result** |
| --- | --- | --- |
| inspect_scene | Read the current objects, selection, active task, and assistance state. | Return authoritative IDs and revision; no mutation. |
| spawn_objects | Place one or more pre-rendered catalog objects in the practice area. | Validate catalog, capacity, and bounds; return engine-assigned instance IDs. |
| move_object | Move, hand over, or place an existing object in a declared container or region. | Validate object and destination; return the completed position and actor. |
| remove_objects | Clear specified objects to prepare another activity. | Reject removal that silently invalidates an active task; return removed IDs. |
| highlight_objects | Point out specified objects with an approved visual cue. | Validate IDs and duration; record assistance when the cue supplies a target answer. |
| animate_sensei | Play a bundled gesture or expression, optionally directed at an object. | Accept only the pre-rendered animation catalog and valid targets. |
| speak | Synthesize language-tagged text on the headset and show matching captions. | Return completed, cancelled, or failed playback; factual confirmations require successful action results. |
| start_challenge | Activate a new contextual language task with an instruction and measurable goals. | Validate TaskSpec, achievable predicates, existing objects, and learner support before activation. |
| show_hint | Show or speak a partial phrase, model answer, explanation, or vocabulary card. | Record the supplied assistance; cards use ordinary text and never generate object art. |
| set_support | Change allowed translation, reading, romaji, pace, or correction settings. | Respect explicit player preferences and record the current assistance state. |
| record_attempt | Submit an evidence-backed assessment of the learner's latest attempt. | Require event references; local code derives engine outcome and assistance and commits once; no direct mastery award. |
| end_activity | Present an evidence-based recap and a suggested next goal. | Bind progress claims to committed evidence; free conversation stays available. |

The speak handler owns speech playback; show_hint delegates any spoken hint to that same handler and records the help supplied. Listening, thinking, and speaking sprite states follow the actual local capture, request, and playback lifecycle. Sensei can request additional expressive gestures through animate_sensei.

### Typed validation and repair

Use classes such as SpawnObjectsArgs, MoveObjectArgs, SpeakArgs, StartChallengeArgs, and RecordAttemptArgs. Keep enums and numeric bounds explicit. Do not deserialize model-selected arbitrary .NET types or discover callable methods from unrestricted reflection. A typed object still needs checks such as object existence, an allowed destination, asset availability, and an achievable objective.

Supported task predicates are identity, approved property, quantity, within-region, left or right in the task frame, contained-in, unchanged, and before or after in the event log. Limit each P0 task to three primary predicates. A valid spoken paraphrase can map to the same action; successful execution does not by itself prove correct grammar or fluency.

For an invalid call, return a structured error such as unknown_object, unsupported_asset, stale_scene, invalid_arguments, or impossible_task, with only the context needed for repair. Sensei may correct the call once within the turn budget. No mutation occurs for rejected calls, and the model cannot bypass validation by repeating a command in prose.

Diagnostic traces record tool name, sanitized arguments, actual result, timing, and scene revision. They exclude credentials and raw audio. A compact developer overlay may show these events during rehearsal so technical execution is demonstrable without exposing private settings.

### Example tool driven interaction

1. The learner says, 'I know no Japanese.' Sensei calls spawn_objects for a water item; Unity creates water_1 from the bundled sprite and returns its ID.

2. Sensei calls highlight_objects for water_1 and speak with an English explanation and a Japanese example. Unity synthesizes and plays the audio locally and records the assistance.

3. The learner asks an unexpected question. Sensei answers through speak, preserving the scene and conversation context.

4. The learner makes a request meaning 'Water, please.' Sensei calls move_object for water_1 with the hand_over action; Unity executes it and returns the actual result.

5. Only after that result may Sensei speak a delivery confirmation. record_attempt references the real utterance and events; local rules classify whether help was used.

6. Sensei introduces tea with spawn_objects and activates a fresh task through start_challenge. A new variation can reduce assistance while preserving the learner's chosen controls.

For an advanced learner, the same tool surface supports a richer scenario: Sensei arranges catalog items, proposes a choice with competing constraints, discusses the learner's reasoning in Japanese, and changes the scene when the conversation establishes a valid action. The language and decisions adapt; the engine executes the same tested tools.

---
[PRD index](README.md)
