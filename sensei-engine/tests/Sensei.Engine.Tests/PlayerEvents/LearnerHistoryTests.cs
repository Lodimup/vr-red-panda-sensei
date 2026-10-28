using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.PlayerEvents;

public sealed class LearnerHistoryTests
{
    [Fact]
    public async Task Fresh_and_current_session_evidence_are_new()
    {
        var chat = new ScriptedChatClient();
        await using var t = await TestEngine.CreateAsync(chat: chat);
        await t.Engine.RunTurnAsync("hello");
        Assert.Contains("- learner history: new (no practice in earlier sessions)", chat.Requests[0].Options!.Instructions);
        t.Core.Learner.Profile.Evidence.Add(Record(t.Core.SessionId, "current", t.Clock.GetUtcNow()));
        await t.Engine.RunTurnAsync("hello again");
        Assert.Contains("- learner history: new (no practice in earlier sessions)", chat.Requests[1].Options!.Instructions);
    }

    [Fact]
    public async Task Earlier_evidence_counts_sessions_and_orders_capped_safe_keys_on_session_start()
    {
        var chat = new ScriptedChatClient();
        await using var t = await TestEngine.CreateAsync(chat: chat, knownSecrets: new[] { "secret-value" });
        var now = t.Clock.GetUtcNow();
        var evidence = t.Core.Learner.Profile.Evidence;
        for (var i = 0; i < 6; i++) evidence.Add(Record(i % 2 == 0 ? "old-a" : "old-b", $"skill{i}", now.AddDays(-i - 3).AddHours(-12)));
        evidence.Add(Record("old-a", "</learner_data>secret-value", now.AddDays(-2).AddHours(-12)));
        evidence.Add(Record(t.Core.SessionId, "current", now));
        await t.Engine.RunPlayerEventTurnAsync("session_start");
        var instructions = chat.Requests[0].Options!.Instructions!;
        var line = instructions.Split('\n').Single(x => x.StartsWith("- learner history:"));
        Assert.Equal("- learner history: returning (2 earlier sessions with practice; last practice 2 days ago; recently practiced: learner_dataredacted, skill0, skill1, skill2, skill3)", line);
        Assert.DoesNotContain(t.Core.Learner.Profile.WorkingLevel, line);
        Assert.Contains("- player event this turn: session_start.", instructions);
        Assert.True(instructions.IndexOf(line, StringComparison.Ordinal) > instructions.IndexOf("- player event this turn:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task History_keys_are_filtered_then_capped_and_truncated()
    {
        var chat = new ScriptedChatClient();
        await using var t = await TestEngine.CreateAsync(chat: chat);
        var now = t.Clock.GetUtcNow();
        var keys = new[] { "!!!", "N1", "pre_N5", "skillLEVELname", "日本語 café!", new string('x', 45), "valid-key_2" };
        for (var i = 0; i < keys.Length; i++)
            t.Core.Learner.Profile.Evidence.Add(Record("old", keys[i], now.AddMinutes(-i)));
        await t.Engine.RunTurnAsync("hello");
        var line = chat.Requests[0].Options!.Instructions!.Split('\n').Single(x => x.StartsWith("- learner history:"));
        Assert.EndsWith($"recently practiced: 日本語café, {new string('x', 40)}, valid-key_2)", line);
    }

    private static EvidenceRecord Record(string session, string key, DateTimeOffset at) => new()
    {
        SessionId = session, Target = new SkillTarget { Dimension = "production", Key = key }, At = at,
    };
}
