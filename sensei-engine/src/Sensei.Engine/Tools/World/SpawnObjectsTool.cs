using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.World;

/// <summary>One entry of <c>spawn_objects.objects</c>.</summary>
public sealed class SpawnObjectEntry
{
    public string CatalogId { get; set; } = "";
    public string? Variant { get; set; }
    public string? Region { get; set; }
    public int? Quantity { get; set; }
}

/// <summary>Arguments of <c>spawn_objects</c> (S30).</summary>
public sealed class SpawnObjectsArgs
{
    public List<SpawnObjectEntry> Objects { get; set; } = new();
}

/// <summary><c>spawn_objects</c> (S30): creates catalog objects in the practice area.</summary>
public sealed class SpawnObjectsTool : SenseiTool<SpawnObjectsArgs>
{
    public override string Name => "spawn_objects";

    public override string Description => "Place catalog objects into the practice area or beside Sensei; new objects can be referenced from the next model round on.";

    public override bool IsMutating => true;

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "objects": {
              "type": "array",
              "description": "Objects to create, 1 to 6 entries, at most 6 objects in total.",
              "minItems": 1,
              "maxItems": 6,
              "items": {
                "type": "object",
                "properties": {
                  "catalog_id": { "type": "string", "description": "Catalog id of the object, for example cup or apple." },
                  "variant": { "type": "string", "description": "Catalog variant; defaults to the entry's first variant." },
                  "region": { "type": "string", "enum": ["left", "center", "right", "sensei_side"], "description": "Where to place it; defaults to center." },
                  "quantity": { "type": "integer", "minimum": 1, "maximum": 4, "description": "How many to create, 1 to 4; defaults to 1." }
                },
                "required": ["catalog_id"],
                "additionalProperties": false
              }
            }
          },
          "required": ["objects"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<SpawnObjectsArgs> ArgsTypeInfo => SenseiJsonContext.Default.SpawnObjectsArgs;

    public override Task<ToolResult> ExecuteAsync(SpawnObjectsArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
