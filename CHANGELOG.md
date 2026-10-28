# Changelog

## v0.1.1

- Beginners receive a first small task.
- Fixed duplicate resume and chunk-failure logging.

- Queued repeat is cancelled when pausing.
- Sensei starts speaking sooner by generating voice sentence by sentence. Median time to first word on device fell from 10.2 seconds to about 9.2 seconds in the measured runs.
- Turn timing logs capture latency across the conversation pipeline.

## v0.1.0

- Practice Japanese with a mixed-reality red panda companion and interactive tabletop objects on Quest.
- Talk using push-to-talk, hear local speech synthesis, and use captions, hints, repeat, and saved learner progress.
- Speech recognition and synthesis run on the headset; conversation text and context go to the configured LLM provider.

### Known limits

- The voice test corpus uses synthetic speech; it does not establish performance across real speakers and environments.
- Mixed Japanese-English speech recognition is weak.
- Device turns have taken approximately 10–25 seconds, depending on the turn and network.
- There is no in-VR API key entry; provision the key from a computer.
