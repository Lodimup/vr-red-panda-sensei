using System.Text.Json.Serialization.Metadata;
using Sensei.Engine.Json;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.Tools.World;

namespace Sensei.Engine.Tests.Foundation;

public sealed class BlankArguments
{
    private sealed class EmptyArgumentsTool : SenseiTool<InspectSceneArgs>
    {
        public override string Name => "test_empty_arguments";
        public override string Description => "Return success after an empty argument object is validated.";
        protected override string SchemaJson => "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
        protected override JsonTypeInfo<InspectSceneArgs> ArgsTypeInfo => SenseiJsonContext.Default.InspectSceneArgs;
        public override Task<ToolResult> ExecuteAsync(InspectSceneArgs args, ToolContext context, CancellationToken ct) =>
            Task.FromResult(context.Success());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \t\n")]
    public async Task S21_blank_input_is_malformed_json_not_an_empty_object(string json)
    {
        await using var t = await TestEngine.CreateAsync(extraTools: new SenseiTool[] { new EmptyArgumentsTool() });
        t.Engine.BeginTurn("inspect the scene");

        var result = await t.Engine.InvokeToolAsync("test_empty_arguments", json);

        Assert.Equal(ToolStatuses.Failure, result.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, result.ErrorCode);
        Assert.Equal(ToolDetails.MalformedJson, result.Detail);
    }
}
