using System.Text.Json;
using System.Text.Json.Nodes;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Learner;

/// <summary>Helpers shared by the learner-tool tests. Assistance is seeded through the foundation's assistance log API.</summary>
internal static class LearnerKit
{
    /// <summary>Records one assistance record as if a speech or hint tool had (foundation API, current turn).</summary>
    public static AssistanceRecord Assist(TestEngine t, string kind, string dimension, string key, string? taskId = null)
    {
        lock (t.Core.Sync)
        {
            var turn = t.Core.CurrentTurn!;
            var ctx = new ToolContext(t.Core, turn, "seed", "seed", turn.GenerationId, false);
            return ctx.RecordAssistance(kind, dimension, key, taskId);
        }
    }

    /// <summary>Spawns an apple and starts a task on it with one skill target; returns the task and the apple's id.</summary>
    public static (TaskView Task, string ObjectId) StartTask(TestEngine t, string dimension, string key, string? japanese = null)
    {
        var apple = Spawn(t, "apple");
        TaskView task;
        lock (t.Core.Sync)
        {
            task = t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "do it" },
                new[] { new SkillTarget { Dimension = dimension, Key = key, Japanese = japanese } },
                new[] { new TaskPredicate { Type = PredicateTypes.InRegion, ObjectId = apple, Region = "left" } },
                Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        }
        t.Core.FlushNotifications();
        return (task, apple);
    }

    public static string TargetJson(string dimension, string key, string? japanese = null)
    {
        var target = new JsonObject { ["dimension"] = dimension, ["key"] = key };
        if (japanese is not null) target["japanese"] = japanese;
        return target.ToJsonString();
    }

    public static string AttemptJson(string dimension, string key, string? excerpt, bool success = true, string assessment = "correct",
        IEnumerable<long>? eventIds = null, bool? fresh = null, string? note = null, string? taskId = null, string? uncertainty = null)
    {
        var o = new JsonObject
        {
            ["target"] = JsonNode.Parse(TargetJson(dimension, key)),
            ["communicative_success"] = success,
            ["language_assessment"] = assessment,
        };
        if (excerpt is not null) o["transcript_excerpt"] = excerpt;
        if (eventIds is not null)
        {
            var arr = new JsonArray();
            foreach (var id in eventIds) arr.Add(id);
            o["event_ids"] = arr;
        }
        if (fresh is not null) o["is_fresh_variation"] = fresh;
        if (note is not null) o["note"] = note;
        if (taskId is not null) o["task_id"] = taskId;
        if (uncertainty is not null) o["uncertainty"] = uncertainty;
        return o.ToJsonString();
    }

    /// <summary>Calls <c>record_attempt</c> in the current turn.</summary>
    public static Task<ToolResult> Attempt(TestEngine t, string dimension, string key, string? excerpt, bool success = true, string assessment = "correct",
        IEnumerable<long>? eventIds = null, bool? fresh = null, string? note = null, string? taskId = null, string? uncertainty = null) =>
        t.CallAsync("record_attempt", AttemptJson(dimension, key, excerpt, success, assessment, eventIds, fresh, note, taskId, uncertainty));

    /// <summary>Starts a new turn whose learner transcript is <paramref name="said"/> and records an attempt quoting it.</summary>
    public static async Task<ToolResult> SayAndAttempt(TestEngine t, string dimension, string key, string said, bool success = true,
        string assessment = "correct", bool? fresh = null)
    {
        t.Engine.BeginTurn(said);
        return await Attempt(t, dimension, key, said, success, assessment, null, fresh);
    }

    public static JsonElement DataOf(ToolResult r) => r.Data ?? throw new InvalidOperationException("no data: " + r.ErrorCode + " " + r.Detail);

    public static string Str(ToolResult r, string property) => DataOf(r).GetProperty(property).GetString()!;

    public static string[] StrArray(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToArray();

    public static LearnerProfile Profile(TestEngine t)
    {
        lock (t.Core.Sync) return Sensei.Engine.Learner.LearnerBook.Clone(t.Core.Learner.Profile);
    }

    public static long LastEventId(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.World.LastEventId;
    }

    /// <summary>An evidence record for rule tests.</summary>
    public static EvidenceRecord Ev(string outcome, string key = "k1", string dimension = "vocabulary", string session = "s-1", bool fresh = false,
        DateTimeOffset? at = null, bool superseded = false, int turn = 1) => new()
    {
        SessionId = session,
        TurnId = turn,
        Target = new SkillTarget { Dimension = dimension, Key = key },
        Outcome = outcome,
        CommunicativeSuccess = outcome != AttemptOutcomes.ConfirmedIncorrect,
        LanguageAssessment = outcome == AttemptOutcomes.Unresolved ? LanguageAssessments.UnclearInput : LanguageAssessments.Correct,
        IsFreshVariation = fresh,
        Superseded = superseded,
        At = at ?? new DateTimeOffset(2026, 10, 27, 9, 30, 15, TimeSpan.Zero),
    };

    /// <summary>A default profile for pure rule tests.</summary>
    public static LearnerProfile NewProfile(string level = WorkingLevels.PreN5)
    {
        var p = LearnerProfile.CreateDefault("p", new DateTimeOffset(2026, 10, 27, 9, 30, 15, TimeSpan.Zero));
        p.WorkingLevel = level;
        return p;
    }

    /// <summary>Appends an attempt (id <c>a&lt;n&gt;</c>, time +1 minute each) and applies S51; returns the record.</summary>
    public static EvidenceRecord Add(LearnerProfile p, string outcome, string key = "k1", bool fresh = false, string dimension = "vocabulary", string session = "s-1")
    {
        var at = new DateTimeOffset(2026, 10, 27, 9, 30, 15, TimeSpan.Zero).AddMinutes(p.Evidence.Count + 1);
        var e = Ev(outcome, key, dimension, session, fresh, at);
        e.AttemptId = p.NextAttemptId();
        p.Evidence.Add(e);
        LevelRules.ApplyEvidenceLevelRules(p, at);
        return e;
    }
}
