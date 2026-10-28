#nullable enable
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Sensei.App.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    public sealed class TestScriptDriver : MonoBehaviour
    {
        private EngineHost host = null!; private PushToTalk ptt = null!;
        public void Initialize(EngineHost h, PushToTalk talk, string path, string name)
        {
            host = h; ptt = talk; _ = Run(path, name);
        }
        private async Task<bool> Until(Func<bool> predicate, double seconds)
        {
            var watch = Stopwatch.StartNew();
            while (!predicate()) { if (watch.Elapsed.TotalSeconds >= seconds) return false; await Task.Yield(); }
            return true;
        }
        private async Task<bool> Wav(string name)
        {
            int before = host.TestTranscriptStarts; ptt.TestPress(name); await Task.Delay(500); ptt.TestRelease();
            return await Until(() => host.TestTranscriptStarts > before, 90);
        }
        private async Task Run(string path, string name)
        {
            try
            {
                if (Path.GetFileName(name) != name || name.Contains("\\")) throw new IOException("Invalid script name");
                var steps = TestScriptParser.Parse(File.ReadAllText(Path.Combine(path, name)));
                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i]; var args = step.Arguments; var watch = Stopwatch.StartNew(); bool ok;
                    try
                    {
                        switch (step.Verb)
                        {
                            case "wait_idle": ok = await Until(() => host.TestIdle, args.Length == 0 ? 90 : double.Parse(args[0], CultureInfo.InvariantCulture)); break;
                            case "wav": ok = await Wav(args[0]); break;
                            case "event": _ = host.EventTurn(args[0]); ok = true; break;
                            case "select": ok = await host.TestSelect(args[0]); break;
                            case "move": ok = await host.TestMove(args[0], args[1]); break;
                            case "interrupt_after_ms": ok = await Until(() => host.SpeechPlaying, 90); if (ok) { await Task.Delay(int.Parse(args[0])); ok = host.SpeechPlaying && await Wav(args[1]); } break;
                            case "sleep": await Task.Delay(TimeSpan.FromSeconds(double.Parse(args[0], CultureInfo.InvariantCulture))); ok = true; break;
                            case "log": Debug.Log("RPS_E2E marker=" + args[0]); ok = true; break;
                            default: ok = false; break;
                        }
                    }
                    catch (Exception e) { Debug.LogError("RPS_E2E error=" + e.GetType().Name); ok = false; }
                    Debug.Log($"RPS_E2E step={i + 1} {step.Verb} {(ok ? "ok" : "fail")} {watch.ElapsedMilliseconds}");
                    if (!ok) { Debug.Log("RPS_E2E done fail"); return; }
                }
                Debug.Log("RPS_E2E done pass");
            }
            catch (Exception e) { Debug.LogError("RPS_E2E error=" + e.GetType().Name); Debug.Log("RPS_E2E done fail"); }
        }
    }
}
