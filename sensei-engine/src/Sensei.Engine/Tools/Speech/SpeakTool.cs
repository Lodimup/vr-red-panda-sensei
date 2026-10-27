using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.Speech;

/// <summary>One segment of <c>speak.segments</c>.</summary>
public sealed class SpeakSegmentArgs
{
    public string Lang { get; set; } = "";
    public string Text { get; set; } = "";
    public string? Reading { get; set; }
    public string? Romaji { get; set; }
    public string? Translation { get; set; }
}

/// <summary>Arguments of <c>speak</c> (S37).</summary>
public sealed class SpeakArgs
{
    public List<SpeakSegmentArgs> Segments { get; set; } = new();
    public string? Pace { get; set; }
}

/// <summary><c>speak</c> (S37): the only way Sensei talks to the learner.</summary>
public sealed class SpeakTool : SenseiTool<SpeakArgs>
{
    public override string Name => "speak";

    public override string Description => "Say something to the learner out loud, with Japanese and English in separate segments; nothing else Sensei writes is ever heard.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "segments": {
              "type": "array",
              "description": "What to say, 1 to 6 segments, each in one language.",
              "minItems": 1,
              "maxItems": 6,
              "items": {
                "type": "object",
                "description": "One spoken segment.",
                "properties": {
                  "lang": { "type": "string", "enum": ["ja", "en"], "description": "Language of the text." },
                  "text": { "type": "string", "minLength": 1, "description": "Text to speak: Japanese up to 60 characters, English up to 160." },
                  "reading": { "type": "string", "description": "Kana reading of Japanese text." },
                  "romaji": { "type": "string", "description": "Romaji of Japanese text." },
                  "translation": { "type": "string", "description": "English translation of Japanese text." }
                },
                "required": ["lang", "text"],
                "additionalProperties": false
              }
            },
            "pace": { "type": "string", "enum": ["normal", "slow"], "description": "Speaking pace; defaults to the learner's pace setting." }
          },
          "required": ["segments"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<SpeakArgs> ArgsTypeInfo => SenseiJsonContext.Default.SpeakArgs;

    public override Task<ToolResult> ExecuteAsync(SpeakArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
