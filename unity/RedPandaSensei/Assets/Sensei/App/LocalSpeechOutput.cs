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
        private readonly bool debugText; private readonly string? secret;
        private readonly object gate = new object();
        private readonly SpeechCache<TtsAudio> cache = new SpeechCache<TtsAudio>(64);
        private long generation = -1;
        public event Action<SpeechSegment>? SegmentStarted;
        public SpeechCaptions CurrentCaptions { get; private set; } = SpeechCaptions.All;
        public event Action? UtteranceEnded;
        public LocalSpeechOutput(LocalTts tts, SpeechPlayer player, MainThreadQueue queue, bool debugText = false, string? secret = null) { this.tts = tts; this.player = player; this.queue = queue; this.debugText = debugText; this.secret = secret; }
        public void Interrupt()
        {
            lock (gate) { generation++; player.StopAll(generation); }
            queue.Post(() => UtteranceEnded?.Invoke());
        }
        private bool Current(long id, CancellationToken ct) { lock (gate) return !ct.IsCancellationRequested && id == generation; }
        private async Task<TtsAudio> Synthesize(SpeechChunk chunk, float speed, CancellationToken ct)
        {
            var segment = chunk.Segment; var text = chunk.Text;
            var latency = TurnLatency.Active; bool first = latency?.FirstSynthesis() == true; if (first) latency!.Mark("first_synth_start");
            lock (gate) if (cache.TryGet(text, segment.Lang, speed, out var hit)) { if (first) latency!.Mark("first_synth_end", " cache=hit"); Debug.Log($"RPS_SPEAK lang={segment.Lang} chars={text.Length} synth_ms=0 cache=hit"); return hit; }
            var watch = Stopwatch.StartNew();
            var audio = await tts.SynthesizeAsync(text, segment.Lang, speed, ct);
            if (first) latency!.Mark("first_synth_end", " cache=miss");
            Debug.Log($"RPS_SPEAK lang={segment.Lang} chars={text.Length} synth_ms={watch.ElapsedMilliseconds}");
            lock (gate) cache.Put(text, segment.Lang, speed, audio);
            return audio;
        }
        public async Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
        {
            int presented = 0; int segmentIndex = 0; bool stopped = false; var id = request.GenerationId; var latency = TurnLatency.Active;
            if (debugText) Debug.Log($"RPS_SPEAK_GROUP generation={id} langs=" + string.Join(",", System.Linq.Enumerable.Select(request.Segments, segment => segment.Lang)));
            lock (gate)
            {
                if (ct.IsCancellationRequested || id < generation) return new SpeechOutcome(SpeechStatuses.Cancelled, null, 0);
                if (id > generation) { generation = id; player.StopAll(id); }
            }
            var speed = request.Pace == "slow" ? 0.8f : 1f;
            try
            {
                var chunks = SpeechChunker.Split(request.Segments);
                var played = await SpeechPipeline.PlayAsync(chunks,
                    chunk => { segmentIndex = chunk.SegmentIndex; return Synthesize(chunk, speed, ct); },
                    async (chunk, audio) =>
                    {
                        var outcome = await player.PlayAsync(audio, id, ct, () =>
                        {
                            latency?.PlaybackStarted();
                            if (!chunk.First) return;
                            Interlocked.Exchange(ref presented, chunk.SegmentIndex + 1);
                            queue.Post(() =>
                            {
                                if (!Current(id, ct)) return;
                                var segment = chunk.Segment;
                                if (debugText) Debug.Log($"RPS_SPEAK generation={id} lang={segment.Lang} text=" + LogRedactor.Redact(segment.Text, secret).Replace('\n', ' ').Replace('\r', ' '));
                                CurrentCaptions = request.EffectiveCaptions; SegmentStarted?.Invoke(segment);
                            });
                        });
                        if (outcome != PlayOutcome.Completed) stopped = true;
                        return outcome;
                    },
                    () => Current(id, ct), ct);
                if (stopped || !Current(id, ct) || played != chunks.Count) return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented);
                return new SpeechOutcome(SpeechStatuses.Completed, null, presented);
            }
            catch (OperationCanceledException) { return new SpeechOutcome(SpeechStatuses.Cancelled, null, presented); }
            catch (Exception) { var segment = request.Segments[Math.Min(segmentIndex, request.Segments.Count - 1)]; Debug.LogError($"RPS_SPEAK failed lang={segment.Lang} chars={segment.Text.Length}"); return new SpeechOutcome(SpeechStatuses.Failed, "synthesis failed", presented); }
            finally { queue.Post(() => { if (Current(id, ct)) UtteranceEnded?.Invoke(); }); }
        }
    }
}
