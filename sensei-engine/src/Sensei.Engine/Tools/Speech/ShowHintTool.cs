using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.Speech;

/// <summary><c>show_hint.content</c>; at least one field.</summary>
public sealed class ShowHintContent
{
    public string? Japanese { get; set; }
    public string? Reading { get; set; }
    public string? Romaji { get; set; }
    public string? English { get; set; }
}

/// <summary>Arguments of <c>show_hint</c> (S38).</summary>
public sealed class ShowHintArgs
{
    public string Kind { get; set; } = "";
    public ShowHintContent Content { get; set; } = new();
    public string? TargetKey { get; set; }
    public bool Speak { get; set; }
}

/// <summary><c>show_hint</c> (S38): shows a hint card, optionally spoken.</summary>
public sealed class ShowHintTool : SenseiTool<ShowHintArgs>
{
    public override string Name => "show_hint";

    public override string Description => "Show the learner a hint card (partial phrase, model answer, explanation, vocabulary card or translation), optionally reading it aloud; hints count as help.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "kind": { "type": "string", "enum": ["partial_phrase", "model_answer", "explanation", "vocabulary_card", "translation"], "description": "Kind of hint." },
            "content": {
              "type": "object",
              "description": "What the card shows; at least one field.",
              "properties": {
                "japanese": { "type": "string", "maxLength": 60, "description": "Japanese text, at most 60 characters." },
                "reading": { "type": "string", "maxLength": 60, "description": "Kana reading, at most 60 characters." },
                "romaji": { "type": "string", "maxLength": 80, "description": "Romaji, at most 80 characters." },
                "english": { "type": "string", "maxLength": 160, "description": "English text, at most 160 characters." }
              },
              "additionalProperties": false
            },
            "target_key": { "type": "string", "description": "Skill target key the hint helps with; defaults to every target of the active task." },
            "speak": { "type": "boolean", "description": "Also read the Japanese and English aloud; defaults to false." }
          },
          "required": ["kind", "content"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<ShowHintArgs> ArgsTypeInfo => SenseiJsonContext.Default.ShowHintArgs;

    public override Task<ToolResult> ExecuteAsync(ShowHintArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
