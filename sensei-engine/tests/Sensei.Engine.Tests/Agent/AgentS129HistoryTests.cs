using Microsoft.Extensions.AI;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.Foundation;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

/// <summary>
/// S129: history keeps, per learner turn, only the learner's transcript (inside the learner_data tags) plus the model's tool calls
/// and results. Player events, notes, task text and the like are rebuilt for each request and never persisted.
/// </summary>
public sealed class AgentS129HistoryTests
{
    private const string TaskText = "TASK_TEXT_MARKER put the apple on the right";
    private const string NoteText = "NOTE_TEXT_MARKER";

    private static void StartTask(TestEngine t)
    {
        lock (t.Core.Sync)
        {
            t.Core.Tasks.CommitStart(TaskFamilies.ListenArrange, new TaskInstruction { English = TaskText },
                new[] { new SkillTarget { Dimension = SkillDimensions.Listening, Key = "TARGET_KEY_MARKER", Japanese = "りんご" } },
                new[] { new TaskPredicate { Type = PredicateTypes.InRegion, ObjectId = "apple_1", Region = "right" } },
                Array.Empty<ScenarioProperty>(), null, false, t.Core.CurrentTurnId);
        }
        t.Core.FlushNotifications();
    }

    /// <summary>Every user message of a request except the last (the current turn's rebuilt block): what history carried.</summary>
    private static IReadOnlyList<string> StoredLearnerMessages(RecordedChatRequest request) =>
        UserMessages(request).SkipLast(1).Select(m => m.Text).ToList();

    [Fact]
    public async Task S129_a_later_request_keeps_the_old_transcript_but_not_the_old_events_notes_or_task_text()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b").EnqueueText("c");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        lock (t.Core.Sync) t.Core.Learner.Profile.Notes["favorite"] = NoteText;
        StartTask(t);
        await t.Engine.RunTurnAsync("FIRST_TRANSCRIPT");
        t.Engine.PlayerMove("apple_1", "left", null);
        await t.Engine.RunTurnAsync("SECOND_TRANSCRIPT");

        // The second request is the one that carried the player's event, the note and the task text, each once, in the current block.
        var current = LearnerBlock(chat.Requests[1]);
        Assert.Contains("\"actor\":\"player\"", current);
        Assert.Contains(NoteText, current);
        Assert.Contains(TaskText, current);

        await t.Engine.RunTurnAsync("THIRD_TRANSCRIPT");

        var stored = StoredLearnerMessages(chat.Requests[2]);
        Assert.Equal(2, stored.Count);
        Assert.Contains("learner_transcript: FIRST_TRANSCRIPT", stored[0]);
        Assert.Contains("learner_transcript: SECOND_TRANSCRIPT", stored[1]);
        foreach (var message in stored)
        {
            Assert.StartsWith("<learner_data>", message);
            Assert.EndsWith("</learner_data>", message);
            Assert.DoesNotContain("player_events_since_previous_turn", message);
            Assert.DoesNotContain("\"actor\":\"player\"", message);
            Assert.DoesNotContain("\"event_id\"", message);
            Assert.DoesNotContain(NoteText, message);
            Assert.DoesNotContain("saved_notes", message);
            Assert.DoesNotContain("TASK_TEXT_MARKER", message);
            Assert.DoesNotContain("TARGET_KEY_MARKER", message);
            Assert.DoesNotContain("active_task", message);
        }
        // Nothing but the current block mentions the player's earlier event.
        var current3 = LearnerBlock(chat.Requests[2]);
        Assert.DoesNotContain("\"actor\":\"player\"", current3);
        Assert.DoesNotContain("\"event_id\":2,", Everything(chat.Requests[2]));
        // The state that is still current is rebuilt in the current block.
        Assert.Contains(NoteText, current3);
        Assert.Contains(TaskText, current3);
        Assert.Contains("learner_transcript: THIRD_TRANSCRIPT", current3);
    }

    [Fact]
    public async Task S129_a_stored_learner_message_is_exactly_the_transcript_in_the_data_tags()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("only words");
        await t.Engine.RunTurnAsync("more words");

        Assert.Equal(new[] { "<learner_data>\nlearner_transcript: only words\n</learner_data>" }, StoredLearnerMessages(chat.Requests[1]));
    }

    [Fact]
    public async Task S129_a_correction_note_is_not_persisted_but_the_corrected_transcript_is()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b").EnqueueText("c");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("I wnat water");
        await t.Engine.CorrectLastTranscriptAsync("I want water");
        Assert.Contains("transcript_correction", LearnerBlock(chat.Requests[1]));

        await t.Engine.RunTurnAsync("next");

        var stored = StoredLearnerMessages(chat.Requests[2]);
        Assert.Equal(2, stored.Count);
        Assert.Contains("learner_transcript: I wnat water", stored[0]);
        Assert.Contains("learner_transcript: I want water", stored[1]);
        Assert.All(stored, m => Assert.DoesNotContain("transcript_correction", m));
        Assert.DoesNotContain("transcript_correction", LearnerBlock(chat.Requests[2]));
    }

    [Fact]
    public async Task S129_tool_calls_and_results_stay_in_history_with_the_transcript()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "kept")))
            .EnqueueText("a")
            .EnqueueText("b");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("one");
        await t.Engine.RunTurnAsync("two");

        var request = chat.Requests[2];
        Assert.Contains(CallsIn(request), c => c.CallId == "c1" && c.Name == "test_probe");
        Assert.Contains(ResultsIn(request), r => r.CallId == "c1" && StatusOf(r) == "success");
    }

    [Fact]
    public async Task S129_the_players_event_reaches_the_model_once_and_only_in_the_request_after_it_happened()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "x")))
            .EnqueueText("a")
            .EnqueueText("b")
            .EnqueueText("c");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        await t.Engine.RunTurnAsync("one");
        t.Engine.PlayerMove("apple_1", "left", null);
        await t.Engine.RunTurnAsync("two");
        await t.Engine.RunTurnAsync("three");

        var seen = chat.Requests.Select((r, i) => (Index: i, Has: Everything(r).Contains("\"actor\":\"player\"", StringComparison.Ordinal))).ToList();
        Assert.Equal(new[] { 2 }, seen.Where(s => s.Has).Select(s => s.Index).ToArray());
    }
}
