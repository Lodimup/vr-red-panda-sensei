using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner;

/// <summary>S44 relevant aids and the aid window (S94), S45 outcome order.</summary>
public sealed class OutcomeTests
{
    private static readonly SupportSettings NoCaptions = new() { ShowJapanese = false, ShowReading = false, ShowRomaji = false, ShowTranslation = false };

    // ---------------------------------------------------------------- S45 outcome order

    [Theory]
    [InlineData("unclear_input", true)]
    [InlineData("unclear_input", false)]
    public void S45_unclear_input_is_unresolved_whatever_the_success_flag(string assessment, bool success)
    {
        Assert.Equal(AttemptOutcomes.Unresolved, EvidenceRules.ClassifyOutcome(assessment, success, Array.Empty<string>()));
    }

    [Fact]
    public void S45_unclear_input_is_unresolved_even_with_a_demonstration()
    {
        Assert.Equal(AttemptOutcomes.Unresolved, EvidenceRules.ClassifyOutcome("unclear_input", true, new[] { AssistanceKinds.Demonstration }));
        Assert.Equal(AttemptOutcomes.Unresolved, EvidenceRules.ClassifyOutcome("unclear_input", false, new[] { AssistanceKinds.ModelAnswer }));
    }

    [Theory]
    [InlineData("correct")]
    [InlineData("minor_issue")]
    [InlineData("meaning_blocking_error")]
    public void S45_communicative_failure_is_confirmed_incorrect(string assessment)
    {
        Assert.Equal(AttemptOutcomes.ConfirmedIncorrect, EvidenceRules.ClassifyOutcome(assessment, false, Array.Empty<string>()));
    }

    [Fact]
    public void S45_communicative_failure_beats_every_aid_rule()
    {
        foreach (var aid in AssistanceKinds.All)
            Assert.Equal(AttemptOutcomes.ConfirmedIncorrect, EvidenceRules.ClassifyOutcome("correct", false, new[] { aid }));
    }

    [Fact]
    public void S45_demonstration_completes_by_demonstration()
    {
        Assert.Equal(AttemptOutcomes.CompletedByDemonstration, EvidenceRules.ClassifyOutcome("correct", true, new[] { AssistanceKinds.Demonstration }));
    }

    [Fact]
    public void S45_demonstration_beats_model_hint_and_repetition()
    {
        var aids = new[] { AssistanceKinds.ModelAnswer, AssistanceKinds.PartialPhrase, AssistanceKinds.Repetition, AssistanceKinds.Demonstration };
        Assert.Equal(AttemptOutcomes.CompletedByDemonstration, EvidenceRules.ClassifyOutcome("correct", true, aids));
    }

    [Theory]
    [InlineData(AssistanceKinds.ModelAnswer)]
    [InlineData(AssistanceKinds.AudioModel)]
    public void S45_model_aids_give_success_after_model(string aid)
    {
        Assert.Equal(AttemptOutcomes.SuccessAfterModel, EvidenceRules.ClassifyOutcome("correct", true, new[] { aid }));
    }

    [Fact]
    public void S45_model_beats_hint_and_repetition()
    {
        Assert.Equal(AttemptOutcomes.SuccessAfterModel, EvidenceRules.ClassifyOutcome("correct", true, new[] { AssistanceKinds.PartialPhrase, AssistanceKinds.Repetition, AssistanceKinds.AudioModel }));
    }

    [Theory]
    [InlineData(AssistanceKinds.TranscriptVisible)]
    [InlineData(AssistanceKinds.TranslationVisible)]
    [InlineData(AssistanceKinds.PartialPhrase)]
    [InlineData(AssistanceKinds.Explanation)]
    [InlineData(AssistanceKinds.VocabularyCard)]
    [InlineData(AssistanceKinds.TranslationHint)]
    [InlineData(AssistanceKinds.HighlightAnswer)]
    public void S45_any_other_aid_except_repetition_gives_success_after_hint(string aid)
    {
        Assert.Equal(AttemptOutcomes.SuccessAfterHint, EvidenceRules.ClassifyOutcome("correct", true, new[] { aid }));
    }

    [Fact]
    public void S45_a_hint_together_with_repetition_is_success_after_hint()
    {
        Assert.Equal(AttemptOutcomes.SuccessAfterHint, EvidenceRules.ClassifyOutcome("correct", true, new[] { AssistanceKinds.Repetition, AssistanceKinds.HighlightAnswer }));
    }

    [Fact]
    public void S45_repetition_alone_gives_success_after_repetition()
    {
        Assert.Equal(AttemptOutcomes.SuccessAfterRepetition, EvidenceRules.ClassifyOutcome("correct", true, new[] { AssistanceKinds.Repetition }));
    }

    [Theory]
    [InlineData("correct")]
    [InlineData("minor_issue")]
    [InlineData("meaning_blocking_error")]
    public void S45_no_aid_and_communicative_success_is_independent_success(string assessment)
    {
        Assert.Equal(AttemptOutcomes.IndependentSuccess, EvidenceRules.ClassifyOutcome(assessment, true, Array.Empty<string>()));
    }

    // ---------------------------------------------------------------- S44 relevant aids per dimension

    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["listening"] = new[] { "transcript_visible", "translation_visible", "highlight_answer", "demonstration", "repetition" },
        ["vocabulary"] = new[] { "model_answer", "partial_phrase", "vocabulary_card", "audio_model", "demonstration" },
        ["production"] = new[] { "model_answer", "partial_phrase", "audio_model", "demonstration" },
        ["reading"] = new[] { "translation_visible", "translation_hint", "audio_model", "highlight_answer" },
        ["interaction"] = new[] { "model_answer", "demonstration" },
        ["register"] = new[] { "model_answer", "demonstration" },
    };

    public static IEnumerable<object[]> DimensionKindPairs() =>
        from dimension in Table.Keys from kind in AssistanceKinds.All select new object[] { dimension, kind };

    [Theory]
    [InlineData("listening")]
    [InlineData("vocabulary")]
    [InlineData("production")]
    [InlineData("reading")]
    [InlineData("interaction")]
    [InlineData("register")]
    public void S44_relevant_aids_match_the_table(string dimension)
    {
        Assert.Equal(Table[dimension].OrderBy(x => x), EvidenceRules.RelevantAids(dimension).OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(DimensionKindPairs))]
    public void S44_a_record_counts_only_when_its_kind_is_relevant_to_the_dimension(string dimension, string kind)
    {
        var record = new AssistanceRecord { Kind = kind, TargetKey = "ringo", Sequence = 5 };
        var counted = EvidenceRules.CountedAids(dimension, "ringo", 0, 10, 0, NoCaptions, new[] { record });
        if (Table[dimension].Contains(kind)) Assert.Equal(new[] { kind }, counted);
        else Assert.Empty(counted);
    }

    [Fact]
    public void S44_explanation_is_relevant_to_no_dimension()
    {
        foreach (var dimension in Table.Keys)
            Assert.DoesNotContain(AssistanceKinds.Explanation, EvidenceRules.RelevantAids(dimension));
    }

    [Fact]
    public void S44_unknown_dimension_has_no_relevant_aid()
    {
        Assert.Empty(EvidenceRules.RelevantAids("grammar"));
    }

    // ---------------------------------------------------------------- S44/S94 support visibility

    [Fact]
    public void S44_listening_visible_japanese_counts_as_transcript_visible()
    {
        var support = new SupportSettings { ShowJapanese = true, ShowReading = false, ShowRomaji = false, ShowTranslation = false };
        Assert.Equal(new[] { AssistanceKinds.TranscriptVisible }, EvidenceRules.CountedAids("listening", "ringo", 0, 10, 0, support, Array.Empty<AssistanceRecord>()));
    }

    [Fact]
    public void S44_listening_visible_translation_counts_as_translation_visible()
    {
        var support = new SupportSettings { ShowJapanese = false, ShowReading = false, ShowRomaji = false, ShowTranslation = true };
        Assert.Equal(new[] { AssistanceKinds.TranslationVisible }, EvidenceRules.CountedAids("listening", "ringo", 0, 10, 0, support, Array.Empty<AssistanceRecord>()));
    }

    [Fact]
    public void S44_listening_reading_and_romaji_captions_are_not_aids()
    {
        var support = new SupportSettings { ShowJapanese = false, ShowReading = true, ShowRomaji = true, ShowTranslation = false };
        Assert.Empty(EvidenceRules.CountedAids("listening", "ringo", 0, 10, 0, support, Array.Empty<AssistanceRecord>()));
    }

    [Fact]
    public void S44_reading_visible_translation_counts_but_visible_japanese_does_not()
    {
        var onlyJapanese = new SupportSettings { ShowJapanese = true, ShowReading = true, ShowRomaji = true, ShowTranslation = false };
        Assert.Empty(EvidenceRules.CountedAids("reading", "ringo", 0, 10, 0, onlyJapanese, Array.Empty<AssistanceRecord>()));
        var translation = new SupportSettings { ShowJapanese = false, ShowReading = false, ShowRomaji = false, ShowTranslation = true };
        Assert.Equal(new[] { AssistanceKinds.TranslationVisible }, EvidenceRules.CountedAids("reading", "ringo", 0, 10, 0, translation, Array.Empty<AssistanceRecord>()));
    }

    [Theory]
    [InlineData("vocabulary")]
    [InlineData("production")]
    [InlineData("interaction")]
    [InlineData("register")]
    public void S44_visibility_settings_are_not_aids_for_other_dimensions(string dimension)
    {
        var allOn = new SupportSettings();
        Assert.Empty(EvidenceRules.CountedAids(dimension, "ringo", 0, 10, 0, allOn, Array.Empty<AssistanceRecord>()));
    }

    [Fact]
    public void S44_a_visibility_aid_and_a_record_of_the_same_kind_count_once()
    {
        var support = new SupportSettings { ShowJapanese = true, ShowReading = false, ShowRomaji = false, ShowTranslation = false };
        var record = new AssistanceRecord { Kind = AssistanceKinds.TranscriptVisible, TargetKey = "ringo", Sequence = 3 };
        Assert.Equal(new[] { AssistanceKinds.TranscriptVisible }, EvidenceRules.CountedAids("listening", "ringo", 0, 10, 0, support, new[] { record }));
    }

    // ---------------------------------------------------------------- S94 window

    private static IReadOnlyList<string> Window(string kind, long sequence, long boundary = 10, long source = 20, long previousUtterance = 0, string dimension = "production", string key = "ringo") =>
        EvidenceRules.CountedAids(dimension, key, boundary, source, previousUtterance, NoCaptions, new[] { new AssistanceRecord { Kind = kind, TargetKey = key, Sequence = sequence } });

    [Fact]
    public void S94_an_aid_must_be_strictly_after_the_boundary()
    {
        Assert.Empty(Window(AssistanceKinds.ModelAnswer, 10));
        Assert.Empty(Window(AssistanceKinds.ModelAnswer, 9));
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, Window(AssistanceKinds.ModelAnswer, 11));
    }

    [Fact]
    public void S94_an_aid_must_be_strictly_before_the_source_utterance()
    {
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, Window(AssistanceKinds.ModelAnswer, 19));
        Assert.Empty(Window(AssistanceKinds.ModelAnswer, 20));
        Assert.Empty(Window(AssistanceKinds.ModelAnswer, 21));
    }

    [Fact]
    public void S94_audio_model_must_be_presented_after_the_previous_learner_utterance()
    {
        Assert.Empty(Window(AssistanceKinds.AudioModel, 12, previousUtterance: 12));
        Assert.Empty(Window(AssistanceKinds.AudioModel, 12, previousUtterance: 15));
        Assert.Equal(new[] { AssistanceKinds.AudioModel }, Window(AssistanceKinds.AudioModel, 13, previousUtterance: 12));
    }

    [Fact]
    public void S94_the_previous_utterance_window_applies_only_to_audio_model()
    {
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, Window(AssistanceKinds.ModelAnswer, 12, previousUtterance: 15));
    }

    [Fact]
    public void S94_audio_model_before_the_boundary_does_not_count_even_inside_the_utterance_window()
    {
        Assert.Empty(Window(AssistanceKinds.AudioModel, 8, boundary: 10, previousUtterance: 5));
    }

    [Fact]
    public void S97_a_record_counts_for_its_own_dimension_and_key_only()
    {
        var record = new AssistanceRecord { Kind = AssistanceKinds.ModelAnswer, TargetKey = "ringo", TargetDimension = "production", Sequence = 12 };
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, EvidenceRules.CountedAids("production", "ringo", 10, 20, 0, NoCaptions, new[] { record }));
        Assert.Empty(EvidenceRules.CountedAids("vocabulary", "ringo", 10, 20, 0, NoCaptions, new[] { record }));
        Assert.Empty(EvidenceRules.CountedAids("production", "mizu", 10, 20, 0, NoCaptions, new[] { record }));
    }

    [Fact]
    public void S97_a_record_made_by_key_only_counts_for_every_dimension_using_that_key()
    {
        var record = new AssistanceRecord { Kind = AssistanceKinds.ModelAnswer, TargetKey = "ringo", TargetDimension = null, Sequence = 12 };
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, EvidenceRules.CountedAids("production", "ringo", 10, 20, 0, NoCaptions, new[] { record }));
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, EvidenceRules.CountedAids("interaction", "ringo", 10, 20, 0, NoCaptions, new[] { record }));
    }

    [Fact]
    public void S94_counted_aids_are_listed_once_in_the_order_they_were_recorded()
    {
        var records = new[]
        {
            new AssistanceRecord { Kind = AssistanceKinds.PartialPhrase, TargetKey = "ringo", Sequence = 14 },
            new AssistanceRecord { Kind = AssistanceKinds.ModelAnswer, TargetKey = "ringo", Sequence = 12 },
            new AssistanceRecord { Kind = AssistanceKinds.ModelAnswer, TargetKey = "ringo", Sequence = 16 },
        };
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer, AssistanceKinds.PartialPhrase }, EvidenceRules.CountedAids("production", "ringo", 10, 20, 0, NoCaptions, records));
    }

    // ---------------------------------------------------------------- S44/S45 through record_attempt

    [Fact]
    public async Task S45_no_aid_gives_independent_success_and_an_empty_assistance_list()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("independent_success", Str(r, "outcome"));
        Assert.Empty(DataOf(r).GetProperty("assistance").EnumerateArray());
    }

    [Theory]
    [InlineData("model_answer", "success_after_model")]
    [InlineData("audio_model", "success_after_model")]
    [InlineData("partial_phrase", "success_after_hint")]
    [InlineData("demonstration", "completed_by_demonstration")]
    public async Task S45_an_aid_before_the_learners_words_decides_the_outcome(string aid, string expected)
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        Assist(t, aid, "production", "ringo");
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal(expected, Str(r, "outcome"));
        Assert.Equal(new[] { aid }, StrArray(DataOf(r).GetProperty("assistance")));
    }

    [Fact]
    public async Task S94_an_aid_given_after_the_learners_words_does_not_count()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんごをください");
        Assist(t, "model_answer", "production", "ringo");
        var r = await Attempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("independent_success", Str(r, "outcome"));
        Assert.Empty(DataOf(r).GetProperty("assistance").EnumerateArray());
    }

    [Fact]
    public async Task S94_an_aid_for_another_key_does_not_count()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        Assist(t, "model_answer", "production", "mizu");
        var r = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("independent_success", Str(r, "outcome"));
    }

    [Fact]
    public async Task S94_an_aid_before_the_bound_task_started_does_not_count_and_one_after_does()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        Assist(t, "model_answer", "production", "ringo");
        StartTask(t, "production", "ringo");
        var before = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("independent_success", Str(before, "outcome"));
        Assert.Equal("task_active", Str(before, "engine_outcome"));

        await using var u = await TestEngine.CreateAsync();
        u.Engine.BeginTurn("start");
        StartTask(u, "production", "ringo");
        Assist(u, "model_answer", "production", "ringo");
        var after = await SayAndAttempt(u, "production", "ringo", "りんごをください");
        Assert.Equal("success_after_model", Str(after, "outcome"));
    }

    [Fact]
    public async Task S94_an_aid_given_before_the_previous_attempt_of_the_same_key_is_not_counted_again()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        Assist(t, "model_answer", "production", "ringo");
        var first = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("success_after_model", Str(first, "outcome"));
        var second = await SayAndAttempt(t, "production", "ringo", "りんごをどうぞ");
        Assert.Equal("independent_success", Str(second, "outcome"));
    }

    [Fact]
    public async Task S94_the_same_aid_still_counts_for_a_different_key_attempted_afterwards()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("start");
        Assist(t, "model_answer", "production", "ringo");
        Assist(t, "model_answer", "production", "mizu");
        var first = await SayAndAttempt(t, "production", "ringo", "りんごをください");
        Assert.Equal("success_after_model", Str(first, "outcome"));
        // mizu has no earlier attempt, so its boundary is the start of the session: the aid given before the first utterance still counts
        var other = await SayAndAttempt(t, "production", "mizu", "みずをください");
        Assert.Equal("success_after_model", Str(other, "outcome"));
        // ... but only for its first attempt
        var again = await SayAndAttempt(t, "production", "mizu", "みずをどうぞ");
        Assert.Equal("independent_success", Str(again, "outcome"));
    }

    [Fact]
    public async Task S94_audio_model_presented_before_the_previous_learner_utterance_does_not_count()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("first");
        Assist(t, "audio_model", "vocabulary", "ringo");
        t.Engine.BeginTurn("second");
        t.Engine.BeginTurn("りんご");
        var r = await Attempt(t, "vocabulary", "ringo", "りんご");
        Assert.Equal("independent_success", Str(r, "outcome"));
    }

    [Fact]
    public async Task S94_audio_model_presented_between_the_previous_utterance_and_the_source_counts()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("first");
        t.Engine.BeginTurn("second");
        Assist(t, "audio_model", "vocabulary", "ringo");
        t.Engine.BeginTurn("りんご");
        var r = await Attempt(t, "vocabulary", "ringo", "りんご");
        Assert.Equal("success_after_model", Str(r, "outcome"));
    }

    [Theory]
    [InlineData("model_answer", "production")]
    [InlineData("partial_phrase", "production")]
    [InlineData("vocabulary_card", "vocabulary")]
    [InlineData("audio_model", "production")]
    [InlineData("demonstration", "production")]
    [InlineData("model_answer", "interaction")]
    [InlineData("demonstration", "register")]
    [InlineData("translation_hint", "reading")]
    [InlineData("highlight_answer", "reading")]
    public async Task S44_through_the_tool_a_relevant_aid_is_counted(string kind, string dimension)
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false, ShowJapanese = false });
        t.Engine.BeginTurn("start");
        Assist(t, kind, dimension, "ringo");
        var r = await SayAndAttempt(t, dimension, "ringo", "りんご");
        Assert.Equal(new[] { kind }, StrArray(DataOf(r).GetProperty("assistance")));
        Assert.NotEqual("independent_success", Str(r, "outcome"));
    }

    [Theory]
    [InlineData("vocabulary_card", "production")]
    [InlineData("translation_hint", "production")]
    [InlineData("highlight_answer", "production")]
    [InlineData("repetition", "production")]
    [InlineData("explanation", "production")]
    [InlineData("partial_phrase", "interaction")]
    [InlineData("audio_model", "interaction")]
    [InlineData("model_answer", "reading")]
    [InlineData("partial_phrase", "reading")]
    [InlineData("repetition", "vocabulary")]
    [InlineData("highlight_answer", "vocabulary")]
    public async Task S44_through_the_tool_an_irrelevant_aid_is_not_counted(string kind, string dimension)
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false, ShowJapanese = false });
        t.Engine.BeginTurn("start");
        Assist(t, kind, dimension, "ringo");
        var r = await SayAndAttempt(t, dimension, "ringo", "りんご");
        Assert.Empty(DataOf(r).GetProperty("assistance").EnumerateArray());
        Assert.Equal("independent_success", Str(r, "outcome"));
    }

    [Fact]
    public async Task S44_reading_with_the_translation_shown_is_not_independent()
    {
        await using var t = await TestEngine.CreateAsync();
        var r = await SayAndAttempt(t, "reading", "ringo", "りんご");
        Assert.Equal("success_after_hint", Str(r, "outcome"));
        Assert.Equal(new[] { "translation_visible" }, StrArray(DataOf(r).GetProperty("assistance")));
    }

    [Fact]
    public async Task S94_support_visibility_is_the_snapshot_taken_when_the_source_utterance_arrived()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false });
        t.Engine.BeginTurn("りんご");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = true });
        var r = await Attempt(t, "reading", "ringo", "りんご");
        Assert.Equal("independent_success", Str(r, "outcome"));
    }

    [Fact]
    public async Task S94_support_shown_at_arrival_counts_even_if_hidden_before_the_attempt_is_recorded()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんご");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false });
        var r = await Attempt(t, "reading", "ringo", "りんご");
        Assert.Equal("success_after_hint", Str(r, "outcome"));
    }

    [Fact]
    public async Task S45_communicative_failure_and_unclear_input_through_the_tool()
    {
        await using var t = await TestEngine.CreateAsync();
        var wrong = await SayAndAttempt(t, "production", "ringo", "りんごをください", success: false, assessment: "meaning_blocking_error");
        Assert.Equal("confirmed_incorrect", Str(wrong, "outcome"));
        var unclear = await SayAndAttempt(t, "production", "mizu", "みず", assessment: "unclear_input");
        Assert.Equal("unresolved", Str(unclear, "outcome"));
        Assert.Equal("high", Str(unclear, "uncertainty"));
    }
}
