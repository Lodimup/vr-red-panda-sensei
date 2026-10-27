using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.World.ReviewFixes;

public sealed class HighlightCancellationTests
{
    private sealed class CancellingEvents : IEngineEvents
    {
        public SenseiEngine? Engine { get; set; }
        public int Highlights { get; private set; }
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs)
        {
            Highlights++;
            Engine!.CancelGeneration();
        }
        public void OnAnimate(string animation, string? targetId) { }
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) { }
        public void OnHint(HintCard card) { }
        public void OnSupportChanged(SupportSettings settings) { }
        public void OnTaskChanged(TaskView task) { }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) { }
    }

    [Fact]
    public async Task S22_highlight_cannot_return_cancelled_after_recording_answer_assistance()
    {
        var events = new CancellingEvents();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
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
        Assert.True((await engine.InvokeToolAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right"))))).IsSuccess);
        var revision = engine.GetScene().Revision;
        var countBefore = engine.Core.Assistance.Records.Count;
        var result = await engine.InvokeToolAsync("highlight_objects", Ids("apple_1"));
        var countAfter = engine.Core.Assistance.Records.Count;

        Assert.Equal(1, events.Highlights);
        Assert.Equal(revision, engine.GetScene().Revision);
        Assert.True(result.Status == ToolStatuses.Success || countAfter == countBefore,
            $"S22: result={result.Status}/{result.ErrorCode}; assistance={countBefore}->{countAfter}; OnHighlight={events.Highlights}. A cancelled call must not leave assistance records.");
    }
}
