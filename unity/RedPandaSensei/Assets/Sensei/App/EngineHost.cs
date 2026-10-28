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
using Sensei.Unity;
using TMPro;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    public sealed class EngineHost : MonoBehaviour
    {
        private readonly MainThreadQueue queue = new MainThreadQueue(); private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private SenseiEngine? engine; private PracticeAreaView area = null!; private SenseiView sensei = null!; private TMP_Text? label; private bool ready; private bool slow; private bool changingPace; private bool hasKey;
        private void Start()
        {
            var rig = FindFirstObjectByType<OVRCameraRig>();
            if (rig == null) { Debug.LogError("RPS_BOOT step=1 fail missing camera rig"); return; }
            var obj = new GameObject("Practice area"); area = obj.AddComponent<PracticeAreaView>(); area.Initialize(rig.centerEyeAnchor, rig.rightHandAnchor);
            sensei = obj.AddComponent<SenseiView>(); sensei.Initialize(rig.centerEyeAnchor, area);
            label = FindFirstObjectByType<TMP_Text>();
            var controller = gameObject.AddComponent<ControllerInput>(); controller.Initialize(this, area, rig.rightHandAnchor);
            var path = Application.persistentDataPath; _ = BootAsync(path);
        }
        private void Report(string text) { Debug.Log(text); if (label != null) label.text = text; }
        private async Task BootAsync(string path)
        {
            try
            {
                var env = SenseiEnvFile.Load(Path.Combine(path, "sensei.env")); env.TryGetValue("SENSEI_LLM_API_KEY", out var key); env.TryGetValue("RPS_DEBUG_TEXT", out var debugText);
                Report("RPS_BOOT step=1 ok");
                for (var i = 2; i <= 4; i++) Report($"RPS_BOOT step={i} ok voice_deferred");
                var events = new UnityEngineEvents(queue, area, sensei, pace => slow = pace == "slow");
                var created = await Task.Run(async () =>
                {
                    var client = string.IsNullOrWhiteSpace(key) ? null : ChatClientFactory.Create(LlmSettings.FromEnvironment(env));
                    return await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = client, Speech = new LogSpeechOutput(debugText == "1", key), Events = events, ProfileStore = new FileProfileStore(Path.Combine(path, "profiles")), ProfileId = "default", KnownSecrets = string.IsNullOrEmpty(key) ? Array.Empty<string>() : new[] { key } }, lifetime.Token);
                });
                engine = created; hasKey = !string.IsNullOrWhiteSpace(key); Report("RPS_BOOT step=5 ok"); area.Place(); var snapshot = await Task.Run(() => created.GetScene()); area.Render(snapshot); slow = snapshot.Support.Pace == "slow"; Report("RPS_BOOT step=6 ok"); ready = true;
                if (string.IsNullOrWhiteSpace(key)) { Report("No API key. Push sensei.env (see README)."); return; }
                if (await EventTurn("session_start")) Report("RPS_BOOT step=7 ok");
                else Report("RPS_BOOT step=7 fail");
            }
            catch (Exception error) { Report($"RPS_BOOT fail type={error.GetType().Name}"); }
        }
        public async Task<bool> EventTurn(string kind)
        {
            if (!ready || engine == null) return false;
            if (!hasKey) { Report("No API key. Push sensei.env (see README)."); return false; }
            try
            {
                var watch = Stopwatch.StartNew(); var result = await Task.Run(() => engine.RunPlayerEventTurnAsync(kind, lifetime.Token));
                var tools = string.Join(",", result.ToolCalls.Select(t => t.Tool)); Report($"RPS_TURN status={result.Status} ms={watch.ElapsedMilliseconds} tools={tools}"); var message = TurnMessages.ForTurn(result.Status, result.ErrorCode); if (message != null) Debug.Log("RPS_ERROR " + message);
                return result.Status == "completed";
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception error) { Debug.LogError("RPS_TURN failed type=" + error.GetType().Name); return false; }
        }
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
        private async void OnDestroy() { lifetime.Cancel(); if (engine != null) await engine.DisposeAsync(); lifetime.Dispose(); }
    }
}
