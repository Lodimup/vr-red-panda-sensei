using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;

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

    /// <summary><c>reason</c> values.</summary>
    public const string ReasonLearnerRequest = "learner_request";

    public const string ReasonAdaptation = "adaptation";

    /// <summary>S120: detail when <c>adaptation</c> sets <c>starting_band</c> while a band is already set.</summary>
    public const string BandAlreadySetDetail = "band_already_set";

    public override string? ValidateArguments(SetSupportArgs args) =>
        ExplicitFields(args).Count == 0 && args.Difficulty is null && args.StartingBand is null && args.SelfReportedJlpt is null
            ? "arguments: at least one field besides reason is required"
            : null;

    public override async Task<ToolResult> ExecuteAsync(SetSupportArgs args, ToolContext context, CancellationToken ct)
    {
        var result = context.Locked(() =>
        {
            var profile = context.Profile;
            var fields = ExplicitFields(args);

            // S76: a pinned field is never changed by a tool call, whatever the reason. The whole call is validated before anything changes.
            foreach (var (field, _) in fields)
                if (profile.IsPinned(field))
                    return context.Failure(ToolErrorCodes.PreferencePinned, $"{field}: pinned by the learner; only the learner can change it");
            // S41, S120: adaptation may only set the band while it is unset.
            if (args.Reason == ReasonAdaptation && args.StartingBand is not null && profile.StartingBand is not null)
                return context.Failure(ToolErrorCodes.InvalidArguments, BandAlreadySetDetail);

            // S103: band defaults first (unpinned fields incl. language_mode), then explicit fields, then self-report, then difficulty.
            var session = context.Learner.CurrentSession(context.SessionId);
            var at = context.Clock.GetUtcNow();
            if (args.StartingBand is not null)
                SupportRules.ApplyStartingBand(profile, session, args.StartingBand, PreferenceActors.Sensei, at);
            foreach (var (field, value) in fields)
                SupportRules.SetField(profile, session, field, value, PreferenceActors.Sensei);
            if (args.SelfReportedJlpt is not null)
            {
                // S41, S120: the self-report is always stored; it moves the level only before evidence exists or when the learner asked.
                profile.SelfReportedJlpt = args.SelfReportedJlpt;
                if (args.Reason == ReasonLearnerRequest || !profile.Evidence.Any(e => !e.Superseded))
                    SupportRules.SetWorkingLevel(profile, args.SelfReportedJlpt, LevelChangeReasons.SelfReport, at);
            }
            if (args.Difficulty is not null)
            {
                var step = args.Difficulty == "harder" ? 1 : -1;
                var reason = args.Reason == ReasonLearnerRequest ? LevelChangeReasons.LearnerRequest : LevelChangeReasons.Adaptation;
                SupportRules.SetWorkingLevel(profile, WorkingLevels.Step(profile.WorkingLevel, step), reason, at);
            }

            context.QueueSupportChanged();
            var data = new JsonObject
            {
                ["support"] = ToolContext.ToNode(profile.Support, SenseiJsonContext.Default.SupportSettings),
                ["pinned"] = ToolContext.ToNode(profile.Pinned, SenseiJsonContext.Default.ListString),
                ["working_level"] = profile.WorkingLevel,
            };
            return context.Success(null, ToolContext.ToData(data));
        });
        // Committed: save without the call's token so a late cancel cannot turn it into `cancelled` (S22).
        if (result.IsSuccess) await context.SaveProfileAsync(CancellationToken.None).ConfigureAwait(false);
        return result;
    }

    /// <summary>The support fields the call sets explicitly, in SPEC order, with their string values.</summary>
    private static List<(string Field, string Value)> ExplicitFields(SetSupportArgs a)
    {
        var fields = new List<(string, string)>();
        if (a.ShowJapanese.HasValue) fields.Add((SupportFields.ShowJapanese, a.ShowJapanese.Value ? "true" : "false"));
        if (a.ShowReading.HasValue) fields.Add((SupportFields.ShowReading, a.ShowReading.Value ? "true" : "false"));
        if (a.ShowRomaji.HasValue) fields.Add((SupportFields.ShowRomaji, a.ShowRomaji.Value ? "true" : "false"));
        if (a.ShowTranslation.HasValue) fields.Add((SupportFields.ShowTranslation, a.ShowTranslation.Value ? "true" : "false"));
        if (a.Pace is not null) fields.Add((SupportFields.Pace, a.Pace));
        if (a.CorrectionStyle is not null) fields.Add((SupportFields.CorrectionStyle, a.CorrectionStyle));
        if (a.LanguageMode is not null) fields.Add((SupportFields.LanguageMode, a.LanguageMode));
        return fields;
    }
}
