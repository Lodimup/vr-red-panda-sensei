using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

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

    public override Task<ToolResult> ExecuteAsync(HighlightObjectsArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
