using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World.ReviewFixes;

/// <summary>S22/S69: a world tool never raises its renderer event for a call that then settles as cancelled, and a handler cancelling during the event cannot turn a committed call into cancelled.</summary>
public sealed class AnimateCancellationTests
{
    private sealed class CancellingEvents : IEngineEvents
    {
        public SenseiEngine? Engine { get; set; }
        public int Animations { get; private set; }
        public int Highlights { get; private set; }
        public void OnAnimate(string animation, string? targetId)
        {
            Animations++;
            Engine!.CancelGeneration();
        }
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) => Highlights++;
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) { }
        public void OnHint(HintCard card) { }
        public void OnSupportChanged(SupportSettings settings) { }
        public void OnTaskChanged(TaskView task) { }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) { }
    }

    private static async Task<(SenseiEngine Engine, CancellingEvents Events)> StartAsync()
    {
        var events = new CancellingEvents();
        var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null,
            Events = events,
            Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(),
            ProfileId = "default",
            Trace = new RecordingTraceSink(),
        });
        events.Engine = engine;
        engine.BeginTurn("setup");
        Assert.True((await engine.InvokeToolAsync("spawn_objects", SpawnJson(("apple", "left", null)))).IsSuccess);
        engine.BeginTurn("practice");
        return (engine, events);
    }

    [Fact]
    public async Task S22_animate_cannot_return_cancelled_after_raising_its_event()
    {
        var (engine, events) = await StartAsync();
        await using var _ = engine;

        var result = await engine.InvokeToolAsync("animate_sensei", """{"animation":"point","target_id":"apple_1"}""");

        Assert.True(result.Status == ToolStatuses.Success || events.Animations == 0,
            $"S22: result={result.Status}/{result.ErrorCode}; OnAnimate={events.Animations}. A cancelled call must not raise its event.");
    }

    [Fact]
    public async Task S69_animate_raises_its_event_once_after_the_call_committed_and_returns_success()
    {
        var (engine, events) = await StartAsync();
        await using var _ = engine;

        var result = await engine.InvokeToolAsync("animate_sensei", """{"animation":"point","target_id":"apple_1"}""");

        Assert.True(result.IsSuccess, $"result={result.Status}/{result.ErrorCode}");
        Assert.Equal(1, events.Animations);
    }

    [Fact]
    public async Task S69_highlight_raises_its_event_once_and_records_assistance_when_the_call_succeeds()
    {
        var (engine, events) = await StartAsync();
        await using var _ = engine;
        Assert.True((await engine.InvokeToolAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))))).IsSuccess);
        var before = engine.Core.Assistance.Records.Count;

        var result = await engine.InvokeToolAsync("highlight_objects", Ids("apple_1"));

        Assert.True(result.IsSuccess, $"result={result.Status}/{result.ErrorCode}");
        Assert.Equal(1, events.Highlights);
        Assert.Equal(before + 1, engine.Core.Assistance.Records.Count);
    }

    [Fact]
    public async Task S22_a_highlight_after_cancel_generation_raises_no_event_and_records_nothing()
    {
        var (engine, events) = await StartAsync();
        await using var _ = engine;
        Assert.True((await engine.InvokeToolAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))))).IsSuccess);
        var before = engine.Core.Assistance.Records.Count;
        engine.CancelGeneration();

        var result = await engine.InvokeToolAsync("highlight_objects", Ids("apple_1"));

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(0, events.Highlights);
        Assert.Equal(before, engine.Core.Assistance.Records.Count);
    }
}
