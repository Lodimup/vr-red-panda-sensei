using System;
using System.Collections.Generic;
using System.Linq;

namespace Sensei.Engine.Learner;

/// <summary>A learner utterance (S92): id <c>l&lt;n&gt;</c>, its turn and its interaction-sequence position.</summary>
public sealed class LearnerUtterance
{
    public string Id { get; set; } = "";
    public long TurnId { get; set; }
    public string Text { get; set; } = "";

    /// <summary>Created by the CLI's <c>/tool</c> (<c>BeginTurn("(cli)")</c>): never an excerpt source, never corrected.</summary>
    public bool IsSynthetic { get; set; }

    public bool Superseded { get; set; }
    public long Sequence { get; set; }
    public DateTimeOffset At { get; set; }

    /// <summary>Support settings when the utterance arrived (S94).</summary>
    public SupportSettings SupportAtArrival { get; set; } = new();

    /// <summary>When this utterance is a correction (S48), the text it replaced.</summary>
    public string? CorrectedFrom { get; set; }
}

/// <summary>Learner utterances of this engine instance (cleared on reset). Access under the engine lock.</summary>
public sealed class UtteranceLog
{
    private readonly List<LearnerUtterance> _items = new();

    public IReadOnlyList<LearnerUtterance> All => _items;

    internal void Add(LearnerUtterance u) => _items.Add(u);

    internal void Clear() => _items.Clear();

    public LearnerUtterance? Find(string id) => _items.FirstOrDefault(u => u.Id == id);

    /// <summary>The latest non-synthetic utterance (S99), superseded or not excluded.</summary>
    public LearnerUtterance? LatestCorrectable() => _items.LastOrDefault(u => !u.IsSynthetic && !u.Superseded);

    /// <summary>Non-synthetic, non-superseded utterances of turns <paramref name="currentTurnId"/> and the one before.</summary>
    public IEnumerable<LearnerUtterance> CurrentOrPreviousTurn(long currentTurnId) =>
        _items.Where(u => !u.IsSynthetic && !u.Superseded && (u.TurnId == currentTurnId || u.TurnId == currentTurnId - 1));

    /// <summary>The latest non-synthetic utterance strictly before sequence position <paramref name="sequence"/>.</summary>
    public LearnerUtterance? PreviousBefore(long sequence) => _items.LastOrDefault(u => !u.IsSynthetic && u.Sequence < sequence);
}
