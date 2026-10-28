#nullable enable
using System;
using System.Diagnostics;
using System.Threading;
using Sensei.Engine.Trace;
using Debug = UnityEngine.Debug;

namespace Sensei.App
{
    // ExecutionContext associates asynchronous engine and speech work with the releasing input.
    internal sealed class TurnLatency
    {
        private static readonly AsyncLocal<TurnLatency?> active = new AsyncLocal<TurnLatency?>();
        private static long nextId;
        private readonly long id = Interlocked.Increment(ref nextId);
        private readonly long started = Stopwatch.GetTimestamp();
        private int firstSynthesis;
        private int firstPlayback;
        internal static TurnLatency? Active { get => active.Value; set => active.Value = value; }
        internal bool FirstSynthesis() => Interlocked.Exchange(ref firstSynthesis, 1) == 0;
        internal void PlaybackStarted() { if (Interlocked.Exchange(ref firstPlayback, 1) == 0) Mark("first_audio_playing"); }
        internal void Mark(string phase, string extra = "")
        {
            long ms = (long)((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
            Debug.Log($"RPS_LAT turn={id} phase={phase} ms={ms}{extra}");
        }
        internal sealed class Sink : IRoundTraceSink
        {
            public void Write(TraceEntry entry)
            {
                if (entry.Tool != "engine:latency") return;
                var turn = Active;
                if (turn == null) return;
                var extra = $" round={entry.Round}";
                if (entry.Status == "response_received") extra += " output_tokens=" + (entry.Arguments.Length == 0 ? "unknown" : entry.Arguments);
                turn.Mark(entry.Status, extra);
            }
        }
    }
}
