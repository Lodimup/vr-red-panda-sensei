using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Events;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Text;

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

    /// <summary>S38a, S123: at least one non-empty content field. S80: no mention of the hidden working level.</summary>
    public override string? ValidateArguments(ShowHintArgs args)
    {
        var c = args.Content;
        if (args.Kind == "vocabulary_card"
            ? PronunciationClaimGuard.CheckLearnerClaim(c.English) || PronunciationClaimGuard.CheckJapaneseLearnerClaim(c.Japanese) || PronunciationClaimGuard.Check(args.TargetKey)
            : PronunciationClaimGuard.Check(c.Japanese, c.Reading, c.Romaji, c.English, args.TargetKey))
            return PronunciationClaimGuard.Detail;
        if (string.IsNullOrEmpty(c.Japanese) && string.IsNullOrEmpty(c.Reading) && string.IsNullOrEmpty(c.Romaji) && string.IsNullOrEmpty(c.English))
            return "content: at least one of japanese, reading, romaji, english is required";
        if (HiddenLevelGuard.Check(c.Japanese) || HiddenLevelGuard.Check(c.Reading) || HiddenLevelGuard.Check(c.Romaji) || HiddenLevelGuard.Check(c.English) || HiddenLevelGuard.Check(string.Join(" ", new[] { c.Japanese, c.Reading, c.Romaji, c.English })))
            return HiddenLevelGuard.Detail;
        return null;
    }

    /// <summary>S123: an empty content string is an absent field.</summary>
    private static ShowHintContent Present(ShowHintContent c) => new()
    {
        Japanese = string.IsNullOrEmpty(c.Japanese) ? null : c.Japanese,
        Reading = string.IsNullOrEmpty(c.Reading) ? null : c.Reading,
        Romaji = string.IsNullOrEmpty(c.Romaji) ? null : c.Romaji,
        English = string.IsNullOrEmpty(c.English) ? null : c.English,
    };

    public override async Task<ToolResult> ExecuteAsync(ShowHintArgs args, ToolContext context, CancellationToken ct)
    {
        var c = Present(args.Content);

        // S38c: speak: true says `japanese` as a ja segment and `english` as an en segment, under the rules of S37.
        List<SpeechSegment>? segments = null;
        var characters = 0;
        if (args.Speak)
        {
            segments = new List<SpeechSegment>();
            if (c.Japanese is not null) segments.Add(new SpeechSegment(SpeechLanguages.Japanese, c.Japanese, c.Reading, c.Romaji, null));
            if (c.English is not null) segments.Add(new SpeechSegment(SpeechLanguages.English, c.English, null, null, null));
            if (segments.Count == 0) return context.Failure(ToolErrorCodes.InvalidArguments, "speak: needs content.japanese or content.english to say");
            if (c.Japanese is not null && !TextRules.ContainsJapanese(c.Japanese) || c.English is not null && TextRules.ContainsJapanese(c.English))
                return context.Failure(ToolErrorCodes.InvalidArguments, SpeechPresenter.SplitLanguageSegments);
            characters = SpeechPresenter.CountCharacters(segments);
        }

        var card = new HintCard
        {
            Kind = args.Kind,
            Japanese = c.Japanese,
            Reading = c.Reading,
            Romaji = c.Romaji,
            English = c.English,
            TargetKey = args.TargetKey,
        };

        SpeechPlan? plan = null;
        var gate = context.Read(() =>
        {
            // S37c
            if (segments is not null && context.Turn.SpokenCharacters + characters > SpeechPresenter.MaxSpokenCharactersPerTurn)
                return context.Failure(ToolErrorCodes.SpeechLimitExceeded,
                    $"{characters} more characters would pass the {SpeechPresenter.MaxSpokenCharactersPerTurn} spoken per turn ({context.Turn.SpokenCharacters} already spoken)");

            // S38b, S89: the hint is on its way to the learner, so its assistance is recorded now, whatever the speech does next.
            var targets = SpeechPresenter.ActiveTaskTargets(context);
            RecordAssistance(context, args, targets);
            if (segments is not null)
                plan = SpeechPresenter.Plan(context, segments, null, characters, targets, isReplay: false);
            return context.Success();
        });
        if (gate.Status != ToolStatuses.Success) return gate;

        context.Events.Hint(card.Clone());
        var cardNode = ToolContext.ToNode(card, SenseiJsonContext.Default.HintCard);
        if (plan is null)
            return context.Success(null, ToolContext.ToData(new JsonObject { ["card"] = cardNode }));
        return await SpeechPresenter.PresentAsync(context, plan, ct, cardNode).ConfigureAwait(false);
    }

    /// <summary>S38b: the kind (translation → translation_hint) for <c>target_key</c> if given, else every target of the active task. Under the lock.</summary>
    private static void RecordAssistance(ToolContext context, ShowHintArgs args, IReadOnlyList<AidTarget> taskTargets)
    {
        var kind = args.Kind == "translation" ? AssistanceKinds.TranslationHint : args.Kind;
        if (args.TargetKey is null)
        {
            foreach (var t in taskTargets) context.RecordAssistance(kind, t.Dimension, t.Key, t.TaskId);
            return;
        }

        var named = taskTargets.Where(t => t.Key == args.TargetKey).ToList();
        if (named.Count > 0)
        {
            foreach (var t in named) context.RecordAssistance(kind, t.Dimension, t.Key, t.TaskId);
            return;
        }
        // Not a target of the active task: a key-only record matches every dimension of that key (S97).
        context.RecordAssistance(kind, null, args.TargetKey, context.Tasks.Active?.TaskId);
    }
}
