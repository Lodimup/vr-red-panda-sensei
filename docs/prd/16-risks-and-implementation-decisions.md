## 16 Risks and implementation decisions

| **Risk or decision** | **Required response** |
| --- | --- |
| Local bilingual synthesis | Prove the preferred Supertonic 3 INT8 route on Quest on day one; keep KittenTTS as a documented alternative and select it only after the same Japanese and English, latency, memory, and frame-timing checks. |
| Model latency | Use short initial replies, bounded tool loops, cached stable audio, visible progress, and measured performance; preserve a responsive render loop. |
| Misrecognition | Provide transcript correction, confirmation, and retry; exclude unresolved input from skill judgments. |
| Hallucinated scene facts | Bind facts to catalog and state, validate proposed actions, and confirm completed effects from engine results. |
| False progress | Track assistance explicitly, use fresh variations, and require repeated evidence before stronger claims. |
| Advanced mode lacks depth | Test discussion, justification, negotiation, and register changes, not just long object instructions. |
| Too much content | Keep one Sensei and 12 object types; increase linguistic depth before expanding art or environments. |
| Novelty replaces usefulness | Keep every feature tied to conversation, learner agency, grounding, or progressive practice. |
| C# and device compatibility | Pin the client package and prove Meta tool calls in a Quest IL2CPP build; use the thin direct REST adapter if the package blocks integration. |
| BYOK setup | Test the single Meta key and bundled local voice independently; handle Meta authentication and quota errors, and preserve progress when the key changes. |
| External dependencies | Pin Meta models, local TTS graphs, native/runtime versions, and licenses; retain archived Supertonic provenance and keep credentials out of the build, logs, and public source. |

---
[PRD index](README.md)
