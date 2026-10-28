#nullable enable
using Sensei.App.Core;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Speech;
using UnityEngine;

namespace Sensei.Unity
{
    // Orchestrator decision 2026-10-28: the sole temporary port until the voice slice.
    public sealed class LogSpeechOutput : ISpeechOutput
    {
        private readonly bool debugText;
        private readonly string? secret;
        public LogSpeechOutput(bool debugText = false, string? secret = null)
        {
            this.debugText = debugText;
            this.secret = secret;
        }
        public static string Redact(string text, string? secret) => LogRedactor.Redact(text, secret);
        public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
        {
            foreach (var segment in request.Segments)
            {
                ct.ThrowIfCancellationRequested();
                var message = $"RPS_SPEAK lang={segment.Lang} chars={segment.Text.Length} synth_ms=0";
                if (debugText) message += " text=" + Redact(segment.Text, secret).Replace('\n', ' ').Replace('\r', ' ');
                Debug.Log(message);
            }
            return Task.FromResult(new SpeechOutcome("completed", null));
        }
    }
}
