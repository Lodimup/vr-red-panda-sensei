using Sensei.Engine.Learner;
using Sensei.Engine.Storage;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Integration;

/// <summary>S7: a new profile is saved as created, then the session record is appended and saved again.</summary>
public sealed class S7CreationSaveTests
{
    private sealed class RecordingStore : IProfileStore
    {
        public List<(string Id, int Sessions)> Saves { get; } = new();
        public Task<LearnerProfile?> LoadAsync(string profileId, CancellationToken ct) => Task.FromResult<LearnerProfile?>(null);
        public Task SaveAsync(LearnerProfile profile, CancellationToken ct)
        {
            Saves.Add((profile.ProfileId, profile.Sessions.Count));
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string profileId, CancellationToken ct) => Task.CompletedTask;
    }

    [Fact]
    public async Task S7_GCore03_creation_is_saved_before_the_session_is_appended()
    {
        var store = new RecordingStore();
        await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions { ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store });

        Assert.Equal(new[] { ("default", 0), ("default", 1) }, store.Saves.ToArray());
    }

    [Fact]
    public async Task S7_an_existing_profile_is_saved_once_with_its_new_session()
    {
        var memory = new InMemoryProfileStore();
        await using (await TestEngine.CreateAsync(store: memory)) { }
        var before = memory.SaveCount;

        await using var second = await TestEngine.CreateAsync(store: memory);

        Assert.Equal(1, memory.SaveCount - before);
        Assert.Equal(2, memory.Peek("default")!.Sessions.Count);
    }

    [Fact]
    public async Task S7_a_failed_creation_save_fails_the_creation()
    {
        var memory = new InMemoryProfileStore { FailSavesWith = new IOException("disk full") };
        var error = await Record.ExceptionAsync(() => TestEngine.CreateAsync(store: memory));
        Assert.IsType<SenseiEngineException>(error);
        Assert.Contains("profile_store_error", error.Message);
        Assert.False(memory.Contains("default"));
    }
}
