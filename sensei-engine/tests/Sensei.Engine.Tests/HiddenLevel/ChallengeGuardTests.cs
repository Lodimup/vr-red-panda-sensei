using System.Text.Json;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.World.WorldKit;

namespace Sensei.Engine.Tests.HiddenLevel;

public sealed class ChallengeGuardTests
{
    [Theory]
    [InlineData("japanese")]
    [InlineData("english")]
    [InlineData("role")]
    [InlineData("scenario")]
    [InlineData("skill")]
    public async Task Start_refuses_hidden_level_in_each_presentation_field(string field)
    {
        await using var t = await StartAsync(("apple", "left"));
        var instruction = field is "japanese" or "english"
            ? $$"""{"{{field}}":"WoRkInG_LeVeL"}""" : """{"english":"Move the apple."}""";
        var targets = field == "skill"
            ? """[{"dimension":"listening","key":"apple","japanese":"working level"}]""" : null;
        var extra = field switch
        {
            "role" => "\"role\":\"working level\"",
            "scenario" => "\"scenario_properties\":[{\"object_id\":\"apple_1\",\"name\":\"state\",\"value\":\"working level\"}]",
            _ => null
        };
        var json = StartJson(Predicates(Obj("apple_1", "right")), instruction: instruction, targets: targets, extra: extra);
        var result = await AssertRefusedAsync(t, "start_challenge", json, ToolErrorCodes.InvalidArguments, "hidden_level");
        Assert.Equal("hidden_level", result.Detail);
        Assert.Null(ActiveTask(t));
    }

    [Theory]
    [InlineData("japanese")]
    [InlineData("english")]
    public async Task Revise_refuses_hidden_level_and_leaves_task_unchanged(string field)
    {
        await using var t = await StartAsync(("apple", "left"));
        await StartTaskAsync(t, Predicates(Obj("apple_1", "right")));
        var before = JsonSerializer.Serialize(ActiveTask(t));
        var json = "{\"task_id\":\"t1\",\"action\":\"revise\",\"reason\":\"clarify\",\"instruction\":{\"" + field + "\":\"working level\"}}";
        var result = await AssertRefusedAsync(t, "update_challenge", json, ToolErrorCodes.InvalidArguments, "hidden_level");
        Assert.Equal("hidden_level", result.Detail);
        Assert.Equal(before, JsonSerializer.Serialize(ActiveTask(t)));
    }

    [Fact]
    public async Task Ordinary_instructions_are_allowed()
    {
        await using var t = await StartAsync(("apple", "left"));
        var result = await t.CallAsync("start_challenge", StartJson(Predicates(Obj("apple_1", "right")), instruction: """{"english":"Move the apple. This is a beginner word."}"""));
        Assert.True(result.IsSuccess, result.Detail);
        var update = await t.CallAsync("update_challenge", """{"task_id":"t1","action":"revise","reason":"clarify","instruction":{"japanese":"りんごを右へ"}}""");
        Assert.True(update.IsSuccess, update.Detail);
    }
}
