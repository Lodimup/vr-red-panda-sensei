using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>highlight_objects</c> (S33).</summary>
public sealed class HighlightObjectsArgs
{
    public List<string> ObjectIds { get; set; } = new();
    public int? DurationMs { get; set; }
}

/// <summary><c>highlight_objects</c> (S33): makes objects glow briefly.</summary>
public sealed class HighlightObjectsTool : SenseiTool<HighlightObjectsArgs>
{
    private const int DefaultDurationMs = 2000;

    public override string Name => "highlight_objects";

    public override string Description => "Make up to four objects glow for a moment so the learner notices them; highlighting a task answer counts as help.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "object_ids": {
              "type": "array",
              "description": "Distinct instance ids to highlight, 1 to 4.",
              "minItems": 1,
              "maxItems": 4,
              "uniqueItems": true,
              "items": { "type": "string", "description": "Instance id." }
            },
            "duration_ms": { "type": "integer", "minimum": 500, "maximum": 8000, "description": "How long the glow lasts in milliseconds; defaults to 2000." }
          },
          "required": ["object_ids"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<HighlightObjectsArgs> ArgsTypeInfo => SenseiJsonContext.Default.HighlightObjectsArgs;

    public override IEnumerable<string> ReferencedInstanceIds(HighlightObjectsArgs args) => args.ObjectIds;

    public override Task<ToolResult> ExecuteAsync(HighlightObjectsArgs args, ToolContext context, CancellationToken ct)
    {
        var durationMs = args.DurationMs ?? DefaultDurationMs;
        var result = context.Locked(() =>
        {
            for (var i = 0; i < args.ObjectIds.Count; i++)
            {
                if (!context.World.Exists(args.ObjectIds[i]))
                    return context.Failure(ToolErrorCodes.UnknownObject, $"object_ids[{i}]: unknown object '{args.ObjectIds[i]}'");
            }

            // S33: pointing at a task answer is help for the task's skill targets.
            string? recorded = null;
            if (context.Tasks.Active is { } task && args.ObjectIds.Any(context.Tasks.IsReferencedByActiveTask))
            {
                context.RecordAssistanceForTask(AssistanceKinds.HighlightAnswer, task);
                recorded = AssistanceKinds.HighlightAnswer;
            }

            var data = new JsonObject { ["recorded_assistance"] = recorded };
            return context.Success(args.ObjectIds, ToolContext.ToData(data));
        });

        if (result.IsSuccess) context.Events.Highlight(args.ObjectIds.ToArray(), durationMs);
        return Task.FromResult(result);
    }
}
