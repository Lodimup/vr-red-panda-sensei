## 17 Sources and technical verification

Technical references were checked on 26 October 2026. Product behaviors and numeric performance targets in this document are proposed requirements. They are not claims that an implementation has already passed testing.

**S1 Meta Global AI Developer Hackathon Participant Guide supplied by the user.** Sections 1, 3, 4, 7, and 8 establish the theme, schedule, new-work requirements, three-minute video, and equally weighted judging rubric.

Private copy supplied by the participant; not publicly linked.

**S2 Meta Model API model catalog.** Defines Muse Spark and Wonder, model input and output types, and the distinction between voice transcription and speech synthesis.

https://dev.meta.ai/docs/models

**S3 Meta speech to text documentation.** Documents Japanese and English transcription, code-switching, push-to-talk, and the absence of word-level timing and confidence scores.

https://dev.meta.ai/docs/speech-to-text

**S4 Meta tool calling documentation.** Defines the model-requested, application-executed tool loop.

https://dev.meta.ai/docs/tool-calling

**S5 Meta structured output documentation.** Documents schema-constrained output; application semantics still require validation.

https://dev.meta.ai/docs/structured-output

**S6 OpenAI Unity package by RageAgainstThePixel.** Documents Unity installation, custom API domains, function tools, and JSON schemas; Meta and Quest integration require an on-device proof.

https://github.com/RageAgainstThePixel/com.openai.unity

**S7 Official JLPT frequently asked questions.** Clarifies that JLPT results do not directly assess speaking, writing, or interaction.

https://www.jlpt.jp/sp/e/faq/

**S8 Supertonic 3 model card.** Documents local ONNX inference, Japanese and English support, approximately 99 million parameters, preset voices, and OpenRAIL-M model licensing.

https://huggingface.co/Supertone/supertonic-3

**S9 Sherpa Japanese Supertonic 3 examples.** Provides the exact converted model, Android ARM64 demo, and C#, Java, Kotlin, and native API examples; it is not a Quest benchmark.

https://k2-fsa.github.io/sherpa/onnx/tts/all/Japanese/supertonic-3-ja.html

**S10 Microsoft Agent Framework overview.** Describes the general .NET agent framework and its relationship to Semantic Kernel and AutoGen.

https://learn.microsoft.com/en-us/agent-framework/overview/

**S11 Unity scripting restrictions.** Explains ahead-of-time compilation and reflection or generic-code restrictions relevant to IL2CPP verification.

https://docs.unity.cn/6000.4/Documentation/Manual/scripting-restrictions.html

**S12 Meta Model API authentication.** Defines bearer-key authentication and the chat completions endpoint; the product uses player-supplied credentials.

https://dev.meta.ai/docs/authentication

**S13 Meta Unity and OpenXR compatibility.** Documents Unity OpenXR compatibility and the recommended plugin path.

https://developers.meta.com/vr/documentation/unity/unity-and-openxr-compatibility/

**S14 Android TextToSpeech API.** Documents installed-engine discovery and voice availability; it does not guarantee any particular Quest engine or Japanese voice.

https://developer.android.com/reference/android/speech/tts/TextToSpeech

**S15 Sherpa Supertonic 3 model release.** Lists the INT8 graph sizes and demonstrates English and Japanese synthesis with the same model package.

https://github.com/k2-fsa/sherpa-onnx/pull/3609

**S16 Sherpa C sharp offline TTS bindings.** Defines the native interop surface, GenerateWithConfig, callbacks, and returned audio metadata.

https://github.com/k2-fsa/sherpa-onnx/blob/master/scripts/dotnet/OfflineTts.cs

**S17 Unity Android native plugins.** Documents native plugin integration from Unity; the complete model integration still requires an IL2CPP device test.

https://docs.unity3d.com/6000.0/Documentation/Manual/AndroidNativePlugins.html

**S18 Sherpa release history.** Records Supertonic 3 support from 1.13.2 and runtime compatibility updates.

https://github.com/k2-fsa/sherpa-onnx/blob/master/CHANGELOG.md

**S19 Sherpa Android ARM64 build script.** Shows Android native build switches, including the C API setting and runtime dependency.

https://github.com/k2-fsa/sherpa-onnx/blob/master/build-android-arm64-v8a.sh

**S20 Meta MMS language coverage.** Lists Japanese recognition and language identification but no released Japanese TTS support.

https://dl.fbaipublicfiles.com/mms/misc/language_coverage_mms.html

**S21 Josee Android TTS implementation.** Documents offline Japanese OpenJTalk and English Flite with Android ARM64 support; a fallback integration reference, not a ready-made Unity package.

https://github.com/FairyDevicesRD/droid.josee.tts

**S22 Archived Supertonic source and model locations.** Documents the ended upstream support, preserved downloads, and separate code and model licenses.

https://github.com/supertone-oss-archive/supertonic

**S23 KittenTTS model families.** Distinguishes lightweight 0.8 ONNX models from KittenTTS 2, advertises the latter's Japanese support, and separates code and model licenses.

https://github.com/KittenML/KittenTTS

**S24 Sherpa KittenTTS model documentation.** Lists the converted English Kitten 0.8 ONNX packages and links their runtime examples; verify the exact supported version before bundling.

https://k2-fsa.github.io/sherpa/onnx/tts/kitten.html

**S25 KittenTTS Nano 0.8 INT8 model.** Lists the 15 million parameter checkpoint, approximate 25 MB model size, supplied voices, and Apache 2.0 license.

https://huggingface.co/KittenML/kitten-tts-nano-0.8-int8

**S26 KittenTTS 2 native runtime.** Documents the GGML and CPU LibTorch stack, native asset footprint, offline cache mode, and batch CLI interface.

https://github.com/KittenML/kitten-tts-2-cpp

**S27 KittenTTS 2 voices and expression.** Documents the narrower detailed language and preset coverage and English-oriented normalization caveat that must be resolved for Japanese.

https://github.com/KittenML/KittenTTS/blob/main/docs/voices-and-expression.md

**S28 Sherpa text frontend transition.** Documents removal of bundled espeak-ng and piper-phonemize and the changed phonemization contract.

https://github.com/k2-fsa/sherpa-onnx/pull/4055

**S29 Sherpa Android release packaging.** Shows the v1.13.8 release workflow enabling the C API and packaging native libraries by Android ABI.

https://github.com/k2-fsa/sherpa-onnx/blob/v1.13.8/.github/workflows/android.yaml

---
[PRD index](README.md)
