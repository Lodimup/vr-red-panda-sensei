using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.RoundTwo;

public sealed class DeepExceptionSecret
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task S77_secret_beyond_sanitizer_depth_never_leaves_public_api(bool secretAtRoot)
    {
        const string secret = "fictional-store-credential";
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { secret });
        Exception failure = new IOException("dependency failed: " + secret);
        for (var i = 0; i < 18; i++) failure = new IOException("dependency wrapper", failure);
        if (secretAtRoot) failure = new IOException("outer: " + secret, failure);
        t.Store.FailSavesWith = failure;

        var error = await Record.ExceptionAsync(() => t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false }));

        Assert.NotNull(error);
        for (Exception? part = error; part is not null; part = part.InnerException)
            Assert.DoesNotContain(secret, part.Message);
    }
}
