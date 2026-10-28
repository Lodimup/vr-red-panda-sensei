using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Speech;

/// <summary>One utterance kept for replay and audio-model detection (S37d, S89, S90).</summary>
public sealed class SpeechHistoryEntry
{
    public string UtteranceId { get; set; } = "";
    public IReadOnlyList<SpeechSegment> Segments { get; set; } = new List<SpeechSegment>();
    public string Pace { get; set; } = "";
    public long TurnId { get; set; }
    public long GenerationId { get; set; }
    public int SegmentsPresented { get; set; }
    public string Outcome { get; set; } = "";
    public bool IsReplay { get; set; }

    /// <summary>Position in the engine-wide interaction sequence (presentation, S92).</summary>
    public long Sequence { get; set; }
}

/// <summary>The last 20 presented utterances (S37d). Access under the engine lock.</summary>
public sealed class SpeechHistory
{
    public const int Capacity = 20;

    private readonly List<SpeechHistoryEntry> _entries = new();

    /// <summary>Oldest first.</summary>
    public IReadOnlyList<SpeechHistoryEntry> Entries => _entries;

    public SpeechHistoryEntry? Latest => _entries.Count == 0 ? null : _entries[_entries.Count - 1];

    public SpeechHistoryEntry? Find(string utteranceId) => _entries.FirstOrDefault(e => e.UtteranceId == utteranceId);

    /// <summary>Adds an entry; only call when at least one segment was presented (S89).</summary>
    internal void Add(SpeechHistoryEntry entry)
    {
        _entries.Add(entry);
        while (_entries.Count > Capacity) _entries.RemoveAt(0);
    }

    internal void Clear() => _entries.Clear();
}
