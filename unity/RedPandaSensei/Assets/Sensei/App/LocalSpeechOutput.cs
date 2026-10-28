#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sensei.App.Core;
using Sensei.Engine.Speech;
using Sensei.Voice;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    public sealed class LocalSpeechOutput : ISpeechOutput
    {
        private readonly LocalTts tts;
        private readonly SpeechPlayer player;
        private readonly MainThreadQueue queue;
        private readonly object gate = new object();
        private readonly SpeechCache<TtsAudio> cache = new SpeechCache<TtsAudio>(64);
        private long generation = -1;
        public event Action<SpeechSegment>? SegmentStarted;
        public SpeechCaptions CurrentCaptions { get; private set; } = SpeechCaptions.All;
        public event Action? UtteranceEnded;
        public LocalSpeechOutput(LocalTts tts, SpeechPlayer player, MainThreadQueue queue) { this.tts = tts; this.player = player; this.queue = queue; }
        public void Interrupt()
        {
            lock (gate) { generation++; player.StopAll(generation); }
            queue.Post(() => UtteranceEnded?.Invoke());
        }
        private bool Current(long id, CancellationToken ct) { lock (gate) return !ct.IsCancellationRequested && id == generation; }
        private async Task<TtsAudio> Synthesize(SpeechSegment segment, float speed, CancellationToken ct)
        {
            lock (gate) if (cache.TryGet(segment.Text, segment.Lang, speed, out var hit)) { Debug.Log($"RPS_SPEAK lang={segment.Lang} chars={segment.Text.Length} synth_ms=0 cache=hit"); return hit; }
            var watch = Stopwatch.StartNew();
            var audio = await tts.SynthesizeAsync(segment.Text, segment.Lang, speed, ct);
            Debug.Log($"RPS_SPEAK lang={segment.Lang} chars={segment.Text.Length} synth_ms={watch.ElapsedMilliseconds}");
            lock (gate) cache.Put(segment.Text, segment.Lang, speed, audio);
            return audio;
        }
        public async Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
        {
            int presented = 0; bool stopped = false; var id = request.GenerationId;
            lock (gate)
            {
                if (ct.IsCancellationRequested || id < generation) return new SpeechOutcome(SpeechStatuses.Cancelled, null, 0);
                if (id > generation) { generation = id; player.StopAll(id); }
            }
            var speed = request.Pace == "slow" ? 0.8f : 1f;
            try
            {
                presented = await SpeechPipeline.PlayAsync(request.Segments,
                    segment => Synthesize(segment, speed, ct),
                    async (segment, audio) => { var outcome = await player.PlayAsync(audio, id, ct, () => queue.Post(() => { if (Current(id, ct)) { CurrentCaptions = request.EffectiveCaptions; SegmentStarted?.Invoke(segment); } })); if (outcome != PlayOutcome.Completed) stopped = true; return outcome; },
                    () => Current(id, ct), ct, count => presented = count);
                if (stopped || !Current(id, ct) || presented != request.Segments.Count) return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented);
                return new SpeechOutcome(SpeechStatuses.Completed, null, presented);
            }
            catch (OperationCanceledException) { return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented); }
            catch (Exception) { var segment = request.Segments[Math.Min(presented, request.Segments.Count - 1)]; Debug.LogError($"RPS_SPEAK failed lang={segment.Lang} chars={segment.Text.Length}"); return new SpeechOutcome(SpeechStatuses.Failed, "synthesis failed", presented); }
            finally { queue.Post(() => { if (Current(id, ct)) UtteranceEnded?.Invoke(); }); }
        }
    }
}
