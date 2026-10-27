using Sensei.Engine.Storage;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.ReviewFixes.RoundFour;

public sealed class ExceptionTrust
{
    private const string Key = "sk-abcdefghijklmnop123456";

    [Fact]
    public async Task F01_rethrowing_a_previous_engine_exception_must_remove_foreign_data()
    {
        var store = new InMemoryProfileStore();
        await using var t = await TestEngine.CreateAsync(store: store, knownSecrets: new[] { Key });
        store.FailSavesWith = new IOException("first failure");
        var first = Assert.IsType<SenseiEngineException>(await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("zero")));
        first.Data["credential"] = Key;
        first.HelpLink = "https://example.com/" + Key;
        first.Source = Key;
        store.FailSavesWith = first;
        var second = await Record.ExceptionAsync(() => t.Engine.SetStartingBandAsync("intermediate"));
        store.FailSavesWith = null;
        Assert.NotNull(second);
        Assert.True(second.Data.Count == 0 && second.HelpLink is null && second.Source != Key,
            $"Previously emitted exception was trusted by identity/type: Data contains key={second.Data["credential"] as string == Key}, HelpLink contains key={second.HelpLink?.Contains(Key)}, Source contains key={second.Source == Key}");
    }

    [Fact]
    public async Task F02_corrupt_profile_exception_must_sanitize_the_requested_profile_id()
    {
        // A provider-shaped key is also a valid S57 profile id. No private field mutation is involved.
        Assert.True(ProfileIds.IsValid(Key));
        var store = new CorruptStore();
        var error = await Record.ExceptionAsync(() => SenseiEngine.CreateAsync(new SenseiEngineOptions
        {
            ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store, ProfileId = Key, KnownSecrets = new[] { Key },
        }));
        Assert.IsType<ProfileCorruptException>(error);
        Assert.DoesNotContain(Key, error.Message);
    }

    private sealed class CorruptStore : IProfileStore
    {
        public Task<Sensei.Engine.Learner.LearnerProfile?> LoadAsync(string id, CancellationToken ct) =>
            throw new ProfileCorruptException(id, id + ".json", null);
        public Task SaveAsync(Sensei.Engine.Learner.LearnerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task DeleteAsync(string id, CancellationToken ct) => Task.CompletedTask;
    }
}
