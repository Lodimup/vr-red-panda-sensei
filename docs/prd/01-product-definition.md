## 1 Product definition

The learner can speak with Sensei at any time in English, Japanese, or a mixture of both, request help, and use Japanese to affect the visible practice scene. Sensei changes its language, explanations, and challenges according to the learner's goals and observed ability. A small object catalog supports progressively richer communication rather than a succession of expensive environments.

**Product promise.** A learner with no Japanese can understand the first interaction and make a supported spoken request; an experienced learner can immediately begin substantive Japanese conversation. Both can make progress across sessions.

The LLM is central to interpreting unscripted speech, resolving references, answering follow-up questions, adapting explanations, accepting valid paraphrases, and creating appropriate next challenges. Every Sensei-directed effect passes through the tool registry, including scene setup, speech, gestures, hints, and challenge changes. The game engine remains responsible for object facts, allowed actions, and recorded task outcomes.

### Fixed requirements

- Meta Quest 3 is the primary acceptance device; Quest 3S is the secondary compatibility target.

- Passthrough remains visible. Sensei uses pre-rendered sprite frames and learning objects use realistic high-resolution images shown as flat cardboard cutouts, all bundled with the app; text panels are rendered normally.

- Object variants and Sensei animation frames are prepared before release. The LLM composes scenes from this catalog and never generates or redraws object art during play.

- The complete experience works in an empty room or above an empty table, without physical props, an automatic room scan, or a modeled 3D environment.

- Ghost red panda Sensei supports real two-way voice conversation. Subtitles supplement speech; they do not replace the required spoken experience.

- Every starting level is accessible in the hackathon build, including zero knowledge and advanced Japanese conversation.

- The hackathon uses BYOK with direct provider calls from Unity; conversation and progression are local, and no application server is required.

- Red Panda Sensei is the LLM game master; every Sensei-directed game effect is an explicit tool call executed and acknowledged by Unity.

- Muse recognition and reasoning are remote; Japanese and English speech synthesis runs on the headset. Bundle the synthesis runtime and voices, require no TTS credential, and verify actual Quest performance on day one.

- The first playable release is a narrow, polished vertical slice that fits the official hackathon build period. Content breadth can grow later.

### Scope boundaries

| **P0 required for submission** | **P1 after P0 is reliable** | **Outside the hackathon release** |
| --- | --- | --- |
| One Sensei; live Japanese and English speech; 12 object types; free conversation; three activity families; all practice bands; local progress; controller interaction; BYOK; typed game-master tools. | Hand tracking; extra object packs; additional explanation languages; one small cosmetic system. | Multiplayer; full 3D worlds; household-object recognition; unrestricted live art generation; a complete JLPT syllabus; automated pronunciation scores; monetization; application backend; self-hosted inference servers; hosted TTS; local LLM inference. |

P0 denotes a release requirement, not a separate development project for every story. P1 features are cut first if time is tight. Voice, zero-knowledge support, advanced access, scene grounding, and persistent progression cannot be traded away for extra content.

---
[PRD index](README.md)
