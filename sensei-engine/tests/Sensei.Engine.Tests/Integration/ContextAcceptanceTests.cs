using System.Text.RegularExpressions;
using Sensei.Engine.Tests.Foundation;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Integration;

/// <summary>The per-request context the model sees (S63) and where the learner's words sit in it (S76), through the agent loop.</summary>
public sealed class ContextAcceptanceTests
{
    private static string Instructions(ScriptedChatClient chat, int request) => chat.Requests[request].Options!.Instructions!;

    private static string Everything(ScriptedChatClient chat, int request) => Agent.AgentTestSupport.Everything(chat.Requests[request]);

    private static int Remaining(string text, string pattern)
    {
        var m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        Assert.True(m.Success, "no match for " + pattern + " in:\n" + text);
        return int.Parse(m.Groups[1].Value);
    }

    [Fact]
    public async Task S63_each_request_reports_decremented_remaining_budgets()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("ok", "a")), ("c2", "test_probe", ProbeJson("ok", "b")))
            .EnqueueFunctionCalls(("c3", "test_probe", ProbeJson("ok", "c")))
            .EnqueueText("done");
        await using var t = await EngineAsync(chat);

        await t.Engine.RunTurnAsync("hi");

        var calls = Enumerable.Range(0, 3).Select(i => Remaining(Instructions(chat, i), @"tool calls remaining this turn: (\d+)")).ToArray();
        Assert.Equal(new[] { 8, 6, 5 }, calls);
        var rounds = Enumerable.Range(0, 3).Select(i => Remaining(Instructions(chat, i), @"model rounds remaining[^\d\n]{0,30}(\d+)")).ToArray();
        Assert.Equal(new[] { 3, 2, 1 }, rounds);
    }

    [Fact]
    public async Task S63_the_context_tells_the_model_when_the_player_moved_something()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        await t.Engine.RunTurnAsync("one");
        t.Engine.PlayerMove("apple_1", "left", null);

        await t.Engine.RunTurnAsync("two");

        Assert.DoesNotContain("\"actor\":\"player\"", Everything(chat, 0));
        var second = Everything(chat, 1);
        Assert.Contains("player", second);
        Assert.Matches("\"actor\":\"player\",\"kind\":\"moved\"", second);
    }

    [Fact]
    public async Task S63_new_context_does_not_reinject_old_player_events()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b").EnqueueText("c");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        await t.Engine.RunTurnAsync("one");
        t.Engine.PlayerMove("apple_1", "left", null);

        await t.Engine.RunTurnAsync("two");
        await t.Engine.RunTurnAsync("three");

        Assert.Matches(@"event[_ ]id[""" + ":='\\t]*2\\b.{0,160}actor[\"" + ":='\\t]*player", LearnerBlock(chat.Requests[1]));
        Assert.DoesNotContain("\"event_id\":2,", Everything(chat, 2).Replace(LearnerBlock(chat.Requests[1]), ""));
        Assert.DoesNotContain("\"actor\":\"player\"", LearnerBlock(chat.Requests[2]));
    }

    [Fact]
    public async Task S76_gap_transcript_notes_and_player_events_are_data_and_policy_is_engine_owned()
    {
        var chat = new ScriptedChatClient().EnqueueText("a").EnqueueText("b");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        lock (t.Core.Sync) t.Core.Learner.Profile.Notes["favorite"] = "NOTE_MARKER";
        await t.Engine.RunTurnAsync("warm up");
        t.Engine.PlayerMove("apple_1", "left", null);

        await t.Engine.RunTurnAsync("TRANSCRIPT_MARKER quoted words");

        var instructions = Instructions(chat, 1);
        Assert.DoesNotContain("TRANSCRIPT_MARKER", instructions);
        Assert.DoesNotContain("NOTE_MARKER", instructions);
        Assert.DoesNotContain("\"actor\":\"player\"", instructions);
        var user = LearnerBlock(chat.Requests[1]);
        var from = user.IndexOf("\nlearner_transcript: TRANSCRIPT_MARKER", StringComparison.Ordinal);
        Assert.True(from >= 0);
        var tail = user[from..];
        Assert.Contains("NOTE_MARKER", tail);
        Assert.Contains("player", tail);
        Assert.EndsWith("</learner_data>", user);
    }

    [Fact]
    public async Task S76_learner_text_sits_inside_one_learner_data_block_that_the_prompt_calls_untrusted()
    {
        var chat = new ScriptedChatClient().EnqueueText("a");
        await using var t = await EngineAsync(chat);
        SceneHelpers.Spawn(t, ("apple", "center"));
        t.Engine.PlayerMove("apple_1", "left", null);

        await t.Engine.RunTurnAsync("TRANSCRIPTMARKER quoted");

        var request = chat.Requests[0];
        var user = LearnerBlock(request);
        // The learner's words travel in the user messages in exactly one block; the prompt may name the tag but opens no block.
        Assert.Single(Regex.Matches(string.Join("\n", request.Messages.Select(m => m.Text)), "<learner_data>"));
        var start = user.IndexOf("<learner_data>", StringComparison.Ordinal);
        var end = user.IndexOf("</learner_data>", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var inside = user[start..end];
        Assert.Contains("TRANSCRIPTMARKER", inside);
        Assert.Contains("player", inside);
        Assert.DoesNotContain("TRANSCRIPTMARKER", request.Options!.Instructions!);
        Assert.Matches("untrusted", request.Options!.Instructions!);
        Assert.Contains("learner_data", request.Options!.Instructions!);
    }
}
