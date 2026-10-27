using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;

namespace Sensei.Engine.Tools.Learner;

/// <summary>Arguments of <c>set_support</c> (S41, S76, S103).</summary>
public sealed class SetSupportArgs
{
    public string Reason { get; set; } = "";
    public bool? ShowJapanese { get; set; }
    public bool? ShowReading { get; set; }
    public bool? ShowRomaji { get; set; }
    public bool? ShowTranslation { get; set; }
    public string? Pace { get; set; }
    public string? CorrectionStyle { get; set; }
    public string? LanguageMode { get; set; }
    public string? Difficulty { get; set; }
    public string? StartingBand { get; set; }
    public string? SelfReportedJlpt { get; set; }
}

/// <summary><c>set_support</c> (S41): changes support settings, band or difficulty.</summary>
public sealed class SetSupportTool : SenseiTool<SetSupportArgs>
{
    public override string Name => "set_support";

    public override string Description => "Change the learner's support settings (captions, pace, corrections, language mode), starting band or difficulty, because the learner asked or to adapt; fields the learner pinned cannot be changed.";

    protected override string SchemaJson => """
        {
          "type": "object",
          "properties": {
            "reason": { "type": "string", "enum": ["learner_request", "adaptation"], "description": "Whether the learner asked for this or Sensei adapts on its own." },
            "show_japanese": { "type": "boolean", "description": "Show Japanese captions." },
            "show_reading": { "type": "boolean", "description": "Show kana readings." },
            "show_romaji": { "type": "boolean", "description": "Show romaji." },
            "show_translation": { "type": "boolean", "description": "Show English translations." },
            "pace": { "type": "string", "enum": ["normal", "slow"], "description": "Speaking pace." },
            "correction_style": { "type": "string", "enum": ["light", "detailed"], "description": "How much correction the learner gets." },
            "language_mode": { "type": "string", "enum": ["english_explanations", "mixed", "japanese_only"], "description": "Language Sensei explains in." },
            "difficulty": { "type": "string", "enum": ["easier", "harder"], "description": "Make practice one step easier or harder." },
            "starting_band": { "type": "string", "enum": ["zero", "early_beginner", "developing_beginner", "intermediate", "advanced"], "description": "The learner's starting band." },
            "self_reported_jlpt": { "type": "string", "enum": ["n5", "n4", "n3", "n2", "n1"], "description": "JLPT level the learner says they have." }
          },
          "required": ["reason"],
          "additionalProperties": false
        }
        """;

    protected override JsonTypeInfo<SetSupportArgs> ArgsTypeInfo => SenseiJsonContext.Default.SetSupportArgs;

    public override Task<ToolResult> ExecuteAsync(SetSupportArgs args, ToolContext context, CancellationToken ct) =>
        Task.FromResult(context.Failure(ToolErrorCodes.InvalidArguments, ToolDetails.NotImplemented));
}
