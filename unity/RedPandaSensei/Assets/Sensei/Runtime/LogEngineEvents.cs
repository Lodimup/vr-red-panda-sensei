#nullable enable
using System.Collections.Generic;
using System;
using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.World;
using UnityEngine;

namespace Sensei.Unity
{
    public sealed class LogEngineEvents : IEngineEvents
    {
        private readonly bool debugText;
        private readonly string? secret;
        public LogEngineEvents(bool debugText = false, string? secret = null)
        {
            this.debugText = debugText;
            this.secret = secret;
        }
        [Serializable] private sealed class JsonText { public string value = ""; }
        private string Quote(string? value)
        {
            if (value == null) return "null";
            var json = JsonUtility.ToJson(new JsonText { value = LogSpeechOutput.Redact(value, secret) });
            return json.Substring(9, json.Length - 10);
        }
        private void Log(string method, string json)
        {
            Debug.Log(debugText ? $"RPS_EVENT {method} {LogSpeechOutput.Redact(json, secret).Replace('\n', ' ').Replace('\r', ' ')}" : $"RPS_EVENT {method}");
        }

        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) => Log(nameof(OnSceneChanged),
            $"{{\"kind\":{Quote(e.Kind)},\"revision\":{scene.Revision}}}");
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) => Log(nameof(OnHighlight),
            $"{{\"ids\":[{string.Join(",", System.Linq.Enumerable.Select(ids, Quote))}],\"duration_ms\":{durationMs}}}");
        public void OnAnimate(string animation, string? targetId) => Log(nameof(OnAnimate),
            $"{{\"animation\":{Quote(animation)},\"target_id\":{Quote(targetId)}}}");
        public void OnHint(HintCard card) => Log(nameof(OnHint),
            $"{{\"kind\":{Quote(card.Kind)},\"japanese\":{Quote(card.Japanese)},\"english\":{Quote(card.English)}}}");
        public void OnSupportChanged(SupportSettings settings) => Log(nameof(OnSupportChanged),
            $"{{\"language_mode\":{Quote(settings.LanguageMode)},\"pace\":{Quote(settings.Pace)}}}");
        public void OnTaskChanged(TaskView task) => Log(nameof(OnTaskChanged),
            $"{{\"id\":{Quote(task.TaskId)},\"status\":{Quote(task.Status)}}}");
        public void OnSummary(SessionSummary summary) => Log(nameof(OnSummary),
            $"{{\"next_suggestion\":{Quote(summary.NextSuggestion)}}}");
        public void OnSenseiState(string state) => Log(nameof(OnSenseiState), $"{{\"state\":{Quote(state)}}}");
    }
}
