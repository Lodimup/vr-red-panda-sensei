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
/// Renders the per-turn context (S63, S76). The instruction text holds engine-authored policy and engine-computed facts only
/// (ids, counts, statuses, enums, budgets). Everything a learner said or influenced goes into one <c>&lt;learner_data&gt;</c> block
/// in the user message: the transcript, correction and interruption notes, the player's actions as scene events, saved notes, and
/// the text Sensei itself wrote earlier that can quote the learner (the active task's instruction, targets and role, and the
/// target keys of the recommendations and the review queue).
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

    /// <summary>S63: the engine-owned state — band, working level, support, pins, counts, scene, budgets. No text a model or learner wrote.</summary>
    public string RenderState(TurnScope scope)
    {
        string snapshotJson;
        string band;
        string workingLevel;
        string? selfReported;
        string supportLine;
        string pinned;
        string activeTask;
        int recommendationCount;
        int reviewDueCount;
        string languageMode;
        int callsLeft;
        lock (_core.Sync)
        {
            var profile = _core.Learner.Profile;
            var snapshot = _core.World.Snapshot();
            var task = snapshot.ActiveTask;
            snapshot.ActiveTask = null;
            snapshotJson = JsonSerializer.Serialize(snapshot, SenseiJsonContext.Default.SceneSnapshot);
            activeTask = task is null ? "none" : $"{task.TaskId} (family {task.Family}, status {task.Status}); its text and targets are in the {SystemPrompt.LearnerDataTag} block";
            band = profile.StartingBand ?? "not chosen yet";
            workingLevel = profile.WorkingLevel;
            selfReported = profile.SelfReportedJlpt;
            languageMode = profile.Support.LanguageMode;
            supportLine = string.Join(", ", SupportFields.All.Select(f => f + "=" + profile.Support.GetFieldValue(f)));
            pinned = profile.Pinned.Count == 0 ? "none" : string.Join(", ", profile.Pinned);
            recommendationCount = EvidenceRules.ComputeRecommendations(profile.Evidence).Count;
            reviewDueCount = EvidenceRules.ComputeReviewDue(profile.Evidence, _core.SessionId).Count;
            callsLeft = scope.Turn.RemainingCalls;
        }

        var round = scope.Rounds == 0 ? 1 : scope.Rounds;
        var roundsLeft = System.Math.Max(0, MaxRounds - round);
        var sb = new StringBuilder();
        sb.AppendLine("## Current state (engine-owned, refreshed before every model request)");
        sb.AppendLine($"- tool calls remaining this turn: {callsLeft.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- model rounds remaining after this request: {roundsLeft.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"- starting band: {band}");
        sb.AppendLine($"- working level (JLPT, hidden from the learner — never state it): {workingLevel}");
        if (selfReported is not null) sb.AppendLine($"- the learner reported their own level as: {selfReported}");
        sb.AppendLine($"- language mode: {languageMode}");
        sb.AppendLine($"- support settings: {supportLine}");
        sb.AppendLine($"- pinned by the learner (never change): {pinned}");
        sb.AppendLine($"- recommendations: {recommendationCount.ToString(CultureInfo.InvariantCulture)} (the targets are in the {SystemPrompt.LearnerDataTag} block)");
        sb.AppendLine($"- review due: {reviewDueCount.ToString(CultureInfo.InvariantCulture)} (the targets are in the {SystemPrompt.LearnerDataTag} block)");
        sb.AppendLine($"- active task: {activeTask}");
        sb.AppendLine($"- scene snapshot (selected object and support included): {snapshotJson}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// S76: the user message of the turn — one delimited block holding the transcript, the correction note, the interruption
    /// note, the player's events since the previous turn and, when <paramref name="includeCurrent"/>, the saved notes and the
    /// current task and target text. The persisted copy leaves those out so history does not repeat them every turn.
    /// </summary>
    public string RenderLearnerMessage(TurnScope scope, bool includeCurrent)
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
        string? task = null;
        List<string>? recommendations = null;
        List<string>? reviewDue = null;
        lock (_core.Sync)
        {
            events = _core.World.EventsAfter(scope.PlayerEventMark)
                .Where(e => e.Actor == SceneActors.Player)
                .Select(e => JsonSerializer.Serialize(e, SenseiJsonContext.Default.SceneEvent))
                .ToList();
            if (includeCurrent)
            {
                var profile = _core.Learner.Profile;
                notes = new Dictionary<string, string>(profile.Notes);
                if (_core.Tasks.Active is { } active) task = JsonSerializer.Serialize(active, SenseiJsonContext.Default.TaskView);
                recommendations = EvidenceRules.ComputeRecommendations(profile.Evidence)
                    .Select(r => $"{{\"key\":{Quote(r.Key)},\"dimension\":{Quote(r.Dimension)},\"kind\":{Quote(r.Kind)}}}").ToList();
                reviewDue = EvidenceRules.ComputeReviewDue(profile.Evidence, _core.SessionId)
                    .Select(d => $"{{\"key\":{Quote(d.Key)},\"dimension\":{Quote(d.Dimension)}}}").ToList();
                scope.ShownEventsUpTo = _core.World.LastEventId;
            }
        }
        if (events.Count > 0)
        {
            lines.Add("player_events_since_previous_turn (scene events whose actor is the player):");
            lines.AddRange(events.Select(e => Safe(e)));
        }
        if (notes is { Count: > 0 })
        {
            lines.Add("saved_notes:");
            foreach (var kv in notes.OrderBy(k => k.Key, System.StringComparer.Ordinal))
                lines.Add($"- {Safe(kv.Key)}: {Safe(kv.Value)}");
        }
        if (task is not null) lines.Add("active_task (written by Sensei earlier, may quote the learner): " + Safe(task));
        if (recommendations is { Count: > 0 }) lines.Add("recommendations (targets named by Sensei earlier): [" + Safe(string.Join(",", recommendations)) + "]");
        if (reviewDue is { Count: > 0 }) lines.Add("review_due (targets named by Sensei earlier): [" + Safe(string.Join(",", reviewDue)) + "]");

        var sb = new StringBuilder();
        sb.Append('<').Append(SystemPrompt.LearnerDataTag).Append(">\n");
        foreach (var line in lines) sb.Append(line).Append('\n');
        sb.Append("</").Append(SystemPrompt.LearnerDataTag).Append('>');
        return sb.ToString();

        string Safe(string text) => Neutralize(secrets.Sanitize(text) ?? "");
    }

    private static string Quote(string text) => JsonSerializer.Serialize(text, SenseiJsonContext.Default.String);

    /// <summary>Keeps the learner's text from closing or reopening the data block.</summary>
    internal static string Neutralize(string text) => TagBreakout.Replace(text, "&lt;$1$2");
}
