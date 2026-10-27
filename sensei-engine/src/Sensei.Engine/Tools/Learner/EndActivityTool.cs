using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>end_activity</c> (S55).</summary>
public sealed class EndActivityArgs
{
    public string NextSuggestion { get; set; } = "";
}

/// <summary><c>end_activity</c> (S55, S88): ends the activity and shows the session summary.</summary>
public sealed class EndActivityTool : SenseiTool<EndActivityArgs>
{
    public override string Name => "end_activity";

    public override string Description => "End the current activity and show the learner a summary of what they practised; speak any recap before calling this.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "next_suggestion": { "type": "string", "minLength": 1, "maxLength": 160, "description": "What to practise next time, in one sentence." }
          },
          "required": ["next_suggestion"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<EndActivityArgs> ArgsTypeInfo => SenseiJsonContext.Default.EndActivityArgs;

    public override Task<ToolResult> ExecuteAsync(EndActivityArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
