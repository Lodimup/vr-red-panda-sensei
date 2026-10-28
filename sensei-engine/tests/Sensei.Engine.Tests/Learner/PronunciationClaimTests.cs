using System.Text.Json.Nodes;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;

namespace Sensei.Engine.Tests.Learner;

public sealed class PronunciationClaimTests
{
    [Theory]
    [InlineData("remember_note", "key")]
    [InlineData("remember_note", "value")]
    [InlineData("record_attempt", "key")]
    [InlineData("record_attempt", "japanese")]
    [InlineData("record_attempt", "transcript_excerpt")]
    [InlineData("record_attempt", "note")]
    [InlineData("record_attempt", "task_id")]
    [InlineData("show_hint", "japanese")]
    [InlineData("show_hint", "reading")]
    [InlineData("show_hint", "romaji")]
    [InlineData("show_hint", "english")]
    [InlineData("show_hint", "target_key")]
    public async Task S141_each_free_text_field_is_rejected_without_effect(string tool, string field)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("accent");
        var args = tool switch
        {
            "remember_note" => new JsonObject { ["key"] = "goal", ["value"] = "travel" },
            "record_attempt" => new JsonObject { ["target"] = new JsonObject { ["dimension"] = "vocabulary", ["key"] = "apple" }, ["transcript_excerpt"] = "accent", ["communicative_success"] = true, ["language_assessment"] = "correct" },
            _ => new JsonObject { ["kind"] = "explanation", ["content"] = new JsonObject { ["english"] = "hello" } },
        };
        if (tool == "record_attempt" && field != "transcript_excerpt") args["transcript_excerpt"] = "word";
        if (tool == "record_attempt" && (field == "key" || field == "japanese")) args["target"]![field] = "accent";
        else if (tool == "show_hint" && field != "target_key") args["content"]![field] = "accent";
        else args[field] = "accent";
        var saves = t.Store.SaveCount;
        var r = await t.CallAsync(tool, args.ToJsonString());
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("pronunciation_claim", r.Detail);
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Empty(t.Events.Hints);
        Assert.Empty(t.Store.Peek("default")?.Evidence ?? new());
        Assert.Empty(t.Store.Peek("default")?.Notes ?? new());
    }

    [Theory]
    [InlineData("PrOnUnCiAtIoN")]
    [InlineData("ACCENT")]
    [InlineData("Intonation")]
    [InlineData("PiTcH")]
    [InlineData("ピッチ")]
    [InlineData("発音")]
    [InlineData("アクセント")]
    [InlineData("イントネーション")]
    public async Task S141_all_terms_are_matched(string term)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        var r = await t.CallAsync("remember_note", new JsonObject { ["key"] = "goal", ["value"] = term }.ToJsonString());
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("pronunciation_claim", r.Detail);
    }

    [Fact]
    public async Task S141_speech_can_teach_pronunciation_and_ordinary_notes_pass()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("hello");
        Assert.True((await t.CallAsync("remember_note", """{"key":"goal","value":"travel"}""")).IsSuccess);
        Assert.True((await t.CallAsync("speak", """{"segments":[{"lang":"en","text":"Let's practice pronunciation."}]}""")).IsSuccess);
    }
}
