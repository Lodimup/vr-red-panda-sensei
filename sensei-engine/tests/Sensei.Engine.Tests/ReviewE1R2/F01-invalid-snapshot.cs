using System.Reflection;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.ReviewE1R2;

public sealed class F01InvalidSnapshot
{
    [Fact]
    public async Task Invalid_event_returns_a_consistent_turn_and_generation_snapshot()
    {
        await using var t = await EngineAsync(new ScriptedChatClient());
        t.Engine.BeginTurn("first");
        var core = (EngineCore)typeof(SenseiEngine).GetField("_core", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(t.Engine)!;
        TurnResult? invalid = null;
        Exception? error = null;
        using var started = new ManualResetEventSlim();
        var worker = new Thread(() =>
        {
            started.Set();
            try { invalid = t.Engine.RunPlayerEventTurnAsync("bad").GetAwaiter().GetResult(); }
            catch (Exception ex) { error = ex; }
        });
        lock (core.Sync)
        {
            worker.Start();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.True(SpinWait.SpinUntil(() => (worker.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
            t.Engine.BeginTurn("second");
        }
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)));
        Assert.Null(error);
        Assert.NotNull(invalid);
        Assert.Equal("invalid_player_event", invalid.ErrorCode);
        Assert.Equal(invalid.TurnId, invalid.GenerationId);
    }
}
