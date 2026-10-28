using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

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

    public override string? ValidateArguments(RememberNoteArgs args) =>
        PronunciationClaimGuard.Check(args.Key, args.Value) ? PronunciationClaimGuard.Detail : null;

    /// <summary>S54: at most this many distinct note keys.</summary>
    public const int MaxNotes = 30;

    public override async Task<ToolResult> ExecuteAsync(RememberNoteArgs args, ToolContext context, CancellationToken ct)
    {
        var result = context.Locked(() =>
        {
            var notes = context.Profile.Notes;
            if (!notes.ContainsKey(args.Key) && notes.Count >= MaxNotes)
                return context.Failure(ToolErrorCodes.NoteLimitExceeded, $"at most {MaxNotes} different notes; reuse a key to replace one");
            if (context.Secrets.Check(args.Key)) return context.Failure(ToolErrorCodes.LooksLikeCredential, "key: looks like a credential");
            if (context.Secrets.Check(args.Value)) return context.Failure(ToolErrorCodes.LooksLikeCredential, "value: looks like a credential");
            notes[args.Key] = args.Value;
            var data = new JsonObject { ["key"] = args.Key, ["notes_count"] = notes.Count };
            return context.Success(null, ToolContext.ToData(data));
        });
        // The note is committed: save without the call's token so a late cancel cannot turn a committed call into `cancelled` (S22).
        if (result.IsSuccess) await context.SaveProfileAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }
}
