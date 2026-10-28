using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Speech;
using UnityEngine;

namespace Sensei.Unity
{
    // RULING(Handler 2026-10-28): the sole temporary port until the voice slice.
    public sealed class LogSpeechOutput : ISpeechOutput
    {
        public Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
        {
            foreach (var segment in request.Segments)
            {
                ct.ThrowIfCancellationRequested();
                Debug.Log($"RPS_SPEAK lang={segment.Lang} text={segment.Text.Replace('\n', ' ').Replace('\r', ' ')}");
            }
            return Task.FromResult(new SpeechOutcome("completed", null));
        }
    }
}
