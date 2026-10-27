using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Sensei.Engine.Json;
using Sensei.Engine.Learner;
using Sensei.Engine.World;

namespace Sensei.Engine.Agent;

/// <summary>
/// Renders the per-turn context (S63, S76). Engine-owned facts go into the instruction text; everything the learner
/// said or influenced (transcript, correction, notes, player actions) goes into one <c>&lt;learner_data&gt;</c> block.
/// </summary>
internal sealed class ContextRenderer
{
    /// <summary>Model requests allowed per turn (S64).</summary>
    public const int MaxRounds = 4;

    private static readonly Regex TagBreakout = new(@"<(\s*/?\s*)(" + SystemPrompt.LearnerDataTag + ")", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly EngineCore _core;

    public ContextRenderer(EngineCore core)
    {
        _core = core;
    }

    /// <summary>The full instruction text for one model request: the system prompt plus the current engine-owned state.</summary>
    public string RenderInstructions(TurnScope scope) => SystemPrompt.Text + "\n\n" + RenderState(scope);

    /// <summary>S63: the engine-owned state — band, working level, support, pins, recommendations, review, scene, task, budgets.</summary>
    public string RenderState(TurnScope scope)
    {
        string snapshotJson;
        string band;
        string workingLevel;
        string? selfReported;
        string supportLine;
        string pinned;
        string recommendations;
        string reviewDue;
        string languageMode;
        int callsLeft;
        int turnId;
        lock (_core.Sync)
        {
            var profile = _core.Learner.Profile;
            snapshotJson = JsonSerializer.Serialize(_core.World.Snapshot(), SenseiJsonContext.Default.SceneSnapshot);
            band = profile.StartingBand ?? "not chosen yet";
            workingLevel = profile.WorkingLevel;
            selfReported = profile.SelfReportedJlpt;
            languageMode = profile.Support.LanguageMode;
            supportLine = string.Join(", ", SupportFields.All.Select(f => f + "=" + profile.Support.GetFieldValue(f)));
            pinned = profile.Pinned.Count == 0 ? "none" : string.Join(", ", profile.Pinned);
            var recs = EvidenceRules.ComputeRecommendations(profile.Evidence);
            recommendations = recs.Count == 0 ? "none" : string.Join(", ", recs.Select(r => $"{r.Key} ({r.Dimension}): {r.Kind}"));
            var due = EvidenceRules.ComputeReviewDue(profile.Evidence, _core.SessionId);
            reviewDue = due.Count == 0 ? "none" : string.Join(", ", due);
            callsLeft = scope.Turn.RemainingCalls;
            turnId = (int)scope.Turn.TurnId;
        }

        var round = scope.Rounds == 0 ? 1 : scope.Rounds;
        var sb = new StringBuilder();
        sb.AppendLine("## Current state (engine-owned, refreshed before every model request)");
        sb.AppendLine($"- turn {turnId.ToString(CultureInfo.InvariantCulture)}: model round {round.ToString(CultureInfo.InvariantCulture)} of {MaxRounds.ToString(CultureInfo.InvariantCulture)}; {(MaxRounds - round).ToString(CultureInfo.InvariantCulture)} more model requests allowed after this one; {callsLeft.ToString(CultureInfo.InvariantCulture)} tool calls left this turn.");
        sb.AppendLine($"- starting band: {band}");
        sb.AppendLine($"- working level (JLPT, hidden from the learner — never state it): {workingLevel}");
        if (selfReported is not null) sb.AppendLine($"- the learner reported their own level as: {selfReported}");
        sb.AppendLine($"- language mode: {languageMode}");
        sb.AppendLine($"- support settings: {supportLine}");
        sb.AppendLine($"- pinned by the learner (never change): {pinned}");
        sb.AppendLine($"- recommendations: {recommendations}");
        sb.AppendLine($"- review due: {reviewDue}");
        sb.AppendLine($"- scene snapshot (selected object, active task and support included): {snapshotJson}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// S76: the user message of the turn — one delimited block holding the transcript, the correction note, the
    /// interruption note, the learner's actions since the previous turn and (when <paramref name="includeNotes"/>) the saved notes.
    /// The persisted copy leaves the notes out so history does not repeat them every turn.
    /// </summary>
    public string RenderLearnerMessage(TurnScope scope, bool includeNotes)
    {
        var request = scope.Request;
        var lines = new List<string>();
        var secrets = _core.Secrets;

        if (request.CorrectedFrom is not null)
        {
            lines.Add($"transcript_correction: the previous learner transcript was corrected from \"{Safe(request.CorrectedFrom)}\" to \"{Safe(request.Transcript)}\"; the corrected text is what the learner said");
        }
        if (request.PreviousTurnInterrupted)
        {
            lines.Add("previous_turn: it was interrupted before it finished");
            if (scope.InterruptedTranscript is not null) lines.Add($"interrupted_learner_transcript: {Safe(scope.InterruptedTranscript)}");
        }
        lines.Add($"learner_transcript: {Safe(request.Transcript)}");

        List<string> events;
        Dictionary<string, string>? notes = null;
        lock (_core.Sync)
        {
            events = _core.World.EventsAfter(scope.PlayerEventMark)
                .Where(e => e.Actor == SceneActors.Player && (e.Kind == SceneEventKinds.Moved || e.Kind == SceneEventKinds.Selected || e.Kind == SceneEventKinds.TaskCompleted))
                .Select(DescribeEvent)
                .ToList();
            if (includeNotes) notes = new Dictionary<string, string>(_core.Learner.Profile.Notes);
            if (includeNotes) scope.ShownEventsUpTo = _core.World.LastEventId;
        }
        if (events.Count > 0)
        {
            lines.Add("learner_actions_since_previous_turn:");
            lines.AddRange(events.Select(e => "- " + e));
        }
        if (notes is { Count: > 0 })
        {
            lines.Add("saved_notes:");
            foreach (var kv in notes.OrderBy(k => k.Key, System.StringComparer.Ordinal))
                lines.Add($"- {Safe(kv.Key)}: {Safe(kv.Value)}");
        }

        var sb = new StringBuilder();
        sb.Append('<').Append(SystemPrompt.LearnerDataTag).Append(">\n");
        foreach (var line in lines) sb.Append(line).Append('\n');
        sb.Append("</").Append(SystemPrompt.LearnerDataTag).Append('>');
        return sb.ToString();

        string Safe(string text) => Neutralize(secrets.Sanitize(text) ?? "");
    }

    private static string DescribeEvent(SceneEvent e)
    {
        var ids = string.Join(", ", e.InstanceIds);
        switch (e.Kind)
        {
            case SceneEventKinds.Moved:
                return $"moved {ids} from {e.From ?? "?"} to {e.To ?? "?"}";
            case SceneEventKinds.Selected:
                return $"selected {ids}";
            default:
                return $"completed the active task {e.TaskId ?? ""}".TrimEnd();
        }
    }

    /// <summary>Keeps the learner's text from closing or reopening the data block.</summary>
    internal static string Neutralize(string text) => TagBreakout.Replace(text, "&lt;$1$2");
}
