#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sensei.App.Core;
using Sensei.Engine;
using Sensei.Engine.Learner;
using Sensei.Engine.Llm;
using Sensei.Engine.Storage;
using Sensei.Engine.Speech;
using Sensei.Voice;
using Sensei.Unity;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    public sealed class EngineHost : MonoBehaviour
    {
        private readonly MainThreadQueue queue = new MainThreadQueue(); private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private LocalTts? tts; private SenseVoiceRecognizer? recognizer; private LocalSpeechOutput? speech; private SpeechPlayer? player; private PushToTalk? pushToTalk; private CaptionPanel? captions; private bool turnBusy; private string? voiceFailure;
        private SenseiEngine? engine; private PracticeAreaView area = null!; private SenseiView sensei = null!; private TMP_Text? label; private bool ready; private bool slow; private bool changingPace; private bool hasKey;
        private void Start()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) { Debug.LogError("RPS_BOOT step=1 fail missing camera rig"); return; }
            var obj = new GameObject("Practice area"); area = obj.AddComponent<PracticeAreaView>(); area.Initialize(rig.centerEyeAnchor, rig.rightHandAnchor);
            sensei = obj.AddComponent<SenseiView>(); sensei.Initialize(rig.centerEyeAnchor, area);
            label = FindFirstObjectByType<TMP_Text>();
            var controller = gameObject.AddComponent<ControllerInput>(); controller.Initialize(this, area, rig.rightHandAnchor);
            var captionObject = new GameObject("Captions"); captionObject.transform.SetParent(area.transform, false); captions = captionObject.AddComponent<CaptionPanel>(); captions.Initialize(rig.centerEyeAnchor);
            var youObject = new GameObject("You said"); youObject.transform.SetParent(area.transform, false); var you = youObject.AddComponent<YouSaidLine>(); you.Initialize(rig.centerEyeAnchor);
            player = gameObject.AddComponent<SpeechPlayer>(); var bounce = gameObject.AddComponent<SpeechBounce>(); bounce.Initialize(player, sensei);
            var path = Application.persistentDataPath; _ = BootAsync(path, you);
        }
        private void Report(string text) { Debug.Log(text); if (label != null) label.text = text; }
        private async Task BootAsync(string path, YouSaidLine you)
        {
            try
            {
                var env = SenseiEnvFile.Load(Path.Combine(path, "sensei.env")); env.TryGetValue("SENSEI_LLM_API_KEY", out var key); env.TryGetValue("RPS_DEBUG_TEXT", out var debugText);
                Report("RPS_BOOT step=1 ok");
                string? ttsDir = null, asrDir = null;
                try { ttsDir = await VoiceModelInstaller.EnsureInstalledAsync("supertonic3", "2026-05-11", lifetime.Token); asrDir = await VoiceModelInstaller.EnsureInstalledAsync("sensevoice-nano", "2025-12-17", lifetime.Token); Report("RPS_BOOT step=2 ok"); }
                catch (Exception e) { voiceFailure = "Voice models could not be installed."; Report("RPS_BOOT step=2 fail voice models " + e.GetType().Name); }
                try { if (ttsDir == null) throw new IOException(); tts = await LocalTts.LoadAsync(ttsDir); await tts.SynthesizeAsync("あ", "ja", 1f, lifetime.Token); speech = new LocalSpeechOutput(tts, player!, queue); speech.SegmentStarted += segment => captions!.Show(segment, speech.CurrentCaptions); speech.UtteranceEnded += () => captions!.End(); Report("RPS_BOOT step=3 ok"); }
                catch (Exception e) { voiceFailure = "Supertonic voice could not be loaded."; Report("RPS_BOOT step=3 fail Supertonic " + e.GetType().Name); }
                try { if (asrDir == null) throw new IOException(); recognizer = await SenseVoiceRecognizer.LoadAsync(asrDir, useItn: false); Report("RPS_BOOT step=4 ok"); }
                catch (Exception e) { voiceFailure = (voiceFailure == null ? "" : voiceFailure + "\n") + "SenseVoice-nano could not be loaded."; Report("RPS_BOOT step=4 fail SenseVoice-nano " + e.GetType().Name); }
                env.TryGetValue("RPS_TEST_AUDIO", out var testAudio); env.TryGetValue("RPS_TEST_PTT_SECONDS", out var testSeconds); env.TryGetValue("RPS_TEST_INTERRUPT_MS", out var testInterrupt);
                pushToTalk = gameObject.AddComponent<PushToTalk>(); pushToTalk.Initialize(this, sensei, you, recognizer, testAudio, debugText == "1", key);
                var events = new UnityEngineEvents(queue, area, sensei, pace => slow = pace == "slow");
                var created = await Task.Run(async () =>
                {
                    var client = string.IsNullOrWhiteSpace(key) ? null : ChatClientFactory.Create(LlmSettings.FromEnvironment(env));
                    return await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = client, Speech = (ISpeechOutput?)speech ?? new UnavailableSpeechOutput(), Events = events, ProfileStore = new FileProfileStore(Path.Combine(path, "profiles")), ProfileId = "default", KnownSecrets = string.IsNullOrEmpty(key) ? Array.Empty<string>() : new[] { key } }, lifetime.Token);
                });
                engine = created; hasKey = !string.IsNullOrWhiteSpace(key); Report("RPS_BOOT step=5 ok"); area.Place(); var snapshot = await Task.Run(() => created.GetScene()); area.Render(snapshot); slow = snapshot.Support.Pace == "slow"; Report("RPS_BOOT step=6 ok"); ready = true;
                if (string.IsNullOrWhiteSpace(key)) { Report("No API key. Push sensei.env (see README)." + (voiceFailure == null ? "" : "\n" + voiceFailure)); return; }
                var greeting = EventTurn("session_start");
                if (float.TryParse(testSeconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !string.IsNullOrEmpty(testAudio) || !string.IsNullOrEmpty(testInterrupt)) { int.TryParse(testInterrupt, out var interruptMs); pushToTalk.StartTest(seconds, interruptMs); }
                if (await greeting) { Report("RPS_BOOT step=7 ok"); if (voiceFailure != null) Report(voiceFailure); else if (label != null) label.gameObject.SetActive(false); }
                else { Report("RPS_BOOT step=7 fail" + (voiceFailure == null ? "" : "\n" + voiceFailure)); return; }
                if (env.TryGetValue("RPS_TEST_TURN", out var testTurn) && !string.IsNullOrWhiteSpace(testTurn))
                {
                    Debug.Log("RPS_TEST_TURN sent");
                    var testResult = await Task.Run(() => created.RunTurnAsync(testTurn, lifetime.Token));
                    Debug.Log($"RPS_TEST_TURN status={testResult.Status}");
                }
            }
            catch (Exception error) { Report($"RPS_BOOT fail type={error.GetType().Name}"); }
        }
        public async Task<bool> EventTurn(string kind)
        {
            if (!ready || engine == null) return false;
            if (!hasKey) { Report("No API key. Push sensei.env (see README)."); return false; }
            try
            {
                turnBusy = true; var watch = Stopwatch.StartNew(); var result = await Task.Run(() => engine.RunPlayerEventTurnAsync(kind, lifetime.Token));
                var tools = string.Join(",", result.ToolCalls.Select(t => t.Tool)); Debug.Log($"RPS_TURN status={result.Status} ms={watch.ElapsedMilliseconds} tools={tools}"); var message = TurnMessages.ForTurn(result.Status, result.ErrorCode); if (message != null) Debug.Log("RPS_ERROR " + message);
                return result.Status == "completed";
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { Debug.LogError("RPS_TURN failed type=" + error.GetType().Name); return false; }
            finally { turnBusy = false; }
        }
        public bool CanTalk => ready && hasKey;
        public bool VoiceIdle => CanTalk && !turnBusy && !(player?.IsPlaying ?? false);
        public bool SpeechPlaying => player?.IsPlaying ?? false;
        public void InterruptSpeech()
        {
            var watch = Stopwatch.StartNew(); var wasPlaying = SpeechPlaying;
            engine?.CancelGeneration(); speech?.Interrupt();
            Debug.Log($"RPS_INTERRUPT was_playing={wasPlaying.ToString().ToLowerInvariant()} stopped={(!SpeechPlaying).ToString().ToLowerInvariant()} stopped_ms={watch.ElapsedMilliseconds}");
        }
        public async Task RunTranscript(string transcript)
        {
            var current = engine; if (current == null || !hasKey) return;
            turnBusy = true;
            try { var watch = Stopwatch.StartNew(); var result = await Task.Run(() => current.RunTurnAsync(transcript, lifetime.Token)); Debug.Log($"RPS_TURN status={result.Status} ms={watch.ElapsedMilliseconds} tools={string.Join(",", result.ToolCalls.Select(t => t.Tool))}"); var message = TurnMessages.ForTurn(result.Status, result.ErrorCode); if (message != null) Debug.Log("RPS_ERROR " + message); }
            catch (OperationCanceledException) { }
            catch (Exception e) { Debug.LogError("RPS_TURN failed type=" + e.GetType().Name); }
            finally { turnBusy = false; }
        }
        private sealed class UnavailableSpeechOutput : ISpeechOutput { public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct) => Task.FromResult(new SpeechOutcome(SpeechStatuses.Failed, "voice unavailable", 0)); }
        public async void Select(string id)
        {
            var current = engine; if (current == null) return;
            try { var result = await Task.Run(() => current.PlayerSelect(id)); Debug.Log($"RPS_PLAYER select id={id} ok={result.Ok.ToString().ToLowerInvariant()}"); }
            catch (Exception e) { Debug.LogError("RPS_PLAYER select failed type=" + e.GetType().Name); }
        }
        public async void Move(string id, string? region, string? container, Action<bool> complete)
        {
            var current = engine; if (current == null) { complete(false); return; }
            try { var result = await Task.Run(() => current.PlayerMove(id, region, container)); Debug.Log($"RPS_PLAYER move id={id} ok={result.Ok.ToString().ToLowerInvariant()}"); complete(result.Ok); }
            catch (Exception e) { Debug.LogError("RPS_PLAYER move failed type=" + e.GetType().Name); complete(false); }
        }
        public void ReplaceArea() { if (!ready) return; area.Place(); _ = EventTurn("area_placed"); }
        public async void TogglePace()
        {
            var current = engine; if (current == null || changingPace) return;
            changingPace = true; var next = !slow;
            try { await Task.Run(() => current.SetPreferenceByPlayerAsync(new SupportPatch { Pace = next ? "slow" : "normal" }, lifetime.Token)); slow = next; }
            catch (Exception e) { Debug.LogError("RPS_PLAYER pace failed type=" + e.GetType().Name); }
            finally { changingPace = false; }
        }
        private void Update() => queue.Drain();
        private async void OnDestroy() { lifetime.Cancel(); if (engine != null) await engine.DisposeAsync(); tts?.Dispose(); recognizer?.Dispose(); lifetime.Dispose(); }
    }
}
