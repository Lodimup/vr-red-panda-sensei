# Red Panda Sensei

**Tagline:** Practice Japanese with a red panda and a tabletop in your own room.

## 150-word description

Red Panda Sensei brings Japanese practice into your room on Quest. A red panda greets you beside a tabletop in passthrough and helps you talk about cardboard objects. Start with no Japanese: tell Sensei what you know, listen to an explanation, and ask for an apple in Japanese. When the request succeeds, the apple moves to you and the task card shows completion.

Hold a button to speak, interrupt a response to ask a question, or use Hint and Repeat when you need support. Captions accompany speech. A menu offers starting levels and a session summary, and saved progress lets Sensei welcome you back on a visit.

Speech recognition and speech synthesis run on the headset. Recognized text and game context are sent to Muse Spark through an OpenRouter-compatible endpoint. Conversation requires an internet connection and a user-provided key. Response times vary, and mixed Japanese-English recognition remains a weak point.

## 400-word description

Red Panda Sensei is a mixed-reality Japanese practice companion for Quest. In passthrough, a ghostly red panda appears beside a tabletop of cardboard standees. The learner can talk to Sensei, ask for help, and use Japanese to affect the practice scene. The room remains visible, and no physical props are needed. Learners can select and move the cards with their controllers, alongside the spoken exchanges that guide each practice session.

A first session begins with Sensei greeting the learner. Someone who knows no Japanese can say so in English and ask to learn a word with an object. Sensei can introduce an apple and set a task: ask for it in Japanese. The learner holds A to speak, says 「りんごをください」, and releases the button. When the request succeeds, the apple moves to the learner and the task card marks the task complete. This connects a spoken phrase to a visible consequence.

The conversation is not a fixed script. The learner can ask a follow-up question or interrupt Sensei by holding A while it is speaking. X requests a hint; B repeats the latest speech. Captions accompany Sensei's voice. The menu exposes starting levels and a session summary, including supported and independent practice counts. Progress is saved locally, and a returning learner can be greeted with 「おかえりなさい」.

The experience uses Quest 3 passthrough, Meta XR SDK, and Muse Spark via the OpenRouter-compatible endpoint. The Unity application manages the scene, controller input, captions, local speech recognition and local speech synthesis. Recognized text and game context are sent to the model provider; model responses drive game-master tool calls, while the app and engine maintain the scene and task state. Artwork is bundled as standees rather than generated during play.

The demo is a supported practice experience, not a claim of measured language learning or pronunciation accuracy. Device turns have taken approximately 10–25 seconds; network and model conditions affect latency. Mixed Japanese-English utterances are a weak point for recognition, and the voice test corpus used synthetic speech rather than establishing performance across real speakers and environments. Internet access and an OpenRouter key are required for conversation. The key must currently be provisioned from a computer; there is no in-headset key entry.

To try the build, download the APK from the release, install it on a developer-enabled Quest, provision your key, and launch the app. The README provides the exact commands and controller guide.

## Meta technology

Quest 3 passthrough, Meta XR SDK, Muse Spark via the OpenRouter-compatible endpoint.

## Built with

Unity, Unity OpenXR, Meta XR Core SDK, Microsoft Agent Framework, Microsoft.Extensions.AI, OpenAI .NET, System.ClientModel, System.Text.Json, sherpa-onnx, ONNX Runtime, Supertonic 3, SenseVoice CTC / Fun-ASR-Nano-2512, Noto Sans JP, TextMeshPro / Unity UI, and Mono.Cecil. SenseVoiceSmall is a comparison model, not fetched for the app. Versions, licences and notices: [third-party components](THIRD_PARTY.md).

## On-device and cloud

On the headset: scene, controller input, artwork, speech recognition and synthesis, and saved learner progress. In the cloud: Muse Spark receives recognized text and game context through the OpenRouter-compatible endpoint. A connection and user-provided key are required.

## Try it

Download the APK from the [GitHub release](https://github.com/Lodimup/vr-red-panda-sensei/releases/tag/v0.1.0), then follow the [README's Try it instructions](../README.md#try-it) for installation and key provisioning.
