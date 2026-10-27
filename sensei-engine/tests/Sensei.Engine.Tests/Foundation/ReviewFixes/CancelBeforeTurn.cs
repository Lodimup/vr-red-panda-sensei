using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Foundation;

public sealed class CancelBeforeTurn
{
    [Fact]
    public async Task S27_cancel_before_first_turn_precedes_no_turn()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.CancelGeneration();

        var result = await t.Engine.InvokeToolAsync("inspect_scene", "{}");

        Assert.Equal(ToolStatuses.Cancelled, result.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
    }
}
