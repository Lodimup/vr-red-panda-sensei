using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;

namespace Sensei.Engine.Tests.Agent;

public sealed class RoundLatencyTests
{
    [Fact]
    public void PromptRequestsShortOpeningAndFirstResponseSpeech()
    {
        var prompt = Sensei.Engine.Agent.SystemPrompt.Text;
        Assert.Contains("Open with a short spoken sentence (at most 6 words or 15 kana)", prompt);
        Assert.Contains("put all speech in the first response when no tool result is needed", prompt);
    }

    [Fact]
    public async Task OptedInSinkReceivesEachRequestAndResponseWithoutLearnerText()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", AgentTestSupport.ProbeJson("ok")))
            .EnqueueText("done", outputTokens: 13);
        var sink = new RoundSink();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions {
            ChatClient = chat, Speech = new RecordingSpeechOutput(), ProfileStore = new InMemoryProfileStore(), Trace = sink
        }, new[] { new ProbeTool() }, null);
        var result = await engine.RunTurnAsync("private learner text");
        var entries = sink.Entries.Where(e => e.Tool == "engine:latency").ToArray();
        Assert.Equal(new[] { "request_sent", "response_received", "request_sent", "response_received" }, entries.Select(e => e.Status));
        Assert.Equal(new[] { 1, 1, 2, 2 }, entries.Select(e => e.Round));
        Assert.Equal(new[] { "", "5", "", "13" }, entries.Select(e => e.Arguments));
        Assert.All(entries, e => { Assert.Equal(result.TurnId, e.TurnId); Assert.DoesNotContain("private", e.Arguments); });
        Assert.Equal("completed", result.Status);
    }

    [Fact]
    public async Task ExistingSinkRemainsToolOnly()
    {
        var sink = new RecordingTraceSink();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions {
            ChatClient = new ScriptedChatClient().EnqueueText("done"), Speech = new RecordingSpeechOutput(),
            ProfileStore = new InMemoryProfileStore(), Trace = sink
        });
        await engine.RunTurnAsync("hello");
        Assert.DoesNotContain(sink.Entries, e => e.Tool == "engine:latency");
    }

    private sealed class RoundSink : IRoundTraceSink
    {
        internal readonly List<TraceEntry> Entries = new();
        public void Write(TraceEntry entry) => Entries.Add(entry);
    }
}
