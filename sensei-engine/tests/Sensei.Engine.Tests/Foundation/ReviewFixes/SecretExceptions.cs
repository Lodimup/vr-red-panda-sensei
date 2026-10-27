using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation;

public sealed class SecretExceptions
{
    [Fact]
    public async Task S77_public_preference_exception_redacts_known_secrets()
    {
        const string secret = "fictional-store-credential";
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { secret });
        t.Store.FailSavesWith = new IOException("store write failed: " + secret);

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" }));

        Assert.NotNull(error);
        Assert.DoesNotContain(secret, error.ToString());
        Assert.Equal("set_preference_by_player failed: profile_store_error", error.Message);
    }
}
