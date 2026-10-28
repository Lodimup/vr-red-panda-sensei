using Microsoft.Extensions.AI;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>An engine plus its recording ports. Dispose to cancel any running turn.</summary>
public sealed class TestEngine : IAsyncDisposable
{
    private TestEngine(SenseiEngine engine, InMemoryProfileStore store, RecordingSpeechOutput speech, RecordingEvents events,
        RecordingTraceSink trace, ManualClock clock, ScriptedChatClient? chat)
    {
        Engine = engine;
        Store = store;
        Speech = speech;
        Events = events;
        Trace = trace;
        Clock = clock;
        Chat = chat;
    }

    public SenseiEngine Engine { get; }
    public InMemoryProfileStore Store { get; }
    public RecordingSpeechOutput Speech { get; }
    public RecordingEvents Events { get; }
    public RecordingTraceSink Trace { get; }
    public ManualClock Clock { get; }
    public ScriptedChatClient? Chat { get; }

    internal EngineCore Core => Engine.Core;

    /// <summary>
    /// Creates an engine with sensible defaults: no chat client, in-memory store, recording speech/events/trace,
    /// a manual clock at 2026-10-27T09:30:15Z, profile id <c>default</c>.
    /// </summary>
    public static Task<TestEngine> CreateAsync(
        ScriptedChatClient? chat = null,
        InMemoryProfileStore? store = null,
        RecordingSpeechOutput? speech = null,
        RecordingEvents? events = null,
        RecordingTraceSink? trace = null,
        ManualClock? clock = null,
        string profileId = "default",
        IReadOnlyList<string>? knownSecrets = null,
        IEnumerable<SenseiTool>? extraTools = null,
        IChatClient? chatClient = null) =>
        CreateCoreAsync(chat, store, speech, events, trace, clock, profileId, knownSecrets, extraTools, chatClient, null);

    /// <summary>Like <see cref="CreateAsync"/> with a custom agent runner (lifecycle tests).</summary>
    internal static async Task<TestEngine> CreateCoreAsync(
        ScriptedChatClient? chat,
        InMemoryProfileStore? store,
        RecordingSpeechOutput? speech,
        RecordingEvents? events,
        RecordingTraceSink? trace,
        ManualClock? clock,
        string profileId,
        IReadOnlyList<string>? knownSecrets,
        IEnumerable<SenseiTool>? extraTools,
        IChatClient? chatClient,
        Func<EngineCore, IAgentRunner>? runner)
    {
        store ??= new InMemoryProfileStore();
        speech ??= new RecordingSpeechOutput();
        events ??= new RecordingEvents();
        trace ??= new RecordingTraceSink();
        clock ??= new ManualClock();
        var options = new SenseiEngineOptions
        {
            ChatClient = chatClient ?? chat,
            Speech = speech,
            ProfileStore = store,
            ProfileId = profileId,
            Events = events,
            Trace = trace,
            Clock = clock,
            KnownSecrets = knownSecrets ?? Array.Empty<string>(),
        };
        var engine = await SenseiEngine.CreateAsync(options, extraTools, runner);
        return new TestEngine(engine, store, speech, events, trace, clock, chat);
    }

    /// <summary>Begins a manual turn and invokes a tool.</summary>
    public Task<ToolResult> CallAsync(string tool, string json, string? callId = null) => Engine.InvokeToolAsync(tool, json, callId);

    public ValueTask DisposeAsync() => Engine.DisposeAsync();
}
