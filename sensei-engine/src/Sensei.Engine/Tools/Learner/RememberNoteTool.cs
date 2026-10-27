using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>remember_note</c> (S54).</summary>
public sealed class RememberNoteArgs
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary><c>remember_note</c> (S54): stores a note about the learner for later sessions.</summary>
public sealed class RememberNoteTool : SenseiTool<RememberNoteArgs>
{
    public override string Name => "remember_note";

    public override string Description => "Remember a short note about the learner (an interest, a goal, a preference) for later sessions; never store secrets.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "key": { "type": "string", "pattern": "^[a-z][a-z0-9_]{0,31}$", "description": "Note key, lowercase letters, digits and underscores." },
            "value": { "type": "string", "minLength": 1, "maxLength": 120, "description": "Note text." }
          },
          "required": ["key", "value"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<RememberNoteArgs> ArgsTypeInfo => SenseiJsonContext.Default.RememberNoteArgs;

    public override Task<ToolResult> ExecuteAsync(RememberNoteArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
