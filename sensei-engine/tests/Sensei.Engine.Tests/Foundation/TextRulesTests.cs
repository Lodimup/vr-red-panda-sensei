using Sensei.Engine.Text;

namespace Sensei.Engine.Tests.Foundation;

public sealed class TextRulesTests
{
    // ------------------------------------------------------------------ ScalarLength (S37a)

    [Fact]
    public void S37a_ScalarLength_counts_a_non_BMP_char_as_one()
    {
        var text = "\U00020BB7"; // 𠮷
        Assert.Equal(2, text.Length);
        Assert.Equal(1, TextRules.ScalarLength(text));
        Assert.Equal(1, TextRules.ScalarLength("𠮷"));
    }

    [Fact]
    public void S37a_ScalarLength_counts_an_emoji_as_one()
    {
        Assert.Equal(1, TextRules.ScalarLength("😀"));
        Assert.Equal(3, TextRules.ScalarLength("a😀b"));
    }

    [Fact]
    public void S37a_ScalarLength_counts_each_non_BMP_char_in_a_run()
    {
        Assert.Equal(3, TextRules.ScalarLength("𠮷𠮷𠮷"));
        Assert.Equal(3, TextRules.ScalarLength("𠮷a𠮷"));
    }

    [Fact]
    public void S37a_ScalarLength_counts_combining_marks_separately()
    {
        Assert.Equal(2, TextRules.ScalarLength("é"));
        Assert.Equal(1, TextRules.ScalarLength("é"));
        Assert.Equal(2, TextRules.ScalarLength("が"));
        Assert.Equal(1, TextRules.ScalarLength("が"));
    }

    [Fact]
    public void S37a_ScalarLength_of_empty_and_null_is_zero()
    {
        Assert.Equal(0, TextRules.ScalarLength(""));
        Assert.Equal(0, TextRules.ScalarLength(null));
    }

    [Theory]
    [InlineData("a", 1)]
    [InlineData("abc", 3)]
    [InlineData("りんご", 3)]
    [InlineData("お茶をください", 7)]
    [InlineData("a b", 3)]
    [InlineData(" ", 1)]
    public void S37a_ScalarLength_of_BMP_text_equals_its_char_count(string text, int expected)
    {
        Assert.Equal(expected, TextRules.ScalarLength(text));
        Assert.Equal(text.Length, TextRules.ScalarLength(text));
    }

    [Fact]
    public void S37a_ScalarLength_counts_a_lone_surrogate_as_one()
    {
        Assert.Equal(1, TextRules.ScalarLength("\uD842"));
        Assert.Equal(1, TextRules.ScalarLength("\uDFB7"));
        Assert.Equal(2, TextRules.ScalarLength("\uD842a"));
    }

    [Fact]
    public void S37a_ScalarLength_60_scalar_boundary_text_counts_exactly_60()
    {
        var sixty = string.Concat(Enumerable.Repeat("𠮷", 60));
        Assert.Equal(120, sixty.Length);
        Assert.Equal(60, TextRules.ScalarLength(sixty));
        Assert.Equal(61, TextRules.ScalarLength(sixty + "a"));
    }

    // ------------------------------------------------------------------ NormalizeForMatch (S43)

    [Fact]
    public void S43_NormalizeForMatch_applies_NFKC_to_full_width_latin()
    {
        Assert.Equal("abc", TextRules.NormalizeForMatch("ＡＢＣ"));
        Assert.Equal("abc", TextRules.NormalizeForMatch("ａｂｃ"));
        Assert.Equal("123", TextRules.NormalizeForMatch("１２３"));
    }

    [Fact]
    public void S43_NormalizeForMatch_composes_half_width_katakana_with_voiced_mark()
    {
        Assert.Equal("ガ", TextRules.NormalizeForMatch("ｶﾞ"));
        Assert.Equal("コップ", TextRules.NormalizeForMatch("ｺｯﾌﾟ"));
    }

    [Fact]
    public void S43_NormalizeForMatch_composes_combining_marks()
    {
        Assert.Equal("é", TextRules.NormalizeForMatch("é"));
        Assert.Equal("が", TextRules.NormalizeForMatch("が"));
    }

    [Fact]
    public void S43_NormalizeForMatch_lowercases()
    {
        Assert.Equal("hello world", TextRules.NormalizeForMatch("HeLLo WORLD"));
        Assert.Equal("abc", TextRules.NormalizeForMatch("ABC"));
    }

    [Fact]
    public void S43_NormalizeForMatch_leaves_Japanese_text_unchanged()
    {
        Assert.Equal("お茶をください", TextRules.NormalizeForMatch("お茶をください"));
    }

    [Fact]
    public void S43_NormalizeForMatch_collapses_runs_of_ASCII_whitespace()
    {
        Assert.Equal("a b c", TextRules.NormalizeForMatch("a   b\t\n c"));
        Assert.Equal("a b", TextRules.NormalizeForMatch("a \r\n\t b"));
    }

    [Fact]
    public void S43_NormalizeForMatch_collapses_ideographic_space()
    {
        Assert.Equal("a b", TextRules.NormalizeForMatch("a　　b"));
        Assert.Equal("a b", TextRules.NormalizeForMatch("a　 　b"));
        Assert.Equal("お茶 ください", TextRules.NormalizeForMatch("お茶　　ください"));
    }

    [Fact]
    public void S43_NormalizeForMatch_treats_a_no_break_space_as_whitespace()
    {
        Assert.Equal("a b", TextRules.NormalizeForMatch("a b"));
    }

    [Fact]
    public void S43_NormalizeForMatch_trims_leading_and_trailing_whitespace()
    {
        Assert.Equal("a", TextRules.NormalizeForMatch("  a  "));
        Assert.Equal("a b", TextRules.NormalizeForMatch("　a b\t\n"));
    }

    [Fact]
    public void S43_NormalizeForMatch_of_whitespace_only_empty_and_null_is_empty()
    {
        Assert.Equal("", TextRules.NormalizeForMatch("   \t\n　"));
        Assert.Equal("", TextRules.NormalizeForMatch(""));
        Assert.Equal("", TextRules.NormalizeForMatch(null));
    }

    [Fact]
    public void S43_NormalizeForMatch_combines_all_steps_so_variants_compare_equal()
    {
        var a = TextRules.NormalizeForMatch("Ｈｅｌｌｏ　  ＷＯＲＬＤ ");
        var b = TextRules.NormalizeForMatch("hello world");
        Assert.Equal(b, a);
    }

    // ------------------------------------------------------------------ NormalizeNoWhitespace (S37e)

    [Fact]
    public void S37e_NormalizeNoWhitespace_removes_all_whitespace()
    {
        Assert.Equal("abcd", TextRules.NormalizeNoWhitespace("a b\tc\n d"));
        Assert.Equal("abcd", TextRules.NormalizeNoWhitespace("  a b c d  "));
    }

    [Fact]
    public void S37e_NormalizeNoWhitespace_removes_ideographic_and_no_break_spaces()
    {
        Assert.Equal("お茶をください", TextRules.NormalizeNoWhitespace("お茶　を ください "));
    }

    [Fact]
    public void S37e_NormalizeNoWhitespace_applies_NFKC_but_keeps_case()
    {
        Assert.Equal("ABC", TextRules.NormalizeNoWhitespace("Ａ Ｂ Ｃ"));
        Assert.Equal("AbC", TextRules.NormalizeNoWhitespace("A b C"));
    }

    [Fact]
    public void S37e_NormalizeNoWhitespace_of_whitespace_only_empty_and_null_is_empty()
    {
        Assert.Equal("", TextRules.NormalizeNoWhitespace(" \t\n　"));
        Assert.Equal("", TextRules.NormalizeNoWhitespace(""));
        Assert.Equal("", TextRules.NormalizeNoWhitespace(null));
    }

    [Fact]
    public void S37e_NormalizeNoWhitespace_lets_a_spaced_model_phrase_match_the_unspaced_target()
    {
        var target = TextRules.NormalizeNoWhitespace("コップ");
        var spoken = TextRules.NormalizeNoWhitespace("これは コ ッ プ です");
        Assert.Contains(target, spoken);
    }

    // ------------------------------------------------------------------ ContainsJapanese

    [Theory]
    [InlineData("あ")]
    [InlineData("りんご")]
    [InlineData("ア")]
    [InlineData("コップ")]
    [InlineData("ｱ")]
    [InlineData("ｺｯﾌﾟ")]
    [InlineData("水")]
    [InlineData("お茶")]
    [InlineData("𠮷")]
    [InlineData("々")]
    [InlineData("hello りんご")]
    [InlineData("I want 水 please")]
    [InlineData("abc123か")]
    public void S37_ContainsJapanese_is_true_for_hiragana_katakana_half_width_katakana_and_kanji(string text)
    {
        Assert.True(TextRules.ContainsJapanese(text), text);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("Water, please.")]
    [InlineData("12345")]
    [InlineData("!?.,;:-_()[]")]
    [InlineData("   ")]
    [InlineData("ＡＢＣ")]
    [InlineData("😀")]
    [InlineData("café")]
    [InlineData("привет")]
    [InlineData("")]
    public void S37_ContainsJapanese_is_false_for_latin_digits_punctuation_and_other_scripts(string text)
    {
        Assert.False(TextRules.ContainsJapanese(text), text);
    }

    [Fact]
    public void S37_ContainsJapanese_is_false_for_null()
    {
        Assert.False(TextRules.ContainsJapanese(null));
    }

    [Fact]
    public void S37_ContainsJapanese_finds_a_non_BMP_kanji_after_a_surrogate_pair_of_another_script()
    {
        Assert.True(TextRules.ContainsJapanese("😀𠮷"));
        Assert.False(TextRules.ContainsJapanese("😀😀"));
    }

    [Theory]
    [InlineData(0x3040, true)]
    [InlineData(0x309F, true)]
    [InlineData(0x30A0, true)]
    [InlineData(0x30FF, true)]
    [InlineData(0xFF66, true)]
    [InlineData(0xFF9F, true)]
    [InlineData(0x4E00, true)]
    [InlineData(0x9FFF, true)]
    [InlineData(0x3400, true)]
    [InlineData(0x20000, true)]
    [InlineData(0x303F, false)]
    [InlineData(0x0041, false)]
    [InlineData(0x0030, false)]
    [InlineData(0xFF21, false)]
    public void S37_IsJapaneseCodePoint_matches_the_documented_ranges_at_their_edges(int codePoint, bool expected)
    {
        Assert.Equal(expected, TextRules.IsJapaneseCodePoint(codePoint));
    }

    // ------------------------------------------------------------------ ContainsIgnoreCase

    [Theory]
    [InlineData("Hello World", "world", true)]
    [InlineData("Hello World", "WORLD", true)]
    [InlineData("Hello World", "lo wo", true)]
    [InlineData("Hello World", "xyz", false)]
    [InlineData("Hello", "", true)]
    [InlineData("", "a", false)]
    public void S80_ContainsIgnoreCase_matches_ordinal_ignoring_case(string text, string value, bool expected)
    {
        Assert.Equal(expected, TextRules.ContainsIgnoreCase(text, value));
    }

    [Fact]
    public void S80_ContainsIgnoreCase_is_false_for_null_text()
    {
        Assert.False(TextRules.ContainsIgnoreCase(null, "a"));
    }
}
