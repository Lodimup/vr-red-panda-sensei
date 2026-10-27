using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.World;

/// <summary><c>move_object.destination</c>: exactly one of region or container id.</summary>
public sealed class MoveDestination
{
    public string? Region { get; set; }
    public string? ContainerId { get; set; }
}

/// <summary>Arguments of <c>move_object</c> (S31).</summary>
public sealed class MoveObjectArgs
{
    public string ObjectId { get; set; } = "";
    public MoveDestination Destination { get; set; } = new();
    public string Purpose { get; set; } = "";
}

/// <summary><c>move_object</c> (S31): Sensei moves an object to a region or into a container.</summary>
public sealed class MoveObjectTool : SenseiTool<MoveObjectArgs>
{
    public override string Name => "move_object";

    public override string Description => "Move one object to a region or into a container, to fulfil the learner's request, demonstrate, or arrange the scene.";

    public override bool IsMutating => true;

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "object_id": { "type": "string", "description": "Instance id of the object to move." },
            "destination": {
              "type": "object",
              "description": "Where to move it: exactly one of region or container_id.",
              "properties": {
                "region": { "type": "string", "enum": ["left", "center", "right", "learner_hand", "sensei_side"], "description": "Target region." },
                "container_id": { "type": "string", "description": "Instance id of the bag or box to put it in." }
              },
              "additionalProperties": false
            },
            "purpose": { "type": "string", "enum": ["fulfill_request", "demonstrate", "arrange"], "description": "Why Sensei moves it; demonstrate counts as help for the active task." }
          },
          "required": ["object_id", "destination", "purpose"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<MoveObjectArgs> ArgsTypeInfo => SenseiJsonContext.Default.MoveObjectArgs;

    public override IEnumerable<string> ReferencedInstanceIds(MoveObjectArgs args)
    {
        yield return args.ObjectId;
        if (args.Destination.ContainerId is not null) yield return args.Destination.ContainerId;
    }

    public override Task<ToolResult> ExecuteAsync(MoveObjectArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
