## 12 Reliability and performance targets

The figures below are engineering targets for the prototype, not measured performance or vendor guarantees. Record device, network, model, and audio configuration with every timing result.

| **Measure** | **Target and acceptance method** |
| --- | --- |
| Rendering | At least 72 frames per second on the primary Quest during active local synthesis; no main-thread waits for local or remote inference. |
| Local interaction | Selection, listening state, and grab feedback respond within 100 ms in the local input test. |
| Interruption | Pressing talk stops audible Sensei playback within 300 ms and prevents stale actions from committing. |
| Conversational delay | For short warm-path turns, aim for median first spoken response within 3 seconds and 95th percentile within 8 seconds after the user finishes. |
| Measured sample | Collect at least 30 complete short turns across Japanese, English, and mixed input; report actual timings and failures. |
| Local synthesis delay | For warmed short sentence segments, target median ready-to-play audio within 1 second and 95th percentile within 2 seconds; measure from validated text to a playable buffer, separately from Meta latency. |
| Local resource use | Record cold initialization, peak memory, installed model size, and a sustained-session frame trace; accept only a configuration that preserves the rendering and interruption targets. |
| Context and cost | Send a compact scene and learner summary plus recent turns; bound output and tool rounds; record reported usage locally and enforce per-turn request limits. |
| Recovery | A deliberate timeout, disconnect, duplicate response, or late reply does not corrupt the scene or saved progress. |
| Save behavior | Save accepted learning evidence promptly and verify restoration after an application restart. |

Aim for one or two model requests for an ordinary exchange, with the hard limit of four rounds and eight tool calls. Cache stable vocabulary audio where appropriate, but an unanticipated question must still produce a live spoken answer. Keep local usage counters and request bounds; show provider quota errors without claiming the app can know the account balance.

### Privacy and data behavior

Request microphone access with a clear recording indicator. Do not send raw passthrough camera images, since the application does not need them. Do not retain raw microphone recordings by default. Keep recent dialogue transient and persist only the excerpts and learning evidence needed for the local profile. Provide reset and deletion controls. Debug recordings require an explicit developer test setting and must be excluded from a public repository.

---
[PRD index](README.md)
