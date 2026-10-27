## 10b Voice synthesis on the headset

*Split from Section 10 Technical architecture.*


Select Supertonic 3 INT8 with sherpa-onnx as the preferred P0 synthesis implementation. It supports Japanese and English in one model family, and sherpa publishes Japanese examples, C# bindings, and Android ARM64 packages. These establish a credible integration path, not a completed Quest benchmark. Recognition and LLM reasoning stay on Meta; only synthesis moves on-device. \[S8, S9\]

Use the converted archive sherpa-onnx-supertonic-3-tts-int8-2026-05-11. Bundle its four ONNX graphs, tts.json, unicode_indexer.bin, voice.bin, and the compatible native runtime. The published converted files total roughly 140 MiB before native libraries; installed storage and peak working memory are separate measurements. Choose one of the ten supplied voice presets and use that preset for both languages. No voice cloning or online voice-builder dependency is required. \[S8, S15\]

### KittenTTS alternatives

Retain KittenTTS as an explicit on-device option behind ISpeechOutputProvider. Evaluate its two model families separately and freeze one working configuration by the day-one integration gate; the hackathon does not require shipping multiple synthesis engines. The newer KittenTTS 2 is a different architecture from the lightweight 0.8 ONNX models. \[S23\]

| **Option** | **Language and model footprint** | **Role in this prototype** |
| --- | --- | --- |
| Supertonic 3 INT8 | Japanese and English; roughly 140 MiB of converted model assets before native libraries. | Preferred baseline through sherpa-onnx; actual Quest performance and teaching quality still require proof. |
| KittenTTS 0.8 Nano INT8 | English model; 15 million parameters and approximately 25 MB for the published checkpoint. | Lightweight English voice option, paired with a separately validated local Japanese engine; cannot satisfy bilingual speech alone. |
| KittenTTS 2 | The current README advertises Japanese and English; 1.7 billion parameters with a 506 MiB smaller-weight option. | Experimental bilingual candidate; Japanese delivery and a complete native Quest integration must be proven before selection. |

The footprint figures describe published model assets, not installed APK size or peak memory. For KittenTTS 0.8, evaluate sherpa's converted kitten-nano-en-v0_8-int8 archive with matching voice data, tokens, and text frontend. Pin the complete adapter: the sherpa 2.0 transition removed bundled espeak-ng and changed phonemization, so older examples cannot be mixed with an arbitrary latest runtime. Treat English-only synthesis as a partial component, and route ja segments to a proven local Japanese engine. A dual-engine configuration must preserve clear, consistent character delivery and pass the same bilingual acceptance tests. \[S24, S25, S28\]

KittenTTS 2 remains a research candidate because its current documentation is inconsistent: the main README advertises 20 languages including Japanese, while the detailed voice guide describes ten languages and supplies no Japanese preset. That guide also warns that its text normalizer is English-oriented. Verify the exact checkpoint, Japanese conditioning, text handling, and audible output before treating the advertised language as implemented in our game. \[S23, S27\]

The KittenTTS 2 native route uses the author's C++ fork with GGML and a CPU LibTorch waveform decoder. Its C++ guide describes a 1.03 GB GGUF plus decoder and voice assets, so the smaller Python-weight figure is not a complete native deployment estimate. The documented native interface is a batch CLI; a Unity Android ARM64 library bridge and playback lifecycle remain project work. It is not interchangeable with sherpa's Kitten ONNX support. Bundle all selected assets, avoid the hosted Kitten API, and apply the same offline synthesis, render responsiveness, interruption, and latency gates. \[S23, S26\]

Keep model and runtime licenses separate: the KittenTTS application code is Apache 2.0, the selected Nano 0.8 INT8 model card declares Apache 2.0, and KittenTTS 2 uses the separately published Stellon Labs Community License. Preserve the exact terms for the selected checkpoint. Voice cloning and expression controls are optional and do not expand P0 scope. \[S23, S25\]

### Unity integration

Implement a small LocalSpeechOutputProvider behind the existing ISpeechOutputProvider interface. Start with matching sherpa C# bindings and the ARM64 libraries from the official v1.13.8 Android release archive; its release workflow enables the C API. Verify native exports and dependencies in the Unity device build. A thin Java or Kotlin bridge remains an alternative if the chosen package exposes only JNI or direct interop proves unreliable. Unity Android native-plugin support is established, but the complete IL2CPP integration still needs a device proof. \[S9, S16, S17, S29\]

Freeze a mutually compatible sherpa, bindings, model, and ONNX Runtime set after the day-one test. Supertonic 3 support begins in sherpa 1.13.2; 1.13.8 with its corresponding runtime is a candidate baseline, not a requirement to use an untested version. The Android build script defaults the C API off, so a direct P/Invoke build must explicitly enable the C API, TTS, and shared libraries. Do not assume an older Unity wrapper exposes Supertonic configuration. \[S18, S19\]

- Package models with the app and extract them to persistentDataPath when a native real file path is required; do not download voice assets during a learning session.

- Load only the selected provider configuration and keep its required models warm; do not eagerly load optional engines. Use one serial synthesis worker; start with two CPU threads and profile rendering before increasing parallelism.

- Generate short sentence segments after a complete speak call has passed validation. Marshal only ready audio and playback state to Unity's main thread; never block rendering on inference.

- Use each generated buffer's reported sample rate when creating the AudioClip; sample-rate values differ across documentation and builds, so do not hardcode one.

- Use callback-free generation initially if that is the reliable IL2CPP path. Interrupt playback immediately, clear queued segments, and reject late buffers using generation IDs. A CancellationToken does not by itself stop native computation; retain the native instance until its work finishes.

- If native cancellation is needed, use a supported callback boundary with a static AOT-compatible callback and retained delegate. Do not pass capturing managed callbacks to native code without proving IL2CPP compatibility.

- Cache repeated vocabulary clips by text, language, voice, pace, and model version; novel answers must still be synthesized locally. Bound the cache and pending queue.

### Japanese and English delivery

SpeakArgs contains an ordered list of text segments, each tagged ja or en, with a bounded pace choice and matching display text. For the preferred Supertonic configuration, synthesize the English explanation and Japanese example separately using the same preset. Do not assume automatic language switching inside one segment. Japanese synthesis receives Japanese script rather than displayed romaji. Validate counters, particles, readings, long vowels, small kana, and the slow setting with the exact voice and quantized build used for the demo.

Initial schema caps are six segments per speak call, 60 Unicode characters per Japanese segment, 160 per English segment, and 480 spoken characters in total per learner turn, including spoken hints. Reject oversized arguments for repair instead of truncating them silently. Tune these caps from the device benchmark; bounded segments limit the time an obsolete native job can occupy the worker after interruption.

Local synthesis requires no API key. Muse Voice Transcribe remains transcription-only, and the public MMS release does not list Japanese TTS. Neither supplies this on-device Japanese synthesis path. Android's TextToSpeech API depends on installed engines and voices; the product must not assume Quest has Google speech services or Japanese voice data. A system-engine check is optional diagnostics, not a launch dependency. \[S2, S3, S14, S20\]

### Device proof and fallback

On day one, install a clean Android IL2CPP build on Quest 3. With networking disabled, synthesize newly entered Japanese and English phrases through the same speak handler, repeat more slowly, and interrupt playback. Then reconnect and prove the full Meta recognition and tool loop with local speech. Measure cold load, warm synthesis delay, peak memory, frame timing, and stale-result handling. Full conversation still needs Meta connectivity; the offline gate tests synthesis only.

If Supertonic fails this gate, first fix the bridge or tune sentence length, thread count, and quality steps without weakening intelligibility. Evaluate the documented KittenTTS options only within the remaining integration budget. A bounded local fallback investigation may also use the documented Android OpenJTalk and Flite implementation for Japanese and English. Every selected configuration must pass the same teaching checks. Do not silently switch to hosted synthesis or replace live speech with prerecorded dialogue. Report an unmet local voice gate if no local configuration is acceptable. \[S21, S23\]

The upstream Supertonic repository is archived. Pin the downloaded model files and runtime, retain provenance, and do not depend on the discontinued hosted demos or Voice Builder. The sample code is MIT and the model weights are OpenRAIL-M; keep the actual model terms and notices with the app and distinguish them from the runtime's license. \[S8, S22\]

---
[PRD index](README.md)
