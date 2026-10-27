using System.ClientModel;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.Foundation.ReviewFixes.RoundFour;

public sealed class ProviderTruncation
{
    [Theory]
    [InlineData("http", false)]
    [InlineData("client", false)]
    [InlineData("timeout", false)]
    [InlineData("http", true)]
    [InlineData("client", true)]
    [InlineData("timeout", true)]
    public async Task F03_sanitize_before_shortening_provider_detail(string kind, bool patternOnly)
    {
        var key = patternOnly ? "sk-abcdefghijklmnop123456" : "ReviewOnlyCredentialAlphaBeta";
        var message = new string('.', 290) + key;
        Exception error = kind switch
        {
            "http" => new HttpRequestException(message),
            "client" => new ClientResultException(message),
            _ => new TaskCanceledException(message),
        };
        var chat = new ScriptedChatClient().EnqueueError(error);
        await using var t = await TestEngine.CreateAsync(chat: chat, knownSecrets: patternOnly ? Array.Empty<string>() : new[] { key });
        var result = await t.Engine.RunTurnAsync("hello");
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.DoesNotContain(key[..10], result.ErrorDetail);
    }
}
