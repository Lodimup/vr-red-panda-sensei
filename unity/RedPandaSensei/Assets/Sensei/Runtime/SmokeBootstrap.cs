#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using Sensei.Engine;
using Sensei.Engine.Llm;
using Sensei.Engine.Storage;
using TMPro;
using UnityEngine;

namespace Sensei.Unity
{
    public sealed class SmokeBootstrap : MonoBehaviour
    {
        [SerializeField] private TMP_Text label = null!;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private string? secret;

        private void Report(string message)
        {
            Debug.Log(message);
            if (label != null) label.text = message;
        }

        private async void Start()
        {
            try
            {
                Report("RPS_SMOKE starting");
                var env = SenseiEnvFile.Load(Path.Combine(Application.persistentDataPath, "sensei.env"));
                env.TryGetValue("SENSEI_LLM_API_KEY", out secret);
                var store = new FileProfileStore(Path.Combine(Application.persistentDataPath, "profiles"));
                var offline = await SenseiEngine.CreateAsync(new SenseiEngineOptions
                {
                    ChatClient = null, Speech = new LogSpeechOutput(), ProfileStore = store, ProfileId = "smoke"
                }, lifetime.Token);
                try
                {
                    offline.BeginTurn("smoke");
                    offline.MarkRoundStart();
                    var result = await offline.InvokeToolAsync("spawn_objects",
                        "{\"objects\":[{\"catalog_id\":\"apple\",\"region\":\"center\"}]}", ct: lifetime.Token);
                    Report($"RPS_SMOKE offline_tool status={result.Status} revision={result.Revision}");
                    if (!result.IsSuccess) throw new InvalidOperationException($"Offline tool: {result.ErrorCode} {result.Detail}");
                }
                finally { await offline.DisposeAsync(); }

                var settings = LlmSettings.FromEnvironment(env);
                Report($"RPS_SMOKE settings {settings}");
                using var client = ChatClientFactory.Create(settings);
                var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
                {
                    ChatClient = client, Speech = new LogSpeechOutput(), Events = new LogEngineEvents(),
                    ProfileStore = store, ProfileId = "smoke", KnownSecrets = secret == null ? Array.Empty<string>() : new[] { secret }
                }, lifetime.Token);
                try
                {
                    var turn = await engine.RunTurnAsync("Hello! I don't know any Japanese yet.", lifetime.Token);
                    Report($"RPS_SMOKE turn status={turn.Status} rounds={turn.Rounds} tool_calls={string.Join(",", turn.ToolCalls.Select(call => call.Tool))}");
                    if (turn.Status != TurnStatuses.Completed)
                        throw new InvalidOperationException($"Turn: {turn.ErrorCode} {turn.ErrorDetail}");
                    if (!turn.ToolCalls.Any(call => call.Tool == "speak") ||
                        !turn.Spoken.SelectMany(utterance => utterance.Segments).Any(segment => segment.Lang == "ja") ||
                        !turn.Spoken.SelectMany(utterance => utterance.Segments).Any(segment => segment.Lang == "en"))
                        throw new InvalidOperationException("Completed turn must speak Japanese and English.");
                    Report("RPS_SMOKE done");
                }
                finally { await engine.DisposeAsync(); }
            }
            catch (Exception error)
            {
                var detail = $"{error.GetType().FullName}: {error.Message}\n{error.StackTrace}";
                if (!string.IsNullOrEmpty(secret)) detail = detail.Replace(secret, "[redacted]");
                Report($"RPS_SMOKE error {detail}");
            }
        }

        private void OnDestroy() { lifetime.Cancel(); lifetime.Dispose(); }
    }
}
