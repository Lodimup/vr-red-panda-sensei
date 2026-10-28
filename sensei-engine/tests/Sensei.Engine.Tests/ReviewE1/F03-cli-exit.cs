using Sensei.Engine.Tests.Cli;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.ReviewE1;

public sealed class F03CliExit
{
    [Theory]
    [InlineData("/event")]
    [InlineData("/event unknown")]
    public async Task Invalid_event_argument_is_a_usage_error(string input)
    {
        var result = await InProcessCli.ScriptAsync(new[] { input }, new ScriptedChatClient());
        Assert.Contains("invalid_player_event", result.Text);
        Assert.Equal(1, result.Code);
    }
}
