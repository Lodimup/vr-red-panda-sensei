using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Text;

namespace Sensei.Engine.Tools.Speech;

/// <summary>A skill target an utterance or hint is aid for: dimension (null = every dimension of the key), key, the target's Japanese text and the task.</summary>
internal sealed record AidTarget(string? Dimension, string Key, string? Japanese, string? TaskId);

/// <summary>Everything decided under the engine lock before an utterance is handed to the speech port.</summary>
internal sealed class SpeechPlan
{
    public SpeechPlan(string utteranceId, IReadOnlyList<SpeechSegment> segments, string pace, SpeechCaptions captions, long sequence,
        int reservedCharacters, IReadOnlyList<AidTarget> targets, bool isReplay)
    {
        UtteranceId = utteranceId;
        Segments = segments;
        Pace = pace;
        Captions = captions;
        Sequence = sequence;
        ReservedCharacters = reservedCharacters;
        Targets = targets;
        IsReplay = isReplay;
    }

    public string UtteranceId { get; }
    public IReadOnlyList<SpeechSegment> Segments { get; }
    public string Pace { get; }
    public SpeechCaptions Captions { get; }

    /// <summary>Interaction-sequence position of the presentation, taken when it starts (S92, S94).</summary>
    public long Sequence { get; }

    /// <summary>Characters counted against the per-turn total (S37c) before the speech starts; zero for a replay.</summary>
    public int ReservedCharacters { get; }

    /// <summary>The active task's skill targets when the speech started.</summary>
    public IReadOnlyList<AidTarget> Targets { get; }

    public bool IsReplay { get; }
}

/// <summary>Shared mechanics of <c>speak</c>, <c>show_hint</c> with <c>speak: true</c> and <c>replay_speech</c> (S37, S38c, S39, S89, S90).</summary>
internal static class SpeechPresenter
{
    /// <summary>S37c: spoken characters per turn.</summary>
    public const int MaxSpokenCharactersPerTurn = 480;

    public const int MaxJapaneseCharacters = 60;
    public const int MaxEnglishCharacters = 160;

    /// <summary>S37b detail.</summary>
    public const string SplitLanguageSegments = "split_language_segments";

    /// <summary>Total length in Unicode scalar values of the segment texts.</summary>
    public static int CountCharacters(IEnumerable<SpeechSegment> segments) => segments.Sum(s => TextRules.ScalarLength(s.Text));

    /// <summary>The active task's skill targets as aid targets. Under the engine lock.</summary>
    public static IReadOnlyList<AidTarget> ActiveTaskTargets(ToolContext ctx)
    {
        var task = ctx.Tasks.Active;
        if (task is null) return Array.Empty<AidTarget>();
        return task.SkillTargets.Select(t => new AidTarget(t.Dimension, t.Key, t.Japanese, task.TaskId)).ToList();
    }

    /// <summary>
    /// Allocates the utterance id and sequence position, reads pace and caption visibility from the current support,
    /// and counts <paramref name="reservedCharacters"/> against the turn. Under the engine lock, after the checks passed.
    /// </summary>
    public static SpeechPlan Plan(ToolContext ctx, IReadOnlyList<SpeechSegment> segments, string? pace, int reservedCharacters,
        IReadOnlyList<AidTarget> targets, bool isReplay)
    {
        var support = ctx.Profile.Support;
        var captions = new SpeechCaptions(support.ShowJapanese, support.ShowReading, support.ShowRomaji, support.ShowTranslation);
        ctx.Turn.SpokenCharacters += reservedCharacters;
        return new SpeechPlan(ctx.NextSpeechUtteranceId(), segments, pace ?? support.Pace, captions, ctx.NextSequence(), reservedCharacters, targets, isReplay);
    }

    /// <summary>
    /// Hands the plan to the speech port, then commits what reached the learner (S89): the utterance enters speech history
    /// when at least one segment was presented, with its audio-model or repetition assistance, even if the call then fails or is cancelled.
    /// Characters of segments that never reached the learner are given back to the turn's total.
    /// </summary>
    public static async Task<ToolResult> PresentAsync(ToolContext ctx, SpeechPlan plan, CancellationToken ct, JsonNode? card = null)
    {
        var request = new SpeechRequest(plan.UtteranceId, ctx.GenerationId, plan.Segments, plan.Pace, plan.Captions);
        SpeechOutcome outcome;
        try
        {
            outcome = await ctx.Speech.SpeakAsync(request, ct).ConfigureAwait(false)
                ?? new SpeechOutcome(SpeechStatuses.Failed, "speech output returned no outcome");
        }
        catch (OperationCanceledException)
        {
            outcome = new SpeechOutcome(SpeechStatuses.Cancelled, null);
        }
        catch (Exception ex)
        {
            outcome = new SpeechOutcome(SpeechStatuses.Failed, ex.Message);
        }

        var presented = outcome.PresentedOf(plan.Segments.Count);
        lock (ctx.Sync)
        {
            // S87: a reset while the speech ran wiped the session; late work changes nothing.
            if (ctx.Utterances.Find(ctx.Turn.Utterance.Id) is null) return ctx.Cancelled();

            if (plan.ReservedCharacters > 0)
            {
                var unspoken = plan.Segments.Skip(presented).Sum(s => TextRules.ScalarLength(s.Text));
                ctx.Turn.SpokenCharacters = Math.Max(0, ctx.Turn.SpokenCharacters - unspoken);
            }
            if (presented >= 1) CommitPresented(ctx, plan, outcome, presented);
        }

        switch (outcome.Status)
        {
            case SpeechStatuses.Completed:
                return ctx.Success(null, Data(plan, outcome, presented, card, withPresented: false));
            case SpeechStatuses.Cancelled:
                var cancelled = ctx.Cancelled();
                // S37d, S90: a cancelled speech that entered history names its utterance.
                if (presented >= 1) cancelled.Data = Data(plan, outcome, presented, card, withPresented: false);
                return cancelled;
            default:
                var reason = string.IsNullOrWhiteSpace(outcome.Error) ? "speech output failed" : outcome.Error!;
                var detail = $"speech failed after {presented} of {plan.Segments.Count} segments were presented: {reason}";
                return ctx.Failure(ToolErrorCodes.SpeechFailed, detail, presented >= 1 ? Data(plan, outcome, presented, null, withPresented: true) : null);
        }
    }

    private static void CommitPresented(ToolContext ctx, SpeechPlan plan, SpeechOutcome outcome, int presented)
    {
        var heard = plan.Segments.Take(presented).ToList();
        ctx.SpeechHistory.Add(new SpeechHistoryEntry
        {
            UtteranceId = plan.UtteranceId,
            Segments = heard,
            Pace = plan.Pace,
            TurnId = ctx.TurnId,
            GenerationId = ctx.GenerationId,
            SegmentsPresented = presented,
            Outcome = outcome.Status,
            IsReplay = plan.IsReplay,
            Sequence = plan.Sequence,
        });

        if (plan.IsReplay)
        {
            // S39, S90: a replay is repetition help, never an audio model.
            foreach (var target in plan.Targets)
                RecordAt(ctx, plan, AssistanceKinds.Repetition, target);
            return;
        }

        // S37e, S89: audio_model only when a presented ja segment contains the target's Japanese (NFKC, whitespace removed).
        var spoken = heard.Where(s => s.Lang == SpeechLanguages.Japanese).Select(s => TextRules.NormalizeNoWhitespace(s.Text)).ToList();
        foreach (var target in plan.Targets)
        {
            var needle = TextRules.NormalizeNoWhitespace(target.Japanese);
            if (needle.Length == 0) continue;
            if (spoken.Any(text => text.Contains(needle))) RecordAt(ctx, plan, AssistanceKinds.AudioModel, target);
        }
    }

    private static void RecordAt(ToolContext ctx, SpeechPlan plan, string kind, AidTarget target)
    {
        var record = ctx.RecordAssistance(kind, target.Dimension, target.Key, target.TaskId);
        record.Sequence = plan.Sequence; // the presentation happened when the speech started (S94)
    }

    private static System.Text.Json.JsonElement Data(SpeechPlan plan, SpeechOutcome outcome, int presented, JsonNode? card, bool withPresented)
    {
        var data = new JsonObject();
        if (card is not null) data["card"] = card;
        data["utterance_id"] = plan.UtteranceId;
        data["outcome"] = outcome.Status;
        if (withPresented) data["segments_presented"] = presented;
        return ToolContext.ToData(data);
    }
}
