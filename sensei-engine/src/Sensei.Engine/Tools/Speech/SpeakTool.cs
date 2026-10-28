using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Text;

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

    /// <summary>S80: presentation text must not mention the hidden working level.</summary>
    public override string? ValidateArguments(SpeakArgs args)
    {
        foreach (var s in args.Segments)
            if (HiddenLevelGuard.Check(s.Text) || HiddenLevelGuard.Check(s.Reading) || HiddenLevelGuard.Check(s.Romaji) || HiddenLevelGuard.Check(s.Translation))
                return HiddenLevelGuard.Detail;
        if (HiddenLevelGuard.Check(string.Join(" ", args.Segments.Select(s => s.Text)))) return HiddenLevelGuard.Detail;
        return null;
    }

    public override async Task<ToolResult> ExecuteAsync(SpeakArgs args, ToolContext context, CancellationToken ct)
    {
        // S37a, in document order.
        for (var i = 0; i < args.Segments.Count; i++)
        {
            var s = args.Segments[i];
            var length = TextRules.ScalarLength(s.Text);
            var max = s.Lang == SpeechLanguages.Japanese ? SpeechPresenter.MaxJapaneseCharacters : SpeechPresenter.MaxEnglishCharacters;
            if (length > max)
                return context.Failure(ToolErrorCodes.SpeechLimitExceeded, $"segments[{i}].text: {length} characters, at most {max} for lang '{s.Lang}'");
        }

        // S37b, in document order.
        for (var i = 0; i < args.Segments.Count; i++)
        {
            var s = args.Segments[i];
            var hasJapanese = TextRules.ContainsJapanese(s.Text);
            if (s.Lang == SpeechLanguages.Japanese && !hasJapanese || s.Lang == SpeechLanguages.English && hasJapanese)
                return context.Failure(ToolErrorCodes.InvalidArguments, SpeechPresenter.SplitLanguageSegments);
        }

        var segments = args.Segments.Select(s => new SpeechSegment(s.Lang, s.Text, s.Reading, s.Romaji, s.Translation)).ToList();
        var characters = SpeechPresenter.CountCharacters(segments);
        SpeechPlan? plan = null;
        var gate = context.Read(() =>
        {
            // S37c
            if (context.Turn.SpokenCharacters + characters > SpeechPresenter.MaxSpokenCharactersPerTurn)
                return context.Failure(ToolErrorCodes.SpeechLimitExceeded,
                    $"{characters} more characters would pass the {SpeechPresenter.MaxSpokenCharactersPerTurn} spoken per turn ({context.Turn.SpokenCharacters} already spoken)");
            plan = SpeechPresenter.Plan(context, segments, args.Pace, characters, SpeechPresenter.ActiveTaskTargets(context), isReplay: false);
            return context.Success();
        });
        if (plan is null) return gate;
        return await SpeechPresenter.PresentAsync(context, plan, ct).ConfigureAwait(false);
    }
}
