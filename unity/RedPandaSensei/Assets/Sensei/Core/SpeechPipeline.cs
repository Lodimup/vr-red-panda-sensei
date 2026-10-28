#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Speech;

namespace Sensei.App.Core
{
    public sealed class SpeechCache<T>
    {
        private readonly int capacity;
        private readonly Dictionary<(string, string, float), LinkedListNode<((string, string, float) key, T value)>> items = new Dictionary<(string, string, float), LinkedListNode<((string, string, float), T)>>();
        private readonly LinkedList<((string, string, float) key, T value)> order = new LinkedList<((string, string, float), T)>();
        public SpeechCache(int capacity) { if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity)); this.capacity = capacity; }
        public bool TryGet(string text, string language, float speed, out T value)
        {
            if (items.TryGetValue((text, language, speed), out var node)) { order.Remove(node); order.AddFirst(node); value = node.Value.value; return true; }
            value = default!; return false;
        }
        public void Put(string text, string language, float speed, T value)
        {
            var key = (text, language, speed);
            if (items.TryGetValue(key, out var old)) { order.Remove(old); items.Remove(key); }
            items.Add(key, order.AddFirst((key, value)));
            if (items.Count > capacity) { var last = order.Last!; items.Remove(last.Value.key); order.RemoveLast(); }
        }
    }
    public enum PlayOutcome { Completed, StoppedAfterStart, NotStarted }

    public sealed class SpeechChunk
    {
        public SpeechSegment Segment { get; }
        public string Text { get; }
        public int SegmentIndex { get; }
        public bool First { get; }
        internal SpeechChunk(SpeechSegment segment, string text, int segmentIndex, bool first)
        { Segment = segment; Text = text; SegmentIndex = segmentIndex; First = first; }
    }

    public static class SpeechChunker
    {
        public static List<SpeechChunk> Split(IReadOnlyList<SpeechSegment> segments)
        {
            var chunks = new List<SpeechChunk>();
            for (int index = 0; index < segments.Count; index++)
            {
                var segment = segments[index]; var text = segment.Text; int start = 0; bool first = true;
                for (int i = 0; i < text.Length; i++)
                {
                    if ("。！？!?.".IndexOf(text[i]) < 0 || (i + 1 < text.Length && !char.IsWhiteSpace(text[i + 1]))) continue;
                    int end = i + 1;
                    while (end < text.Length && char.IsWhiteSpace(text[end])) end++;
                    chunks.Add(new SpeechChunk(segment, text.Substring(start, end - start), index, first));
                    first = false; start = end; i = end - 1;
                }
                if (start < text.Length || first) chunks.Add(new SpeechChunk(segment, text.Substring(start), index, first));
            }
            return chunks;
        }
    }

    public static class SpeechPipeline
    {
        // Count segments once playback has started, including interrupted playback.
        public static async Task<int> PlayAsync<TSegment, TAudio>(IReadOnlyList<TSegment> segments, Func<TSegment, Task<TAudio>> synthesize, Func<TSegment, TAudio, Task<PlayOutcome>> play, Func<bool> current, CancellationToken token, Action<int>? onPlayed = null)
        {
            if (segments.Count == 0) return 0;
            var next = synthesize(segments[0]); int played = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                var audio = await next; token.ThrowIfCancellationRequested(); if (!current()) return played;
                var playback = play(segments[i], audio);
                if (i + 1 < segments.Count) next = synthesize(segments[i + 1]);
                var outcome = await playback;
                if (outcome != PlayOutcome.NotStarted) { played++; onPlayed?.Invoke(played); }
                if (outcome != PlayOutcome.Completed || !current()) return played;
            }
            return played;
        }
    }
}
