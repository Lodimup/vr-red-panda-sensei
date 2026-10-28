using System.Collections.Generic;
using Sensei.Engine.Learner;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tools;

/// <summary>State of the current turn (S23–S26). Read and write under the engine lock.</summary>
public sealed class TurnState
{
    /// <summary>Tool calls allowed per turn (S25).</summary>
    public const int CallBudget = 8;

    /// <summary>Validation failures after which the turn is exhausted (S25).</summary>
    public const int ValidationFailureLimit = 2;

    public long TurnId { get; internal set; }
    public long GenerationId { get; internal set; }

    /// <summary>The learner utterance that started the turn (synthetic for <c>(cli)</c>).</summary>
    public LearnerUtterance Utterance { get; internal set; } = new();

    /// <summary>Model round within the turn, from 1.</summary>
    public int Round { get; internal set; }

    /// <summary>Id of the last scene event when the current round began (S26, S84).</summary>
    public long RoundBaseEventId { get; internal set; }

    /// <summary>Non-cached calls made this turn.</summary>
    public int CallsCounted { get; internal set; }

    public int ValidationFailures { get; internal set; }
    public bool Exhausted { get; internal set; }

    /// <summary>Characters spoken this turn by <c>speak</c> and <c>show_hint</c> (S37c), maintained by the speech tools.</summary>
    public int SpokenCharacters { get; set; }

    /// <summary>Trace entries of this turn, for <c>TurnResult.tool_calls</c>.</summary>
    public List<TraceEntry> TraceEntries { get; } = new();

    /// <summary>Calls remaining before the budget is exhausted.</summary>
    public int RemainingCalls => Exhausted ? 0 : System.Math.Max(0, CallBudget - CallsCounted);
}

/// <summary>A failed check: error code plus detail.</summary>
public sealed record ToolFailure(string Code, string Detail);
