using System.Text.Json.Nodes;
using Sensei.Engine.Tests.TestKit;

namespace Sensei.Engine.Tests.ReviewR4;

public class GuardTests
{
    [Theory]
    [InlineData("You're at level N4.")]
    [InlineData("あなたの日本語はN4です。")]
    public async Task CannotTellLearnerActualLevel(string text)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Core.Learner.Profile.WorkingLevel = "n4";
        t.Engine.BeginTurn("hello");
        var payload = new JsonObject { ["segments"] = new JsonArray(new JsonObject { ["lang"] = text.Contains('あ') ? "ja" : "en", ["text"] = text }) };
        var r = await t.CallAsync("speak", payload.ToJsonString());
        Assert.DoesNotContain(t.Speech.Requests.SelectMany(x => x.Segments), segment => segment.Text == text);
    }

    [Fact]
    public async Task VocabularyCardCannotDeliverPronunciationVerdict()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんご");
        var payload = new JsonObject { ["kind"] = "vocabulary_card", ["content"] = new JsonObject { ["japanese"] = "りんご", ["english"] = "Your pronunciation of apple was wrong." }, ["speak"] = true };
        var r = await t.CallAsync("show_hint", payload.ToJsonString());
        Assert.DoesNotContain(t.Events.Hints, hint => hint.English == "Your pronunciation of apple was wrong.");
    }
}
