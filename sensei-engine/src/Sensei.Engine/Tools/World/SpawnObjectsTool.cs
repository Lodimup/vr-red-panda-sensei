using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Catalog;
using Sensei.Engine.Json;
using Sensei.Engine.World;

namespace Sensei.Engine.Tools.World;

/// <summary>One entry of <c>spawn_objects.objects</c>.</summary>
public sealed class SpawnObjectEntry
{
    public string CatalogId { get; set; } = "";
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
    private const int DefaultQuantity = 1;
    private const int MaxTotalQuantity = 6;

    public override string Name => "spawn_objects";

    public override string Description => "Place catalog objects in the scene.";

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

    /// <summary>Total quantity of the call, with the default of 1 for an omitted quantity (S30a).</summary>
    private static int TotalQuantity(SpawnObjectsArgs args) => args.Objects.Sum(o => o.Quantity ?? DefaultQuantity);

    public override string? ValidateArguments(SpawnObjectsArgs args) =>
        TotalQuantity(args) > MaxTotalQuantity ? $"objects: total quantity must be at most {MaxTotalQuantity}" : null;

    public override Task<ToolResult> ExecuteAsync(SpawnObjectsArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Locked(() =>
        {
            for (var i = 0; i < args.Objects.Count; i++)
            {
                if (!ObjectCatalog.Contains(args.Objects[i].CatalogId))
                    return context.Failure(ToolErrorCodes.UnknownCatalogId, $"objects[{i}].catalog_id: unknown catalog id '{args.Objects[i].CatalogId}'");
            }

            var total = TotalQuantity(args);
            if (context.World.Count + total > SceneWorld.Capacity)
                return context.Failure(ToolErrorCodes.CapacityExceeded, $"the scene holds at most {SceneWorld.Capacity} objects; it has {context.World.Count} and the call adds {total}");

            var requests = new List<SpawnRequest>();
            foreach (var entry in args.Objects)
            {
                var region = entry.Region ?? SceneRegions.Center;
                for (var n = 0; n < (entry.Quantity ?? DefaultQuantity); n++) requests.Add(new SpawnRequest(entry.CatalogId, region));
            }

            var committed = context.World.CommitSpawn(requests, SceneActors.Sensei, context.TurnId);
            return context.Success(committed.InstanceIds);
        }));
}
