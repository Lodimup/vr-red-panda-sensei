#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    public static class SpeechPipeline
    {
        // A zero-based result is the number of segments that completed playback.
        public static async Task<int> PlayAsync<TSegment, TAudio>(IReadOnlyList<TSegment> segments, Func<TSegment, Task<TAudio>> synthesize, Func<TSegment, TAudio, Task<bool>> play, Func<bool> current, CancellationToken token, Action<int>? onPlayed = null)
        {
            if (segments.Count == 0) return 0;
            var next = synthesize(segments[0]); int played = 0;
            for (int i = 0; i < segments.Count; i++)
            {
                var audio = await next; token.ThrowIfCancellationRequested(); if (!current()) return played;
                var playback = play(segments[i], audio);
                if (i + 1 < segments.Count) next = synthesize(segments[i + 1]);
                if (!await playback || !current()) return played;
                played++; onPlayed?.Invoke(played);
            }
            return played;
        }
    }
}
