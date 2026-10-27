# Red Panda Sensei Product Requirements

*Progressive Japanese learning in your own environment*

Version 1.2 \| 26 October 2026 \| Meta AI Developer Hackathon

**Build decision.** We are building a single-player Meta Quest experience in which a ghost red panda Sensei teaches Japanese through genuine spoken conversation and interactive flat pixel objects placed in the learner's real surroundings. It starts at zero knowledge and adapts through advanced conversation. This document defines the product, one-sentence user stories, technical contracts, acceptance criteria, and the hackathon delivery plan.

**One Meta key and local speech synthesis.** The Quest app calls Meta directly for recognition and LLM reasoning using the player's key. Sensei's spoken replies are synthesized on the headset; Supertonic 3 INT8 through sherpa-onnx is the preferred baseline, with KittenTTS alternatives documented in Section 10. Conversation context, the C# agent loop, tool validation, game state, and learner progress also live in the app. We operate no application backend or speech server. The selected local synthesis route must pass the day-one Quest performance and pronunciation gate.

**Sensei is the game master.** Red Panda Sensei chooses the next teaching move and causes game effects through named, typed C# tools. Unity validates and executes those calls and returns what actually happened. The character, objects, and animation frames are all pre-rendered assets.

## Sections

- [01-product-definition](01-product-definition.md)
- [02-experience-and-game-loop](02-experience-and-game-loop.md)
- [03-learners-and-progressive-difficulty](03-learners-and-progressive-difficulty.md)
- [04-first-session-and-returning-sessions](04-first-session-and-returning-sessions.md)
- [05-red-panda-sensei-as-game-master](05-red-panda-sensei-as-game-master.md)
- [06-learner-model-and-progression](06-learner-model-and-progression.md)
- [07-world-and-interaction-requirements](07-world-and-interaction-requirements.md)
- [08-functional-requirements](08-functional-requirements.md)
- [09-one-sentence-user-stories](09-one-sentence-user-stories.md)
- [10-technical-architecture](10-technical-architecture.md)
- [10b-voice-synthesis-on-the-headset](10b-voice-synthesis-on-the-headset.md)
- [11-game-master-and-tool-contracts](11-game-master-and-tool-contracts.md)
- [12-reliability-and-performance-targets](12-reliability-and-performance-targets.md)
- [13-acceptance-scenarios-and-release-gates](13-acceptance-scenarios-and-release-gates.md)
- [14-delivery-plan](14-delivery-plan.md)
- [15-three-minute-demonstration](15-three-minute-demonstration.md)
- [16-risks-and-implementation-decisions](16-risks-and-implementation-decisions.md)
- [17-sources-and-technical-verification](17-sources-and-technical-verification.md)
