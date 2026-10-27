## 13 Acceptance scenarios and release gates

The scenarios below define required behavior. They are tests to perform during implementation; no passing results are claimed by this document.

| **Gate** | **Scenario** | **Required result** |
| --- | --- | --- |
| A01 | A new learner starts in an empty room and says they know no Japanese. | Spoken English onboarding, one grounded Japanese example, a supported request, and a later fresh attempt all work without kana knowledge. |
| A02 | The learner asks an unanticipated relevant question and then a contextual follow-up. | Sensei returns audible contextual answers and preserves the conversation; a fixed question menu is insufficient. |
| A03 | The learner uses a valid paraphrase or a single meaningful word at the appropriate level. | Communication succeeds where the meaning is clear; evidence records the actual language used rather than an exact-match failure. |
| A04 | A beginner asks for a hint, then completes the task. | The result is supported success, never independent production. |
| A05 | The scene is rearranged and the target highlight is removed. | Sensei presents a valid variation and the engine checks the selected object or final arrangement. |
| A06 | An intermediate learner clarifies a two-step instruction and proposes an alternative. | Sensei resolves the clarification in context and accepts a valid alternative without resetting the dialogue. |
| A07 | An advanced learner requests Japanese-only discussion and a register change. | The conversation begins at that level, responds to the learner's reasoning, and provides a contextual reformulation. |
| A08 | Recognition is ambiguous or conflicts with an object action. | Sensei clarifies; no false pronunciation judgment or confirmed learning failure is recorded. |
| A09 | The model references an unavailable sprite or proposes an impossible task. | Validation blocks the proposal and obtains a valid alternative or clear explanation; no runtime art generation occurs. |
| A10 | The learner interrupts, moves an object during processing, or retries a request. | Old audio stops, stale commands are rejected, and actions and evidence remain idempotent. |
| A11 | The app closes and restarts after practice. | Preferences, evidence, and review targets are restored; the practice area can be placed again. |
| A12 | The user performs a live Japanese and English voice conversation on Quest. | Meta performs recognition and reasoning remotely; the Quest synthesizes intelligible Japanese and English responses locally with one Meta credential. |
| A13 | The bundled local synthesis route reads target phrases and spontaneous answers. | The spoken wording agrees with the teaching text, is intelligible in both languages, and passes the documented latency and deployment check. |
| A14 | A translation, transcript, or model answer remains visible during a relevant attempt. | The result is classified using the target-specific assistance rules, even if the target object is not highlighted. |
| A15 | A spoken request causes a successful object delivery. | The record separately identifies engine-verified delivery and model-assessed language; success does not automatically certify the utterance. |
| A16 | A clean Quest install receives the player's single Meta credential. | Direct Meta API calls work without an application server or app account; speech synthesis, history, and learner state are local and require no second key. |
| A17 | A key is invalid, rate-limited, replaced, or cleared. | The app shows a specific recoverable state; replacement or clearing cancels outstanding requests and preserves progress. |
| A18 | Sensei initiates a scene change, spoken reply, hint, and new challenge. | Each effect has a registered call and actual result; descriptive model text alone produces no game mutation. |
| A19 | A tool has invalid arguments or names an unavailable asset. | No mutation occurs; a structured error allows one bounded repair; unsupported actions never bypass the registry. |
| A20 | The pinned C# client and tool registry run in a Quest Android IL2CPP build. | Meta credentials, argument parsing, action dispatch, real tool-result continuation, cancellation, and credential redaction work on-device. |
| A21 | A clean install with networking disabled receives novel Japanese and English text through the speak handler. | Both languages are synthesized audibly from bundled assets without a TTS key, runtime download, or preinstalled system voice; no TTS network request occurs. |
| A22 | Local speech generation runs while the user interacts, then the user interrupts and starts another turn. | The scene stays responsive, playback stops within 300 ms, stale buffers never play, and native objects remain valid until active work ends. |

### Content validation

Review the launch vocabulary, readings, counters, first-session phrases, and advanced sample scenarios with a competent Japanese reviewer before recording the final demo. Include valid paraphrases, ambiguous references, self-corrections, particles, quantities, and negative or ordered instructions in the sample set. An automated language check can assist but cannot be described as human review.

Run the zero-knowledge usability path with people who do not know Japanese and the advanced path with a competent speaker when testers are available. If external testing is unavailable, record that limitation rather than claiming learner validation. The three-minute demonstration can show observed behavior; it cannot establish long-term learning efficacy.

### Minimum release decision

The build is ready for submission only when all P0 functional requirements and acceptance scenarios are satisfied on the primary device, the dynamic voice-output route is working, and the source and demo accurately distinguish implemented behavior from future scope. Cosmetic polish cannot compensate for a silent Sensei, a broken zero-knowledge path, or advanced mode being a renamed beginner exercise.

---
[PRD index](README.md)
