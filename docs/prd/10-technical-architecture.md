## 10 Technical architecture

Use Unity 6 and C# with a mutually supported, pinned Unity OpenXR and Meta XR package set. Meta XR Core supplies passthrough and the camera rig; Meta XR Interaction SDK supplies the controller interaction baseline. Use a spatial scene containing flat sprite planes and world-space text, with the real room visible through passthrough. Verify the Android IL2CPP build on the actual Quest on day one. \[S13\]

### Direct BYOK topology

The installed Quest app is the only application we deploy. The player supplies one Meta Model API key. Unity calls Meta over HTTPS, optionally using WebSocket for streaming recognition. Supertonic 3 performs Japanese and English synthesis locally through sherpa-onnx. Conversation history, prompts, tool orchestration, scene validation, and learner persistence are client responsibilities. Application servers, hosted databases, login services, self-hosted inference servers, and hosted TTS are outside scope.

| **Component** | **Responsibility** |
| --- | --- |
| Quest presentation | Passthrough, pre-rendered sprites, controller input, captions, microphone capture, and playback of locally generated speech. |
| SenseiAgent in C# | Compose context, send model requests, read tool calls, validate arguments, dispatch allowed tools, append actual results, and stop or continue the turn. |
| Unity game state | Authoritative scene, player events, challenge predicates, assistance state, and evidence; all mutations pass through registered handlers. |
| Meta hosted dialogue | Muse Spark selects game-master tools and their arguments from the conversation, scene facts, learner profile, and allowed schemas. |
| Meta hosted recognition | Muse Voice Transcribe converts the captured Japanese, English, or mixed utterance to text. |
| Local speech output | Bundled Supertonic 3 INT8 models run through sherpa-onnx on the Quest CPU and return PCM for Unity playback. |
| Local persistence | Versioned preferences, learning evidence, review targets, and compact recovery state; provider credentials are not stored in these files. |

### C sharp library decision

Use the Unity-specific OpenAI-Unity package by RageAgainstThePixel as the initial chat and tool-call transport, behind an IModelClient interface, with a small typed SenseiAgent loop owned by this project. The package documents Unity support, custom API domains, tool calls, and JSON schemas. Configure the Meta API host and Muse Spark model explicitly; the package name does not select an OpenAI model or require routing our requests to OpenAI. Compatibility with Meta and the Quest build must be proven in the day-one integration test. \[S4, S6, S12\]

This supplies the PydanticAI-style behavior we need through a compact composition: typed C# argument classes; explicit JSON tool schemas; validation before execution; structured success or error results; a bounded repair retry; and local conversation state. The Unity library is a client SDK, not a complete PydanticAI-equivalent agent framework. SenseiAgent supplies the orchestration and game-specific validation.

Configure the HTTP client with new OpenAISettings(domain: "api.meta.ai", apiVersion: "v1") and pass the player's Meta key unchanged. The domain contains neither a URL scheme nor a /v1 path. Do not add an OpenAI key prefix. Verify the final request host before sending. Use a dedicated Meta adapter for ASR: the package's custom HTTP domain setting does not establish a compatible Meta realtime WebSocket route. \[S6, S12\]

Microsoft Agent Framework is the general .NET agent-framework alternative, succeeding Semantic Kernel and AutoGen. It is not a required dependency for this Unity build. A .NET target alone does not prove Android IL2CPP behavior; avoid making the hackathon depend on unverified framework, reflection, or serialization paths. \[S10, S11\]

- Register the small tool catalog explicitly in C#; use checked-in JSON schemas or generate them in the editor before building.

- Deserialize arguments to concrete classes, reject missing fields and invalid values, and run semantic checks against current game state.

- Preserve any serialized or indirectly referenced types needed by IL2CPP stripping; verify them in a device build, not only in the Unity Editor.

- Dispatch Unity scene mutations on the main thread. Network requests remain asynchronous and cancellable.

- Use the library only for the compatible chat/tool surface. Meta ASR has a dedicated network adapter; ISpeechOutputProvider invokes the bundled local TTS runtime. Do not route synthesis through the chat SDK.

- If the chosen client rejects Meta credentials, emits incompatible fields, or blocks the Quest build, replace only IModelClient with a small UnityWebRequest adapter to the documented Meta endpoint. Preserve the typed tools and SenseiAgent loop.

### Provider calls and credentials

| **Function** | **Endpoint or adapter** | **Credential** |
| --- | --- | --- |
| Dialogue and tools | POST https://api.meta.ai/v1/chat/completions; pin the tested Muse Spark version. | Player's Meta Model API key in the bearer header. |
| P0 speech recognition | POST https://api.meta.ai/v1/asr/transcribe; upload a completed push-to-talk recording in a supported format. | The same Meta Model API key. |
| Optional live recognition | wss://api.meta.ai/v1/asr/realtime; implement Meta's documented streaming protocol only if measured latency justifies it. | Meta key in the first JSON authorization handshake; not the HTTP Authorization header. |
| Speech generation | Local ISpeechOutputProvider adapter to Supertonic 3 INT8 through sherpa-onnx; no HTTP endpoint. | None; runtime and voices are bundled. |

Meta documents bearer authentication for Model API calls and Japanese, English, and code-switching support in Muse Voice Transcribe. The file-transcription route keeps the P0 implementation simple. Streaming ASR uses its own audio encoding, real-time pacing, and handshake requirements; implement that protocol from Meta's documentation if selected. \[S3, S12\]

The setup UI has one masked Meta credential field, Test connection, Replace key, and Clear key, plus an independent Test local voice action. The key is memory-only and must be entered again after a full app restart. A future remember-key option requires an appropriate secure local store. Exclude keys from prompts, tool arguments, saved profiles, traces, recordings, and public source. Keep the Meta host fixed. Local synthesis neither reads a credential nor sends text to a network service.

Authentication, quota, and connectivity failures receive distinct actionable messages. Replacing or clearing a key cancels pending provider requests and queued Sensei effects; clearing credentials leaves learning progress intact. The tool registry has no credential-reading, arbitrary-network, file-execution, or code-evaluation tool.

### Runtime turn sequence

1. The learner presses talk. Unity stops old speech and queued actions, advances the generation ID, and captures the utterance locally.

2. Unity sends the completed recording directly to Muse Voice Transcribe with the player-supplied Meta key.

3. A completed transcript becomes a user turn. Attach the selected object, authoritative scene snapshot, active task, assistance state, and compact learner context.

4. SenseiAgent calls Muse Spark directly with the conversation and the allowed function-tool schemas. Sensei returns named tool calls with typed arguments.

5. The local dispatcher validates the call name, argument schema, current generation, relevant scene revision, catalog IDs, and task semantics.

6. Unity executes valid calls and returns one structured result per provider tool-call ID. Invalid calls return a specific error without changing game state.

7. Sensei receives the actual results and chooses the next tool or ends its turn. Spoken content uses the speak tool, which invokes local synthesis and plays the generated audio.

8. Validated record_attempt calls commit evidence through local rules; end_activity can present a summary and next suggestion. Save accepted progress locally.

There is one game-master agent. Conversation state is maintained in the app, with recent messages and a compact context summary. On restart, restore the learner record and clearly resume a new conversation from that record. The local agent loop is ordinary C# orchestration. LLM and recognition inference remain Meta-hosted; the synthesis model runs on the headset.

### Turn and interruption rules

- Unity attaches session_id, turn_id, generation_id, and the request's scene revision. Keep provider tool_call_id and a local idempotency key for every action.

- Calls from one response form an ordered batch rooted in its scene snapshot. Advance the execution revision after each committed call; if player input or another turn changes the scene, invalidate the remaining dependent calls and return fresh state.

- A call that needs a prior tool result must occur after that result reaches the model in a subsequent round. Never invent a future object ID or assume a previous action succeeded merely because its call appeared earlier in the same response.

- Pressing talk, clearing a key, or stopping an activity cancels the generation. Late audio and tool calls from that generation are discarded.

- Before the next model request, settle every tool-call ID already appended to history with its actual completed result or a cancelled result. Discarding stale payloads must not leave unresolved tool calls in outgoing conversation history.

- Replay a cached tool result for a duplicate call; never spawn, deliver, or score twice after a network retry.

- Use at most four model request rounds and eight tool calls per learner turn; allow one argument-repair attempt within that budget. Stop cleanly when the budget is exhausted.

- P0 microphone capture is controlled by push-to-talk and is inactive during Sensei playback until the learner explicitly interrupts.

- Preserve the activity and profile on failure. Captions can assist recovery but do not satisfy the required spoken experience.

---
[PRD index](README.md)
