using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Agent;

/// <summary>
/// Stands in for a tool name the model asked for that the registry does not offer in the request (an unknown tool, S85),
/// so the function-invocation loop hands the call to the engine pipeline instead of answering it itself.
/// Never sent to the provider and never invoked directly: <see cref="EngineFunctionInvoker"/> routes every call.
/// </summary>
internal sealed class PassThroughFunction : AIFunction
{
    private static readonly JsonElement EmptyObjectSchema = ParseSchema();

    private readonly string _name;

    public PassThroughFunction(string name)
    {
        _name = name;
    }

    public override string Name => _name;

    public override string Description => "Routes a call the model made to the engine pipeline.";

    public override JsonElement JsonSchema => EmptyObjectSchema;

    protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("pass-through functions are invoked only through the engine function invoker");

    private static JsonElement ParseSchema()
    {
        using var doc = JsonDocument.Parse("{\"type\":\"object\"}", new JsonDocumentOptions { MaxDepth = JsonRedaction.WalkDepth(JsonRedaction.MaxFinalValues) });
        return doc.RootElement.Clone();
    }
}
