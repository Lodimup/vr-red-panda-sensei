using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>remove_objects</c> (S32).</summary>
public sealed class RemoveObjectsArgs
{
    public List<string> ObjectIds { get; set; } = new();
}

/// <summary><c>remove_objects</c> (S32): removes objects from the scene.</summary>
public sealed class RemoveObjectsTool : SenseiTool<RemoveObjectsArgs>
{
    public override string Name => "remove_objects";

    public override string Description => "Take objects out of the scene; a container can only go together with everything inside it.";

    public override bool IsMutating => true;

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "object_ids": {
              "type": "array",
              "description": "Distinct instance ids to remove, 1 to 16.",
              "minItems": 1,
              "maxItems": 16,
              "uniqueItems": true,
              "items": { "type": "string", "description": "Instance id." }
            }
          },
          "required": ["object_ids"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<RemoveObjectsArgs> ArgsTypeInfo => SenseiJsonContext.Default.RemoveObjectsArgs;

    public override IEnumerable<string> ReferencedInstanceIds(RemoveObjectsArgs args) => args.ObjectIds;

    public override Task<ToolResult> ExecuteAsync(RemoveObjectsArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
