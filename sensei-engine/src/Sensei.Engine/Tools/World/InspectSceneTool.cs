using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.World;

/// <summary>Arguments of <c>inspect_scene</c>: none.</summary>
public sealed class InspectSceneArgs
{
}

/// <summary><c>inspect_scene</c> (S29): returns the scene snapshot; never mutates.</summary>
public sealed class InspectSceneTool : SenseiTool<InspectSceneArgs>
{
    public override string Name => "inspect_scene";

    public override string Description => "Look at the practice area: every object with its id, place and Japanese name, the selected object, the active task and the learner's support settings.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<InspectSceneArgs> ArgsTypeInfo => SenseiJsonContext.Default.InspectSceneArgs;

    public override Task<ToolResult> ExecuteAsync(InspectSceneArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Read(() =>
            context.Success(null, ToolContext.ToData(context.World.Snapshot(), SenseiJsonContext.Default.SceneSnapshot))));
}
