using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

public sealed class ReviewS3KanaDetectionTests
{
    // Unicode Kana Supplement: U+1B000 KATAKANA LETTER ARCHAIC E;
    // U+1B001 HIRAGANA LETTER ARCHAIC YE.
    [Theory]
    [InlineData(0x1B000, "ja")]
    [InlineData(0x1B001, "ja")]
    [InlineData(0x1B000, "en")]
    [InlineData(0x1B001, "en")]
    public async Task S37b_supplementary_kana_obey_the_same_language_rules(int codePoint, string lang)
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, lang, char.ConvertFromUtf32(codePoint));
        if (lang == "ja")
        {
            Assert.Equal(ToolStatuses.Success, r.Status);
            Assert.Single(t.Speech.Requests);
        }
        else
        {
            Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
            Assert.Equal("split_language_segments", r.Detail);
            Assert.Empty(t.Speech.Requests);
        }
    }

    [Theory]
    [InlineData("japanese")]
    [InlineData("english")]
    public async Task S38c_spoken_hints_apply_the_supplementary_kana_language_rule(string field)
    {
        await using var t = await NewAsync();
        var content = Content();
        content[field] = char.ConvertFromUtf32(0x1B000);
        var r = await Hint(t, "explanation", content, speak: true);
        Assert.Equal(field == "japanese" ? ToolStatuses.Success : ToolStatuses.Failure, r.Status);
        if (field == "english") Assert.Equal("split_language_segments", r.Detail);
    }
}
