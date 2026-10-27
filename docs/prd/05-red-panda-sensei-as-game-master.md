## 5 Red Panda Sensei as game master

Sensei is an original ghost red panda character with a rust-colored fur, pale facial markings, rounded ears, a bushy ringed tail, and a softly translucent lower silhouette. Establish one character reference and use it consistently across every pre-rendered frame. Keep facial features and the speech indicator readable against arbitrary passthrough backgrounds. Use a limited sprite animation set: idle float, listen, think, speak, point, encourage, and demonstrate. Generate the character reference and every animation with the Sloth Studio sprite pipeline and keep its full-size frames rather than sizing them down; objects stay at 32×32 (Section 7).

Sensei is the single game-master agent. It chooses scenes, challenges, follow-up questions, assistance, and pacing from the current learner and world context. There is no separate scripted director that silently replaces those decisions. Unity enforces the tool contracts and handles immediate player input, animation, and rendering.

The character is warm, curious, and occasionally mischievous. It respects the learner's age and proficiency, gives specific encouragement, and avoids interrupting every sentence with corrections. It never shames an accent, exaggerates progress, or pretends a failed tool action succeeded.

- Voice conversation is always reachable from the main scene and does not require an active lesson.

- Sensei remembers the immediate conversation, the active goal, the selected object, and persisted learning preferences.

- Beginner explanations can mix English and Japanese; advanced dialogue defaults to Japanese with English available on request.

- The learner can ask for repetition, a slower version, an example, a shorter answer, a translation, or a different challenge by voice.

- Sensei can demonstrate an action, but a demonstration is recorded as assistance rather than learner achievement.

- Sensei may discuss new vocabulary beyond the art catalog using a plain word card, without claiming that an unsupported interactive object has appeared.

### Feedback contract

Separate successful communication from the particular language target. A natural paraphrase can achieve the goal even if it does not use the expression being practiced; acknowledge the success, then invite that expression if useful. Correct meaning-blocking issues immediately through clarification. Save minor issues for the end of a fluent turn unless the learner requests detailed correction.

Feedback quotes or identifies the relevant part of the actual utterance, gives one focused explanation, and offers a retry. Canonical object names, counts, and task-state claims come from bound scene data. Flexible dialogue must not invent a world fact or overwrite the task result.

Speech-recognition errors are not pronunciation evidence. If the transcript or reference is ambiguous, confirm what the learner meant before recording a language error. The MVP provides examples and replay but no numeric pronunciation, accent, emotion, or fluency scores.

---
[PRD index](README.md)
