using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>animate_sensei</c> (S34).</summary>
public sealed class AnimateSenseiArgs
{
    public string Animation { get; set; } = "";
    public string? TargetId { get; set; }
}

/// <summary><c>animate_sensei</c> (S34): plays a Sensei animation.</summary>
public sealed class AnimateSenseiTool : SenseiTool<AnimateSenseiArgs>
{
    public override string Name => "animate_sensei";

    public override string Description => "Play one of Sensei's ghost red panda animations, optionally aimed at an object.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "animation": { "type": "string", "enum": ["idle_float", "listen", "think", "speak", "point", "encourage", "demonstrate"], "description": "Animation to play." },
            "target_id": { "type": "string", "description": "Instance id Sensei points at or faces." }
          },
          "required": ["animation"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<AnimateSenseiArgs> ArgsTypeInfo => SenseiJsonContext.Default.AnimateSenseiArgs;

    public override IEnumerable<string> ReferencedInstanceIds(AnimateSenseiArgs args)
    {
        if (args.TargetId is not null) yield return args.TargetId;
    }

    public override Task<ToolResult> ExecuteAsync(AnimateSenseiArgs args, ToolContext context, CancellationToken ct)
    {
        var result = context.Locked(() =>
            args.TargetId is not null && !context.World.Exists(args.TargetId)
                ? context.Failure(ToolErrorCodes.UnknownObject, $"target_id: unknown object '{args.TargetId}'")
                : context.Success());

        if (result.IsSuccess) context.Events.Animate(args.Animation, args.TargetId);
        return Task.FromResult(result);
    }
}
