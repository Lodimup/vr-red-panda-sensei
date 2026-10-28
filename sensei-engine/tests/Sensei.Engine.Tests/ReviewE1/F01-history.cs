using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.ReviewE1;

public sealed class F01History
{
    [Fact]
    public async Task Synthetic_events_do_not_consume_learner_turn_history_limit()
    {
        var chat = new ScriptedChatClient();
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("remember my preference");
        for (var i = 0; i < 12; i++) await t.Engine.RunPlayerEventTurnAsync("area_placed");
        await t.Engine.RunTurnAsync("what did I say?");
        Assert.Contains(UserMessages(chat.Requests.Last()), m => m.Text.Contains("learner_transcript: remember my preference"));
    }
}
