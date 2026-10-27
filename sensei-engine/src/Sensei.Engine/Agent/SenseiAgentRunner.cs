using System.Threading;
using System.Threading.Tasks;

namespace Sensei.Engine.Agent;

/// <summary>
/// Agent loop over Microsoft Agent Framework (S61–S68). Owned by the agent slice; this stub reports
/// <c>llm_not_configured</c> until it is implemented.
/// </summary>
internal sealed class SenseiAgentRunner : IAgentRunner
{
    private readonly EngineCore _core;

    public SenseiAgentRunner(EngineCore core)
    {
        _core = core;
    }

    public Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct)
    {
        _ = _core;
        return Task.FromResult(new TurnResult
        {
            TurnId = request.Turn.TurnId,
            GenerationId = request.Turn.GenerationId,
            Status = TurnStatuses.Failed,
            ErrorCode = TurnErrorCodes.LlmNotConfigured,
            ErrorDetail = "the agent loop is not implemented yet",
        });
    }

    public void ResetConversation()
    {
    }
}
