using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.World;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>remove_objects</c> (S32).</summary>
public sealed class RemoveObjectsArgs
{
    public List<string> ObjectIds { get; set; } = new();
}

/// <summary><c>remove_objects</c> (S32): removes objects from the scene.</summary>
public sealed class RemoveObjectsTool : SenseiTool<RemoveObjectsArgs>
{
    /// <summary>S32a detail when a container is removed without everything inside it.</summary>
    public const string ContainerNotEmpty = "container_not_empty";

    public override string Name => "remove_objects";

    public override string Description => "Remove scene objects; include container contents.";

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
        Task.FromResult(context.Locked(() =>
        {
            // S32a, in the story's order, over the whole batch (S83): unknown id, active task, container contents.
            for (var i = 0; i < args.ObjectIds.Count; i++)
            {
                if (!context.World.Exists(args.ObjectIds[i]))
                    return context.Failure(ToolErrorCodes.UnknownObject, $"object_ids[{i}]: unknown object '{args.ObjectIds[i]}'");
            }

            var blocked = context.Tasks.CheckRemoval(args.ObjectIds);
            if (blocked is not null) return context.Failure(blocked);

            var removing = new HashSet<string>(args.ObjectIds, StringComparer.Ordinal);
            foreach (var id in args.ObjectIds)
            {
                if (context.World.ContentsOf(id).Any(inner => !removing.Contains(inner.Id)))
                    return context.Failure(ToolErrorCodes.InvalidArguments, ContainerNotEmpty);
            }

            var committed = context.World.CommitRemove(args.ObjectIds, SceneActors.Sensei, context.TurnId);
            return context.Success(committed.InstanceIds);
        }));
}
