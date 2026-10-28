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
        [SerializeField] private TMP_FontAsset? panelFont;
        private int finishedTurns; private int hintShownAt = -1; private string? logKey;
        private StatusPanel statusPanel = null!; private TaskCard taskCard = null!; private HintCard hintCard = null!; private MenuPanel menu = null!;
        public string ModelStatus { get; private set; } = "no key"; public string VoiceStatus => speech != null ? "ready" : "not loaded"; public string RecognizerStatus => recognizer != null ? "ready" : "not loaded";
        private UnityEngineEvents? engineEvents; private LocalTts? tts; private SenseVoiceRecognizer? recognizer; private LocalSpeechOutput? speech; private SpeechPlayer? player; private PushToTalk? pushToTalk; private CaptionPanel? captions; private bool turnBusy; private string? voiceFailure;

        private SenseiEngine? engine; private PracticeAreaView area = null!; private SenseiView sensei = null!; private TMP_Text? label; private bool ready; private bool slow; private bool changingPace; private bool hasKey; private bool destroyed;
        private void Start()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) { Debug.LogError("RPS_BOOT step=1 fail missing camera rig"); return; }
            var obj = new GameObject("Practice area"); area = obj.AddComponent<PracticeAreaView>(); area.Initialize(rig.centerEyeAnchor, rig.rightHandAnchor);
            sensei = obj.AddComponent<SenseiView>(); sensei.Initialize(rig.centerEyeAnchor, area);
            label = FindFirstObjectByType<TMP_Text>(); if (label != null) label.gameObject.SetActive(false);
            panelFont ??= Resources.Load<TMP_FontAsset>("Fonts/NotoSansJP");
            statusPanel = new GameObject("Status panel").AddComponent<StatusPanel>(); statusPanel.Initialize(rig.centerEyeAnchor, panelFont);
            taskCard = new GameObject("Task card").AddComponent<TaskCard>(); taskCard.Initialize(area.transform, panelFont);
            hintCard = new GameObject("Hint card").AddComponent<HintCard>(); hintCard.Initialize(area.transform, panelFont);
            menu = new GameObject("Menu panel").AddComponent<MenuPanel>(); menu.Initialize(rig.leftHandAnchor, rig.centerEyeAnchor, this, panelFont);
            var controller = gameObject.AddComponent<ControllerInput>(); controller.Initialize(this, area, rig.rightHandAnchor);
            var captionObject = new GameObject("Captions"); captionObject.transform.SetParent(area.transform, false); captions = captionObject.AddComponent<CaptionPanel>(); captions.Initialize(rig.centerEyeAnchor);
            var youObject = new GameObject("You said"); youObject.transform.SetParent(area.transform, false); var you = youObject.AddComponent<YouSaidLine>(); you.Initialize(rig.centerEyeAnchor);
            player = gameObject.AddComponent<SpeechPlayer>(); var bounce = gameObject.AddComponent<SpeechBounce>(); bounce.Initialize(player, sensei);
            var path = Application.persistentDataPath; _ = BootAsync(path, you);
        }
        private void Report(string text)
        {
            Debug.Log(LogRedactor.Redact(text, logKey));
            string display = text;
            if (text.StartsWith("RPS_BOOT step="))
            {
                var names = new[] { "", "Load settings", "Install voice models", "Load voice", "Load recognizer", "Load profile", "Place practice area", "Start session" };
                var step = 0;
                if (text.Length > 14 && int.TryParse(text.Substring(14, 1), out step) && step > 0 && step < names.Length)
                    display = names[step] + (text.Contains("voice_deferred") ? ": waiting for voice" : text.Contains("fail") ? ": failed" : ": ready");
                if (text.Contains("fail") && (step == 2 || step == 3 || step == 4)) {
                    var model = text.Contains("Supertonic") ? "Supertonic (supertonic3)" : text.Contains("SenseVoice") ? "SenseVoice-nano" : "voice model";
                    var reason = text.Split(' ').Last();
                    display = names[step] + ": " + model + " failed (" + reason + ")";
                }
            }
            statusPanel.Add(LogRedactor.Redact(display, logKey), text.Contains("fail") || text.StartsWith("No API key"));
        }
        private async Task BootAsync(string path, YouSaidLine you)

        {
            try
            {
                var env = SenseiEnvFile.Load(Path.Combine(path, "sensei.env")); env.TryGetValue("SENSEI_LLM_API_KEY", out var key); env.TryGetValue("RPS_DEBUG_TEXT", out var debugText); logKey = key; taskCard.SetKey(key); hintCard.SetKey(key); captions?.SetKey(key); you.SetKey(key);
                Report("RPS_BOOT step=1 ok");
                string? ttsDir = null, asrDir = null;
                try { ttsDir = await VoiceModelInstaller.EnsureInstalledAsync("supertonic3", "2026-05-11", lifetime.Token); }
                catch (Exception e) { Report("RPS_BOOT step=2 fail Supertonic " + e.GetType().Name); }
                try { asrDir = await VoiceModelInstaller.EnsureInstalledAsync("sensevoice-nano", "2025-12-17", lifetime.Token); }
                catch (Exception e) { Report("RPS_BOOT step=2 fail SenseVoice-nano " + e.GetType().Name); }
                if (ttsDir != null && asrDir != null) Report("RPS_BOOT step=2 ok");
                try { if (ttsDir == null) throw new IOException(); tts = await LocalTts.LoadAsync(ttsDir); await tts.SynthesizeAsync("あ", "ja", 1f, lifetime.Token); speech = new LocalSpeechOutput(tts, player!, queue); speech.SegmentStarted += segment => captions!.Show(segment, speech.CurrentCaptions); speech.UtteranceEnded += () => captions!.End(); Report("RPS_BOOT step=3 ok"); }
                catch (Exception e) { voiceFailure = "Supertonic voice could not be loaded."; Report("RPS_BOOT step=3 fail Supertonic " + e.GetType().Name); }
                if (destroyed) { tts?.Dispose(); return; }
                try { if (asrDir == null) throw new IOException(); recognizer = await SenseVoiceRecognizer.LoadAsync(asrDir, useItn: false); Report("RPS_BOOT step=4 ok"); }
                catch (Exception e) { voiceFailure = (voiceFailure == null ? "" : voiceFailure + "\n") + "SenseVoice-nano could not be loaded."; Report("RPS_BOOT step=4 fail SenseVoice-nano " + e.GetType().Name); }
                if (destroyed) { tts?.Dispose(); recognizer?.Dispose(); return; }
                env.TryGetValue("RPS_TEST_AUDIO", out var testAudio); env.TryGetValue("RPS_TEST_PTT_SECONDS", out var testSeconds); env.TryGetValue("RPS_TEST_INTERRUPT_MS", out var testInterrupt);
                pushToTalk = gameObject.AddComponent<PushToTalk>(); pushToTalk.Initialize(this, sensei, you, recognizer, testAudio, debugText == "1", key);
                var events = new UnityEngineEvents(queue, area, sensei, pace => slow = pace == "slow"); engineEvents = events;
                events.Hint = hint => { hintShownAt = finishedTurns; hintCard.Show(hint); }; events.Task = task => { hintCard.Clear(); taskCard.Show(task); }; events.Support = taskCard.SetSupport;
                var created = await Task.Run(async () =>
                {
                    var client = string.IsNullOrWhiteSpace(key) ? null : ChatClientFactory.Create(LlmSettings.FromEnvironment(env));
                    return await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = client, Speech = (ISpeechOutput?)speech ?? new UnavailableSpeechOutput(), Events = events, ProfileStore = new FileProfileStore(Path.Combine(path, "profiles")), ProfileId = "default", KnownSecrets = string.IsNullOrEmpty(key) ? Array.Empty<string>() : new[] { key } }, lifetime.Token);
                });
                if (destroyed) { await created.DisposeAsync(); return; }
                engine = created; hasKey = !string.IsNullOrWhiteSpace(key); ModelStatus = hasKey ? "ready" : "no key"; menu.RefreshStatus(); Report("RPS_BOOT step=5 ok");
                area.SetTestFrame(env.TryGetValue("RPS_TEST_FRAME", out var testFrame) && testFrame == "sensei"); area.Place(); var snapshot = await Task.Run(() => created.GetScene()); if (destroyed) return; area.Render(snapshot); slow = snapshot.Support.Pace == "slow"; taskCard.SetSupport(snapshot.Support); taskCard.Show(snapshot.ActiveTask); Report("RPS_BOOT step=6 ok"); ready = true;
                if (env.TryGetValue("RPS_TEST_MENU", out var testMenu) && testMenu == "1") menu.Toggle();
                if (string.IsNullOrWhiteSpace(key)) { Report("No API key. Push sensei.env (see README)." + (voiceFailure == null ? "" : "\n" + voiceFailure)); statusPanel.Complete(); return; }
                var greeting = EventTurn("session_start");
                if (float.TryParse(testSeconds, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) || !string.IsNullOrEmpty(testAudio) || !string.IsNullOrEmpty(testInterrupt)) { int.TryParse(testInterrupt, out var interruptMs); pushToTalk.StartTest(seconds, interruptMs); }
                if (await greeting) { Report("RPS_BOOT step=7 ok"); if (voiceFailure != null) Report(voiceFailure); statusPanel.Complete(); }
                else { Report("RPS_BOOT step=7 fail" + (voiceFailure == null ? "" : "\n" + voiceFailure)); return; }

                if (env.TryGetValue("RPS_TEST_TURN", out var testTurn) && !string.IsNullOrWhiteSpace(testTurn))
                {
                    Debug.Log("RPS_TEST_TURN sent");
                    var testResult = await Task.Run(() => created.RunTurnAsync(testTurn, lifetime.Token));
                    FinishTurn(testResult.Status, testResult.ErrorCode); Debug.Log($"RPS_TEST_TURN status={testResult.Status}");
                }
            }
            catch (Exception error) { if (!destroyed) Report($"RPS_BOOT fail type={error.GetType().Name}"); }
        }
        public async Task<bool> EventTurn(string kind)
        {
            if (!ready || engine == null) return false;
            if (!hasKey) { Report("No API key. Push sensei.env (see README)."); return false; }
            try
            {
                turnBusy = true; var watch = Stopwatch.StartNew(); var result = await Task.Run(() => engine.RunPlayerEventTurnAsync(kind, lifetime.Token));
                FinishTurn(result.Status, result.ErrorCode); var tools = string.Join(",", result.ToolCalls.Select(t => t.Tool)); Debug.Log(LogRedactor.Redact($"RPS_TURN status={result.Status} ms={watch.ElapsedMilliseconds} tools={tools}", logKey)); var message = TurnMessages.ForTurn(result.Status, result.ErrorCode); if (message != null) Debug.Log("RPS_ERROR " + message);

                return result.Status == "completed";
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { Debug.LogError("RPS_TURN failed type=" + error.GetType().Name); return false; }
            finally { turnBusy = false; }
        }
        private void FinishTurn(string status, string? code)
        {
            queue.Post(() => { finishedTurns++; if (hintShownAt >= 0 && finishedTurns > hintShownAt + 1) { hintCard.Clear(); hintShownAt = -1; } }); ModelStatus = code switch { "llm_not_configured" => "no key", "auth_failed" => "auth failed", "quota_exceeded" => "quota", "network_error" => "network", _ => hasKey ? "ready" : "no key" };
            menu.RefreshStatus(); var message = TurnMessages.ForTurn(status, code); if (message != null) captions?.ShowError(message);
        }
        public void ToggleMenu() => menu.Toggle();
        public async Task<bool> SetBand(string band) { var current = engine; if (current == null) return false; try { await Task.Run(() => current.SetStartingBandAsync(band, lifetime.Token)); return true; } catch (Exception e) { Debug.LogError("RPS_BAND failed type=" + e.GetType().Name); return false; } }
        public async Task<string> Summary() { var current = engine; if (current == null) return "Nothing practiced yet."; return LogRedactor.Redact(MenuState.FormatSummary(await Task.Run(() => current.GetSessionSummary())), logKey); }
        public async Task<bool> ResetProgress() { var current = engine; if (current == null) return false; try { var previous = await Task.Run(async () => { var active = current.GetActiveTask(); await current.ResetProfileAsync(lifetime.Token); return active; }); engineEvents?.ResetPanels(); taskCard.Reset(previous); hintCard.Clear(); return true; } catch (Exception e) { Debug.LogError("RPS_RESET failed type=" + e.GetType().Name); return false; } }

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
            try { var watch = Stopwatch.StartNew(); var result = await Task.Run(() => current.RunTurnAsync(transcript, lifetime.Token)); FinishTurn(result.Status, result.ErrorCode); Debug.Log(LogRedactor.Redact($"RPS_TURN status={result.Status} ms={watch.ElapsedMilliseconds} tools={string.Join(",", result.ToolCalls.Select(t => t.Tool))}", logKey)); var message = TurnMessages.ForTurn(result.Status, result.ErrorCode); if (message != null) Debug.Log("RPS_ERROR " + message); }
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
        private void OnApplicationQuit() => Shutdown();
        private void OnDestroy() => Shutdown();
        private async void Shutdown()
        {
            if (destroyed) return;
            destroyed = true; ready = false;
            var current = engine; engine = null;
            current?.CancelGeneration(); lifetime.Cancel(); speech?.Interrupt();
            if (current != null) await current.DisposeAsync();
            tts?.Dispose(); recognizer?.Dispose();
        }
    }
}
