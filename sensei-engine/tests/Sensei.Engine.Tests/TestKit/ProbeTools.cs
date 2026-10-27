using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Sensei.Engine.Events;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>Arguments of the test-only probe tools.</summary>
public sealed class ProbeArgs
{
    public string Action { get; set; } = "";
    public string? Text { get; set; }
    public int? Count { get; set; }
    public List<string> Ids { get; set; } = new();
    public ProbeNested? Nested { get; set; }
    public int? DelayMs { get; set; }
    public bool Flag { get; set; }
    public string? Code { get; set; }
}

public sealed class ProbeNested
{
    public bool Flag { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProbeArgs))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Test-only tool that exercises the pipeline (S21–S28, S81–S86) without depending on the 16 stub tools.
/// Actions: <c>ok</c>, <c>fail_unknown</c>, <c>fail_speech</c>, <c>spawn</c>, <c>slow</c>, <c>end_generation</c>, <c>throw</c>, <c>hint</c>, <c>secret</c>.
/// </summary>
public class ProbeTool : SenseiTool<ProbeArgs>
{
    private int _executions;

    public ProbeTool(string name = "test_probe")
    {
        ToolName = name;
    }

    public string ToolName { get; }

    public int Executions => _executions;

    public override string Name => ToolName;

    public override string Description => "Test-only probe tool that exercises the engine pipeline.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "action": { "type": "string", "enum": ["ok", "fail_unknown", "fail_speech", "spawn", "slow", "spawn_slow", "end_generation", "throw", "hint", "secret"], "description": "What the probe does." },
            "text": { "type": "string", "maxLength": 10, "description": "Echoed text, at most 10 characters." },
            "count": { "type": "integer", "minimum": 1, "maximum": 5, "description": "A bounded integer." },
            "ids": { "type": "array", "maxItems": 4, "uniqueItems": true, "items": { "type": "string", "description": "Instance id." }, "description": "Referenced instance ids." },
            "nested": {
              "type": "object",
              "description": "A nested object.",
              "properties": { "flag": { "type": "boolean", "description": "A required flag." } },
              "required": ["flag"],
              "additionalProperties": false
            },
            "delay_ms": { "type": "integer", "minimum": 0, "maximum": 60000, "description": "Delay for slow." },
            "flag": { "type": "boolean", "description": "Optional boolean defaulting to false." },
            "code": { "type": "string", "pattern": "^[a-z]{1,3}$", "description": "A short lowercase code." }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<ProbeArgs> ArgsTypeInfo => TestJsonContext.Default.ProbeArgs;

    public override string? ValidateArguments(ProbeArgs args) =>
        args.Text == "bad" ? "text: must not be bad" : null;

    public override IEnumerable<string> ReferencedInstanceIds(ProbeArgs args) => args.Ids;

    public override async Task<ToolResult> ExecuteAsync(ProbeArgs args, ToolContext context, CancellationToken ct)
    {
        Interlocked.Increment(ref _executions);
        switch (args.Action)
        {
            case "ok":
                return context.Success(args.Ids, ToolContext.ToData(new JsonObject { ["echo"] = args.Text, ["flag"] = args.Flag }));
            case "fail_unknown":
                return context.Failure(ToolErrorCodes.UnknownObject, "probe failure");
            case "fail_speech":
                return context.Failure(ToolErrorCodes.SpeechFailed, "probe speech failure");
            case "spawn":
                return context.Locked(() =>
                {
                    if (context.World.Count >= SceneWorld.Capacity) return context.Failure(ToolErrorCodes.CapacityExceeded, "full");
                    var e = context.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, context.TurnId);
                    return context.Success(e.InstanceIds);
                });
            case "slow":
                await Task.Delay(args.DelayMs ?? 5000, ct);
                return context.Locked(() => context.Success());
            case "spawn_slow":
            {
                var spawned = context.Locked(() =>
                {
                    var e = context.World.CommitSpawn(new[] { new SpawnRequest("apple", SceneRegions.Center) }, SceneActors.Sensei, context.TurnId);
                    return context.Success(e.InstanceIds);
                });
                await Task.Delay(args.DelayMs ?? 300, ct);
                return spawned;
            }
            case "end_generation":
                context.EndGenerationAfterCall();
                return context.Success();
            case "throw":
                throw new InvalidOperationException("probe exploded");
            case "hint":
                context.Events.Hint(new HintCard { Kind = "explanation", English = args.Text });
                return context.Success();
            case "secret":
                return context.Failure(ToolErrorCodes.InvalidArguments, "detail with " + args.Text);
            default:
                return context.Failure(ToolErrorCodes.InvalidArguments, "unknown action");
        }
    }
}

/// <summary>The same probe, declared mutating so the stale-scene rule applies (S26).</summary>
public sealed class MutatingProbeTool : ProbeTool
{
    public MutatingProbeTool() : base("test_mutate")
    {
    }

    public override bool IsMutating => true;
}
