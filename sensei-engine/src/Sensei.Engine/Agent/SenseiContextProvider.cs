using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Agents.AI;

namespace Sensei.Engine.Agent;

/// <summary>
/// MAF context provider (S61): injects the engine-owned per-turn state as instructions. It adds no messages, so nothing
/// it contributes is ever persisted in the conversation history. The round-limiting client refreshes the same text
/// before every later model request (S63).
/// </summary>
internal sealed class SenseiContextProvider : AIContextProvider
{
    private readonly ContextRenderer _renderer;
    private readonly Func<TurnScope?> _active;

    public SenseiContextProvider(ContextRenderer renderer, Func<TurnScope?> active)
    {
        _renderer = renderer;
        _active = active;
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var scope = _active();
        return new ValueTask<AIContext>(new AIContext { Instructions = scope is null ? null : _renderer.RenderState(scope) });
    }
}
