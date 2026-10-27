using Sensei.Engine.Storage;

namespace Sensei.Engine.Tests.Foundation;

public sealed class MissingProfileFields
{
    [Fact]
    public async Task S57_incomplete_profile_is_corrupt_and_left_untouched()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scratchpad", "rps-corrupt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileProfileStore(directory);
            var path = store.PathFor("default");
            const string incomplete = "{\"profile_id\":\"default\"}";
            await File.WriteAllTextAsync(path, incomplete);

            await Assert.ThrowsAsync<ProfileCorruptException>(() => store.LoadAsync("default", CancellationToken.None));
            Assert.Equal(incomplete, await File.ReadAllTextAsync(path));
        }
        finally { Directory.Delete(directory, true); }
    }
}

public sealed class IncompleteProfileStartup
{
    [Fact]
    public async Task S57_startup_does_not_silently_overwrite_an_incomplete_profile()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scratchpad", "rps-startup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new FileProfileStore(directory);
            var path = store.PathFor("default");
            const string incomplete = "{\"profile_id\":\"default\"}";
            await File.WriteAllTextAsync(path, incomplete);
            var error = await Record.ExceptionAsync(async () =>
            {
                await using var engine = await SenseiEngine.CreateAsync(new SenseiEngineOptions
                {
                    ChatClient = null, ProfileStore = store,
                    Speech = new Sensei.Engine.Tests.TestKit.RecordingSpeechOutput()
                });
            });

            Assert.Equal(incomplete, await File.ReadAllTextAsync(path));
            Assert.IsType<ProfileCorruptException>(error);
        }
        finally { Directory.Delete(directory, true); }
    }
}
