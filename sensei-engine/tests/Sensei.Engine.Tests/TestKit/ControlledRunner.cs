namespace Sensei.Engine.Tests.TestKit;

/// <summary>An agent runner that blocks until released or cancelled, for lifecycle tests (S68, S87, S116).</summary>
internal sealed class ControlledRunner : IAgentRunner
{
    private readonly EngineCore _core;
    private TaskCompletionSource<bool> _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ControlledRunner(EngineCore core)
    {
        _core = core;
    }

    public TaskCompletionSource<AgentTurnRequest> Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public List<AgentTurnRequest> Requests { get; } = new();

    public int ResetCount { get; private set; }

    /// <summary>When false, turns complete at once.</summary>
    public bool Block { get; set; } = true;

    public void Release() => _release.TrySetResult(true);

    public async Task<TurnResult> RunTurnAsync(AgentTurnRequest request, CancellationToken ct)
    {
        lock (Requests) Requests.Add(request);
        Started.TrySetResult(request);
        var status = TurnStatuses.Completed;
        if (Block)
        {
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var reg = _core.GenerationToken.Register(() => cancelled.TrySetResult(true));
            using var reg2 = ct.Register(() => cancelled.TrySetResult(true));
            var winner = await Task.WhenAny(_release.Task, cancelled.Task);
            if (winner == cancelled.Task) status = TurnStatuses.Cancelled;
        }
        _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Started = new TaskCompletionSource<AgentTurnRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        return new TurnResult { TurnId = request.Turn.TurnId, GenerationId = request.Turn.GenerationId, Status = status, Rounds = 1 };
    }

    public void ResetConversation() => ResetCount++;
}
