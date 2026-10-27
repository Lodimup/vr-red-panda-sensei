using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation.RoundThree;

public sealed class CommittedLearnerSaveFault
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task S22_saved_note_is_never_reported_cancelled_when_the_save_faults_late(bool nextTurn, bool cancellationException)
    {
        var store = new SaveThenFaultStore(cancellationException);
        var events = new RecordingEvents();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store, Events = events,
        });
        engine.BeginTurn("first");
        store.Armed = true;
        var first = engine.InvokeToolAsync("remember_note", """{"key":"interest","value":"music"}""", "note");
        await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = engine.InvokeToolAsync("remember_note", """{"key":"interest","value":"music"}""", "note");
        if (nextTurn) engine.BeginTurn("second");
        else engine.CancelGeneration();
        store.Release.TrySetResult(true);
        var result = await first.WaitAsync(TimeSpan.FromSeconds(5));
        var second = await duplicate.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(result.Status, second.Status);
        var liveNote = engine.GetProfile().Notes.ContainsKey("interest");
        var savedNote = store.Inner.Peek("default")!.Notes.ContainsKey("interest");
        Assert.False(result.Status is ToolStatuses.Cancelled or ToolStatuses.Failure && (liveNote || savedNote),
            $"S22: result={result.Status}, live note={liveNote}, persisted note={savedNote}; a cancelled call retained its committed profile write.");
    }

    [Theory]
    [InlineData("remember_note", false)]
    [InlineData("remember_note", true)]
    [InlineData("set_support", false)]
    [InlineData("set_support", true)]
    public async Task S22_successful_builtin_learner_write_keeps_success_after_generation_ends(string tool, bool nextTurn)
    {
        var store = new SaveThenFaultStore(false) { ReturnSuccess = true };
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store,
        });
        engine.BeginTurn("first");
        store.Armed = true;
        var json = tool == "remember_note" ? """{"key":"interest","value":"music"}""" : """{"reason":"learner_request","pace":"slow"}""";
        var pending = engine.InvokeToolAsync(tool, json, "write");
        await store.Saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (nextTurn) engine.BeginTurn("second");
        else engine.CancelGeneration();
        store.Release.TrySetResult(true);
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(5));
        var profile = engine.GetProfile();
        var persisted = store.Inner.Peek("default")!;
        var committed = tool == "remember_note"
            ? profile.Notes.ContainsKey("interest") && persisted.Notes.ContainsKey("interest")
            : profile.Support.Pace == "slow" && persisted.Support.Pace == "slow";
        Assert.True(committed);
        Assert.True(result.Status == ToolStatuses.Success,
            $"S22: tool={tool}, result={result.Status}, live and persisted change=True; no dependency fault occurred.");
    }

    private sealed class SaveThenFaultStore(bool cancellationException) : IProfileStore
    {
        public InMemoryProfileStore Inner { get; } = new();
        public bool Armed { get; set; }
        public bool ReturnSuccess { get; init; }
        public TaskCompletionSource<bool> Saved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<LearnerProfile?> LoadAsync(string id, CancellationToken ct) => Inner.LoadAsync(id, ct);
        public Task DeleteAsync(string id, CancellationToken ct) => Inner.DeleteAsync(id, ct);
        public async Task SaveAsync(LearnerProfile profile, CancellationToken ct)
        {
            await Inner.SaveAsync(profile, ct);
            if (!Armed) return;
            Saved.TrySetResult(true);
            await Release.Task;
            if (ReturnSuccess) return;
            if (cancellationException) throw new OperationCanceledException("store acknowledgement cancelled");
            throw new IOException("store acknowledgement failed");
        }
    }
}
