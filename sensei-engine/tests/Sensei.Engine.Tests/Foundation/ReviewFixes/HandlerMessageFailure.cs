using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Tasks;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Sensei.Engine.World;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

public sealed class HandlerMessageFailure
{
    [Theory]
    [InlineData("speak")]
    [InlineData("show_hint")]
    [InlineData("replay_speech")]
    public async Task S37d_unreadable_speech_exception_still_returns_speech_failed(string tool)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        Assert.True((await t.CallAsync("speak", """{"segments":[{"lang":"en","text":"hello"}]}""")).IsSuccess);
        t.Speech.Outcome = _ => throw new UnreadableException();
        var json = tool switch
        {
            "speak" => """{"segments":[{"lang":"en","text":"hello"}]}""",
            "show_hint" => """{"kind":"explanation","content":{"english":"hello"},"speak":true}""",
            _ => "{}",
        };
        ToolResult? result = null;
        var error = await Record.ExceptionAsync(async () => result = await t.CallAsync(tool, json));
        Assert.Null(error);
        Assert.Equal(ToolStatuses.Failure, result!.Status);
        Assert.Equal(ToolErrorCodes.SpeechFailed, result.ErrorCode);
    }

    [Fact]
    public async Task S69_throwing_exception_message_cannot_break_a_committed_scene_call()
    {
        var trace = new RecordingTraceSink();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = new InMemoryProfileStore(),
            Events = new FaultingEvents(), Trace = trace,
        });
        engine.BeginTurn("hello");
        ToolResult? result = null;
        var error = await Record.ExceptionAsync(async () => result = await engine.InvokeToolAsync("spawn_objects",
            """{"objects":[{"catalog_id":"apple","region":"center"}]}""", "spawn"));
        Assert.Single(engine.GetScene().Instances);
        Assert.Null(error);
        Assert.Equal(ToolStatuses.Success, result!.Status);
        Assert.Contains(trace.Entries, e => e.ErrorCode == ToolErrorCodes.EventHandlerError);
    }

    private sealed class UnreadableException : Exception
    {
        public override string Message => throw new IOException("the handler exception's Message getter failed");
    }
    private sealed class FaultingEvents : IEngineEvents
    {
        public void OnSceneChanged(SceneEvent e, SceneSnapshot scene) => throw new UnreadableException();
        public void OnHighlight(IReadOnlyList<string> ids, int durationMs) { }
        public void OnAnimate(string animation, string? targetId) { }
        public void OnHint(HintCard card) { }
        public void OnSupportChanged(SupportSettings settings) { }
        public void OnTaskChanged(TaskView task) { }
        public void OnSummary(SessionSummary summary) { }
        public void OnSenseiState(string state) { }
    }
}
