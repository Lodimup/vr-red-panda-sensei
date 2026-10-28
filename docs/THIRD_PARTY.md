# Third-party components

Versions below are the checked-in pins or the resolved dependencies used by this checkout. Model weights and code can have different terms; consult the linked full notices before redistribution.

| Component | Version | Licence | Use and source |
|---|---|---|---|
| Microsoft Agent Framework (`Microsoft.Agents.AI`, `Microsoft.Agents.AI.OpenAI`) | 1.26.0 | MIT | Agent and tool loop. [Source](https://github.com/microsoft/agent-framework) |
| Microsoft.Extensions.AI | 10.10.0 | MIT | Chat abstractions and integration. [Source](https://github.com/dotnet/extensions) |
| OpenAI .NET | 2.14.0 | MIT | OpenAI-compatible client for OpenRouter. [Source](https://github.com/openai/openai-dotnet) |
| System.ClientModel | 1.15.0 | MIT | Client transport. [Source](https://github.com/Azure/azure-sdk-for-net/tree/main/sdk/core/System.ClientModel) |
| System.Text.Json | 10.0.12 | MIT | JSON serialization. [Source](https://github.com/dotnet/runtime) |
| sherpa-onnx | 1.13.8 | Apache-2.0 | Native speech runtime and bindings. [Notice](../unity/packages/com.supercapybara.sherpa-onnx/NOTICE.md), [licence](../unity/packages/com.supercapybara.sherpa-onnx/LICENSE), [source](https://github.com/k2-fsa/sherpa-onnx/tree/v1.13.8) |
| ONNX Runtime | 1.28.2 | MIT | Model inference. [Notice](../unity/packages/com.supercapybara.sherpa-onnx/NOTICE.md), [source](https://github.com/microsoft/onnxruntime/tree/v1.28.2) |
| Supertonic 3 INT8 | 2026-05-11 conversion | OpenRAIL-M weights; MIT sample code | On-device TTS. [Model card](https://huggingface.co/Supertone/supertonic-3), [terms](../unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md) |
| SenseVoice CTC / Fun-ASR-Nano-2512 INT8 | 2025-12-17 conversion | Apache-2.0 | On-device ASR encoder. [Model card](https://huggingface.co/FunAudioLLM/Fun-ASR-Nano-2512), [terms](../unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md) |
| SenseVoiceSmall INT8 | 2025-09-09 conversion | FunASR Model Open Source License Agreement | Comparison model, not fetched for the app. [Model card](https://huggingface.co/FunAudioLLM/SenseVoiceSmall), [terms](../unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md) |
| Noto Sans JP Regular | 2.004 | SIL OFL 1.1 | Japanese text. [Notice](../unity/THIRD_PARTY.md), [licence](../unity/NOTO-SANS-JP-LICENSE.txt), [release](https://github.com/notofonts/noto-cjk/releases/tag/Sans2.004) |
| Unity OpenXR | 1.19.0 | Unity Companion License | XR integration. [Package](https://docs.unity3d.com/Packages/com.unity.xr.openxr@1.19/manual/index.html) |
| Meta XR Core SDK | 207.0.1 | Meta XR SDK License | Quest integration. [SDK](https://developers.meta.com/horizon/downloads/package/meta-xr-core-sdk/) |
| TextMeshPro / Unity UI | 2.6.0 (`com.unity.ugui`) | Unity Companion License | Text and UI. [Package](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/index.html) |
| Mono.Cecil | 0.11.6 | MIT | Build-time assembly identity rewriting. [Source](https://github.com/jbevain/cecil) |

`sync-engine.sh` copies the engine's published runtime dependencies and renames assembly identities that collide with Unity's copies, including System.Text.Json; namespaces and types remain unchanged. See [Unity's attribution](../unity/THIRD_PARTY.md) and the [speech model terms](../unity/packages/com.supercapybara.sensei.voice/THIRD_PARTY.md) for full notices and model archive hashes.
