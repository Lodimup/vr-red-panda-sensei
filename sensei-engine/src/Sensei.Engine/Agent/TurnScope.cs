using System.Collections.Generic;
using System.Threading;

namespace Sensei.Engine.Agent;

/// <summary>
/// What the agent loop knows about the turn it is running: the request, the combined cancellation token, the model
/// requests made so far and what the provider answered. One turn runs at a time (S68), so the layers of the chat-client
/// stack read it through the runner's <c>Active</c> field.
/// </summary>
internal sealed class TurnScope
{
    public TurnScope(AgentTurnRequest request, CancellationToken token, long playerEventMark)
    {
        Request = request;
        Token = token;
        PlayerEventMark = playerEventMark;
        ShownEventsUpTo = playerEventMark;
    }

    public AgentTurnRequest Request { get; }

    public Tools.TurnState Turn => Request.Turn;

    /// <summary>Cancelled when the generation is cancelled or the caller's token fires.</summary>
    public CancellationToken Token { get; }

    /// <summary>Player events with an id above this were not shown to the model before this turn (S63).</summary>
    public long PlayerEventMark { get; }

    /// <summary>Highest scene event id already reflected in a context sent to the model.</summary>
    public long ShownEventsUpTo { get; set; }

    /// <summary>Model requests that reached the provider (S64).</summary>
    public int Rounds { get; set; }

    /// <summary>True once a request was refused because the round limit was reached (S64).</summary>
    public bool BudgetExhausted { get; set; }

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    /// <summary>Plain assistant text the provider produced (trace only, S67).</summary>
    public List<string> AssistantText { get; } = new();

    /// <summary>Last presentation state raised this turn, to avoid repeats (S69).</summary>
    public string LastState { get; set; } = Events.SenseiStates.Idle;

    /// <summary>The text of the interrupted previous turn, when there was one (S63).</summary>
    public string? InterruptedTranscript { get; set; }
}
