using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Tools;

namespace Sensei.Engine;

/// <summary>
/// Runs one model-driven turn after the engine started it (S23, S117). Implemented by <c>Agent/SenseiAgentRunner</c>.
/// The engine owns the lifecycle gate (S68, S87), the utterance record, transcript correction (S99) and the final
/// <c>idle</c> state; the runner owns rounds, history, provider errors and <c>thinking</c>/<c>speaking</c>.
/// </summary>
internal interface IAgentRunner
{
    Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct);

    /// <summary>S87: forget the conversation history.</summary>
    void ResetConversation();
}

/// <summary>Input of <see cref="IAgentRunner.RunTurnAsync"/>.</summary>
internal sealed class AgentTurnRequest
{
    public AgentTurnRequest(TurnState turn, string transcript, string? correctedFrom, bool previousTurnInterrupted)
    {
        Turn = turn;
        Transcript = transcript;
        CorrectedFrom = correctedFrom;
        PreviousTurnInterrupted = previousTurnInterrupted;
    }

    /// <summary>The turn the engine already began (turn id, generation, utterance).</summary>
    public TurnState Turn { get; }

    public string Transcript { get; }

    /// <summary>S48: when this turn corrects the previous transcript, the text it replaced.</summary>
    public string? CorrectedFrom { get; }

    /// <summary>S63: the previous turn ended <c>cancelled</c>.</summary>
    public bool PreviousTurnInterrupted { get; }
}
