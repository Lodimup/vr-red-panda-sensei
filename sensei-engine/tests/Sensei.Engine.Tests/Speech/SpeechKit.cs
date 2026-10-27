using System.Text.Json;
using System.Text.Json.Nodes;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Speech;

/// <summary>Helpers shared by the speech-tool tests.</summary>
internal static class SpeechKit
{
    /// <summary>A scalar outside the BMP that is a CJK ideograph (a surrogate pair in UTF-16).</summary>
    public static readonly string NonBmpKanji = char.ConvertFromUtf32(0x20BB7);

    /// <summary>A scalar outside the BMP that is not Japanese (an emoji).</summary>
    public static readonly string NonBmpEmoji = char.ConvertFromUtf32(0x1F600);

    public static string Repeat(string unit, int count) => string.Concat(Enumerable.Repeat(unit, count));

    /// <summary>An English text of exactly <paramref name="scalars"/> Unicode scalar values.</summary>
    public static string English(int scalars) => Repeat("a", scalars);

    /// <summary>A Japanese text of exactly <paramref name="scalars"/> Unicode scalar values.</summary>
    public static string Japanese(int scalars) => Repeat("あ", scalars);

    /// <summary>A new engine with a manual turn already begun.</summary>
    public static async Task<TestEngine> NewAsync(RecordingSpeechOutput? speech = null, RecordingEvents? events = null)
    {
        var t = await TestEngine.CreateAsync(speech: speech, events: events);
        t.Engine.BeginTurn("let's practise");
        return t;
    }

    public static JsonObject Segment(string lang, string text, string? reading = null, string? romaji = null, string? translation = null)
    {
        var o = new JsonObject { ["lang"] = lang, ["text"] = text };
        if (reading is not null) o["reading"] = reading;
        if (romaji is not null) o["romaji"] = romaji;
        if (translation is not null) o["translation"] = translation;
        return o;
    }

    public static string SpeakJson(string? pace, params JsonObject[] segments)
    {
        var arr = new JsonArray();
        foreach (var s in segments) arr.Add(s);
        var o = new JsonObject { ["segments"] = arr };
        if (pace is not null) o["pace"] = pace;
        return o.ToJsonString();
    }

    public static Task<ToolResult> Speak(TestEngine t, params JsonObject[] segments) => t.CallAsync("speak", SpeakJson(null, segments));

    public static Task<ToolResult> SpeakOne(TestEngine t, string lang, string text, string? pace = null) =>
        t.CallAsync("speak", SpeakJson(pace, Segment(lang, text)));

    public static string HintJson(string kind, JsonObject content, string? targetKey = null, bool? speak = null)
    {
        var o = new JsonObject { ["kind"] = kind, ["content"] = content };
        if (targetKey is not null) o["target_key"] = targetKey;
        if (speak is not null) o["speak"] = speak;
        return o.ToJsonString();
    }

    public static JsonObject Content(string? japanese = null, string? reading = null, string? romaji = null, string? english = null)
    {
        var o = new JsonObject();
        if (japanese is not null) o["japanese"] = japanese;
        if (reading is not null) o["reading"] = reading;
        if (romaji is not null) o["romaji"] = romaji;
        if (english is not null) o["english"] = english;
        return o;
    }

    public static Task<ToolResult> Hint(TestEngine t, string kind, JsonObject content, string? targetKey = null, bool? speak = null) =>
        t.CallAsync("show_hint", HintJson(kind, content, targetKey, speak));

    public static Task<ToolResult> Replay(TestEngine t, string? utteranceId = null, string? pace = null)
    {
        var o = new JsonObject();
        if (utteranceId is not null) o["utterance_id"] = utteranceId;
        if (pace is not null) o["pace"] = pace;
        return t.CallAsync("replay_speech", o.ToJsonString());
    }

    public static string DataString(ToolResult r, string name) => r.Data!.Value.GetProperty(name).GetString()!;

    public static bool HasData(ToolResult r, string name) => r.Data is { ValueKind: JsonValueKind.Object } d && d.TryGetProperty(name, out _);

    /// <summary>Starts a task on a fresh apple with the given skill targets (dimension, key, japanese).</summary>
    public static TaskView StartTask(TestEngine t, params (string Dimension, string Key, string? Japanese)[] targets)
    {
        var apple = Spawn(t, "apple");
        TaskView task;
        lock (t.Core.Sync)
        {
            task = t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = "do it" },
                targets.Select(x => new SkillTarget { Dimension = x.Dimension, Key = x.Key, Japanese = x.Japanese }).ToList(),
                new[] { new TaskPredicate { Type = PredicateTypes.InRegion, ObjectId = apple, Region = "left" } },
                Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        }
        t.Core.FlushNotifications();
        return task;
    }

    /// <summary>A copy of the assistance log.</summary>
    public static IReadOnlyList<AssistanceRecord> Assistance(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.Assistance.Records.ToList();
    }

    public static IReadOnlyList<AssistanceRecord> Assistance(TestEngine t, string kind) => Assistance(t).Where(r => r.Kind == kind).ToList();

    /// <summary>A copy of the speech history, oldest first.</summary>
    public static IReadOnlyList<SpeechHistoryEntry> History(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.SpeechHistory.Entries.ToList();
    }

    public static int SpokenCharacters(TestEngine t)
    {
        lock (t.Core.Sync) return t.Core.CurrentTurn!.SpokenCharacters;
    }

    /// <summary>A speech port whose outcome is chosen per request; presented counts are explicit.</summary>
    public static Func<SpeechRequest, SpeechOutcome> Outcome(string status, int presented, string? error = null) =>
        _ => new SpeechOutcome(status, error, presented);

}
