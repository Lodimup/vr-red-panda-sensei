using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

/// <summary>speak: S37a–S37e, S80, S89, S91.</summary>
public sealed class SpeakTests
{
    // ------------------------------------------------------------ S37a: limits in Unicode scalar values

    [Fact]
    public async Task S37a_ja_text_of_60_characters_is_accepted()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "ja", Japanese(60));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Single(t.Speech.Requests);
    }

    [Fact]
    public async Task S37a_ja_text_of_61_characters_is_speech_limit_exceeded_and_nothing_is_spoken()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "ja", Japanese(61));
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
        Assert.Empty(t.Speech.Requests);
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37a_en_text_of_160_characters_is_accepted()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "en", English(160));
        Assert.Equal(ToolStatuses.Success, r.Status);
    }

    [Fact]
    public async Task S37a_en_text_of_161_characters_is_speech_limit_exceeded()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "en", English(161));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S37a_ja_limit_counts_scalar_values_so_60_non_bmp_characters_are_accepted()
    {
        await using var t = await NewAsync();
        var text = Repeat(NonBmpKanji, 60); // 120 UTF-16 code units, 60 scalar values
        Assert.Equal(120, text.Length);
        var r = await SpeakOne(t, "ja", text);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal(text, t.Speech.Requests.Single().Segments[0].Text);
    }

    [Fact]
    public async Task S37a_ja_limit_counts_scalar_values_so_61_non_bmp_characters_are_refused()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "ja", Repeat(NonBmpKanji, 61));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
    }

    [Fact]
    public async Task S37a_en_limit_counts_scalar_values_so_160_emoji_are_accepted_and_161_are_refused()
    {
        await using var t = await NewAsync();
        var ok = await SpeakOne(t, "en", Repeat(NonBmpEmoji, 160)); // 320 UTF-16 code units
        Assert.Equal(ToolStatuses.Success, ok.Status);
        t.Engine.BeginTurn("again");
        var tooLong = await SpeakOne(t, "en", Repeat(NonBmpEmoji, 161));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, tooLong.ErrorCode);
    }

    [Fact]
    public async Task S37a_the_limit_applies_to_each_segment_not_to_the_call()
    {
        await using var t = await NewAsync();
        var r = await Speak(t, Segment("ja", Japanese(60)), Segment("en", English(160)), Segment("ja", Japanese(60)));
        Assert.Equal(ToolStatuses.Success, r.Status); // 280 characters in one call
        t.Engine.BeginTurn("next");
        var over = await Speak(t, Segment("en", English(10)), Segment("en", English(161)));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, over.ErrorCode);
        Assert.Contains("segments[1]", over.Detail);
    }

    [Fact]
    public async Task S37a_an_empty_text_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await SpeakOne(t, "ja", "")).ErrorCode);
    }

    [Fact]
    public async Task S37a_zero_segments_and_seven_segments_are_invalid_arguments()
    {
        await using var t = await NewAsync();
        var none = await t.CallAsync("speak", """{"segments":[]}""");
        Assert.Equal(ToolErrorCodes.InvalidArguments, none.ErrorCode);
        var seven = await Speak(t, Enumerable.Range(0, 7).Select(_ => Segment("en", "hi")).ToArray());
        Assert.Equal(ToolErrorCodes.InvalidArguments, seven.ErrorCode);
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S37a_six_segments_are_accepted()
    {
        await using var t = await NewAsync();
        var r = await Speak(t, Enumerable.Range(0, 6).Select(_ => Segment("en", "hi")).ToArray());
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal(6, t.Speech.Requests.Single().Segments.Count);
    }

    // ------------------------------------------------------------ S37b: one language per segment

    [Theory]
    [InlineData("あ")]            // hiragana
    [InlineData("ア")]            // katakana
    [InlineData("ｱ")]            // half-width katakana
    [InlineData("水")]            // kanji
    [InlineData("々")]            // iteration mark
    [InlineData("こんにちはHello")] // mixed with Latin
    public async Task S37b_a_ja_segment_with_kana_or_kanji_is_accepted(string text)
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "ja", text)).Status);
    }

    [Fact]
    public async Task S37b_a_ja_segment_with_a_non_bmp_kanji_is_accepted()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "ja", NonBmpKanji)).Status);
    }

    [Theory]
    [InlineData("ringo")]
    [InlineData("ohayou gozaimasu")]
    [InlineData("123")]
    [InlineData("!?")]
    public async Task S37b_a_romaji_only_ja_segment_is_split_language_segments(string text)
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "ja", text);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("split_language_segments", r.Detail);
        Assert.Empty(t.Speech.Requests);
    }

    [Theory]
    [InlineData("I like りんご")]   // hiragana
    [InlineData("Say コップ")]       // katakana
    [InlineData("The 水 is cold")]  // kanji
    [InlineData("ｱｯﾌﾟﾙ")]           // half-width katakana
    [InlineData("々")]
    public async Task S37b_an_en_segment_with_kana_or_kanji_is_split_language_segments(string text)
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "en", text);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("split_language_segments", r.Detail);
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S37b_an_en_segment_with_a_non_bmp_kanji_is_split_language_segments()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "en", "look: " + NonBmpKanji);
        Assert.Equal("split_language_segments", r.Detail);
    }

    [Theory]
    [InlineData("It means water. Ready? Say: mizu!")]
    [InlineData("Café — naïve, ok?")]
    [InlineData("😀 well done")]
    [InlineData("한국어")] // other scripts are not Japanese
    public async Task S37b_an_en_segment_without_japanese_characters_is_accepted(string text)
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", text)).Status);
    }

    [Fact]
    public async Task S37b_the_first_wrong_segment_decides_and_nothing_is_spoken()
    {
        await using var t = await NewAsync();
        var r = await Speak(t, Segment("en", "Say it:"), Segment("ja", "ringo"), Segment("en", "ok"));
        Assert.Equal("split_language_segments", r.Detail);
        Assert.Empty(t.Speech.Requests);
        Assert.Empty(History(t));
    }

    [Fact]
    public async Task S37a_is_checked_before_S37b()
    {
        await using var t = await NewAsync();
        var r = await SpeakOne(t, "ja", English(61)); // too long and romaji-only
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
    }

    [Fact]
    public async Task S21_an_unknown_lang_or_pace_or_property_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await SpeakOne(t, "fr", "bonjour")).ErrorCode);
        t.Engine.BeginTurn("n2");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await SpeakOne(t, "en", "hi", pace: "fast")).ErrorCode);
        t.Engine.BeginTurn("n3");
        Assert.Equal(ToolErrorCodes.InvalidArguments,
            (await t.CallAsync("speak", """{"segments":[{"lang":"en","text":"hi","volume":3}]}""")).ErrorCode);
        t.Engine.BeginTurn("n4");
        Assert.Equal(ToolErrorCodes.InvalidArguments,
            (await t.CallAsync("speak", """{"segments":[{"lang":"en"}]}""")).ErrorCode);
    }

    // ------------------------------------------------------------ S37c: 480 characters per turn

    [Fact]
    public async Task S37c_exactly_480_characters_in_a_turn_are_accepted()
    {
        await using var t = await NewAsync();
        for (var i = 0; i < 3; i++)
            Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(160))).Status);
        Assert.Equal(480, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37c_the_481st_character_is_refused_and_nothing_is_spoken()
    {
        await using var t = await NewAsync();
        for (var i = 0; i < 3; i++) await SpeakOne(t, "en", English(160));
        var r = await SpeakOne(t, "en", "a");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
        Assert.Equal(3, t.Speech.Requests.Count);
        Assert.Equal(480, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37c_a_call_that_would_pass_the_total_is_refused_whole_while_a_smaller_one_still_fits()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", English(160));
        await SpeakOne(t, "en", English(160));
        await SpeakOne(t, "en", English(159)); // 479
        var two = await SpeakOne(t, "en", "ab");
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, two.ErrorCode);
        var one = await SpeakOne(t, "en", "a");
        Assert.Equal(ToolStatuses.Success, one.Status);
        Assert.Equal(480, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37c_one_call_with_segments_adding_up_past_480_is_refused_whole()
    {
        await using var t = await NewAsync();
        var r = await Speak(t, Segment("en", English(150)), Segment("en", English(150)), Segment("en", English(150)), Segment("en", English(31)));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
        Assert.Empty(t.Speech.Requests);
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37c_the_total_counts_scalar_values_across_languages()
    {
        await using var t = await NewAsync();
        await Speak(t, Segment("ja", Repeat(NonBmpKanji, 60)), Segment("en", Repeat(NonBmpEmoji, 160))); // 220
        await Speak(t, Segment("ja", Japanese(60)), Segment("en", English(160))); // 440
        Assert.Equal(440, SpokenCharacters(t));
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(40))).Status);
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, (await SpeakOne(t, "ja", "あ")).ErrorCode);
    }

    [Fact]
    public async Task S37c_a_new_turn_starts_again_from_zero()
    {
        await using var t = await NewAsync();
        for (var i = 0; i < 3; i++) await SpeakOne(t, "en", English(160));
        t.Engine.BeginTurn("next turn");
        Assert.Equal(0, SpokenCharacters(t));
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(160))).Status);
    }

    [Fact]
    public async Task S37c_a_failed_speech_that_presented_nothing_gives_its_characters_back()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "no audio device");
        for (var i = 0; i < 3; i++)
            Assert.Equal(ToolErrorCodes.SpeechFailed, (await SpeakOne(t, "en", English(160))).ErrorCode);
        Assert.Equal(0, SpokenCharacters(t));
        t.Speech.Outcome = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(160))).Status);
    }

    [Fact]
    public async Task S37c_only_the_segments_that_reached_the_learner_count()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 1, "stopped");
        await Speak(t, Segment("en", English(100)), Segment("en", English(100)), Segment("en", English(100)));
        Assert.Equal(100, SpokenCharacters(t));
        t.Speech.Outcome = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(160))).Status);
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(160))).Status);
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", English(60))).Status); // 480
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, (await SpeakOne(t, "en", "a")).ErrorCode);
    }

    [Fact]
    public async Task S37c_a_replay_does_not_count_toward_the_480()
    {
        await using var t = await NewAsync();
        for (var i = 0; i < 3; i++) await SpeakOne(t, "en", English(160));
        var replay = await Replay(t);
        Assert.Equal(ToolStatuses.Success, replay.Status);
        Assert.Equal(480, SpokenCharacters(t));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, (await SpeakOne(t, "en", "a")).ErrorCode);
    }

    // ------------------------------------------------------------ S37d: outcomes

    [Fact]
    public async Task S37d_a_completed_speech_is_success_with_utterance_id_and_outcome()
    {
        await using var t = await NewAsync();
        var r = await Speak(t, Segment("ja", "りんご", "りんご", "ringo", "apple"), Segment("en", "That is an apple."));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Null(r.ErrorCode);
        Assert.Equal("u1", DataString(r, "utterance_id"));
        Assert.Equal("completed", DataString(r, "outcome"));
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S37d_the_request_carries_the_utterance_id_current_generation_segments_and_default_pace()
    {
        await using var t = await NewAsync();
        await Speak(t, Segment("ja", "りんご", "りんご", "ringo", "apple"), Segment("en", "That is an apple."));
        var request = t.Speech.Requests.Single();
        Assert.Equal("u1", request.UtteranceId);
        long generation;
        lock (t.Core.Sync) generation = t.Core.CurrentTurn!.GenerationId;
        Assert.Equal(generation, request.GenerationId);
        Assert.Equal("normal", request.Pace);
        Assert.Equal(new SpeechSegment("ja", "りんご", "りんご", "ringo", "apple"), request.Segments[0]);
        Assert.Equal(new SpeechSegment("en", "That is an apple.", null, null, null), request.Segments[1]);
    }

    [Fact]
    public async Task S37d_pace_defaults_to_the_support_pace_and_an_explicit_pace_wins()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "one");
        await SpeakOne(t, "en", "two", pace: "slow");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        await SpeakOne(t, "en", "three");
        await SpeakOne(t, "en", "four", pace: "normal");
        Assert.Equal(new[] { "normal", "slow", "slow", "normal" }, t.Speech.Requests.Select(r => r.Pace));
    }

    [Fact]
    public async Task S37d_utterance_ids_count_up_and_a_refused_call_does_not_use_one()
    {
        await using var t = await NewAsync();
        Assert.Equal("u1", DataString(await SpeakOne(t, "en", "a"), "utterance_id"));
        await SpeakOne(t, "en", English(161)); // refused
        Assert.Equal("u2", DataString(await SpeakOne(t, "en", "b"), "utterance_id"));
    }

    [Fact]
    public async Task S37d_a_cancelled_outcome_is_a_cancelled_result()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 0);
        var r = await SpeakOne(t, "en", "hello");
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, r.ErrorCode);
    }

    [Fact]
    public async Task S37d_a_failed_outcome_is_failure_speech_failed_with_the_port_error_in_the_detail()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "no audio device");
        var r = await SpeakOne(t, "en", "hello");
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Contains("no audio device", r.Detail);
        Assert.False(HasData(r, "utterance_id")); // nothing reached the learner
    }

    [Fact]
    public async Task S37d_a_speech_port_that_throws_is_speech_failed()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = _ => throw new InvalidOperationException("tts crashed");
        var r = await SpeakOne(t, "en", "hello");
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Contains("tts crashed", r.Detail);
        Assert.Empty(History(t));
    }

    [Fact]
    public async Task S25_a_speech_failure_does_not_count_as_a_validation_failure()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "no audio device");
        await SpeakOne(t, "en", "one");
        await SpeakOne(t, "en", "two");
        await SpeakOne(t, "en", "three");
        t.Speech.Outcome = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", "four")).Status);
    }

    [Fact]
    public async Task S37d_a_speak_call_is_kept_in_speech_history_with_what_was_said()
    {
        await using var t = await NewAsync();
        await Speak(t, Segment("ja", "水", "みず", "mizu"), Segment("en", "water"));
        var entry = History(t).Single();
        Assert.Equal("u1", entry.UtteranceId);
        Assert.Equal(2, entry.SegmentsPresented);
        Assert.Equal("completed", entry.Outcome);
        Assert.Equal("normal", entry.Pace);
        Assert.False(entry.IsReplay);
        Assert.Equal(new[] { "水", "water" }, entry.Segments.Select(s => s.Text));
    }

    [Fact]
    public async Task S37d_speech_history_keeps_the_last_20_utterances()
    {
        await using var t = await NewAsync();
        for (var i = 1; i <= 25; i++)
        {
            if (i % 8 == 1) t.Engine.BeginTurn("turn " + i); // 8 calls per turn
            Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", "line " + i)).Status);
        }
        var history = History(t);
        Assert.Equal(20, history.Count);
        Assert.Equal("u6", history[0].UtteranceId);
        Assert.Equal("u25", history[19].UtteranceId);
        Assert.Equal(ToolErrorCodes.NothingToReplay, (await Replay(t, "u5")).ErrorCode);
        Assert.Equal(ToolStatuses.Success, (await Replay(t, "u6")).Status);
    }

    [Fact]
    public async Task S37d_the_captions_of_the_request_are_the_current_support_visibility_S91()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "all shown");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowJapanese = false, ShowRomaji = false });
        await SpeakOne(t, "en", "some hidden");
        Assert.Equal(new SpeechCaptions(true, true, true, true), t.Speech.Requests[0].EffectiveCaptions);
        Assert.Equal(new SpeechCaptions(false, true, false, true), t.Speech.Requests[1].EffectiveCaptions);
    }

    // ------------------------------------------------------------ cancellation mid-speech

    [Fact]
    public async Task S27_cancel_generation_in_the_middle_of_speech_cancels_the_speech_and_the_call()
    {
        await using var t = await NewAsync();
        t.Speech.Delay = TimeSpan.FromSeconds(30);
        var call = t.Engine.InvokeToolAsync("speak", EnglishJson("a long sentence"));
        await t.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        t.Engine.CancelGeneration();

        var r = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Equal(ToolErrorCodes.Cancelled, r.ErrorCode);
        Assert.Empty(History(t));
        Assert.Empty(Assistance(t));
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S27_a_speak_after_cancel_generation_is_cancelled_without_reaching_the_port()
    {
        await using var t = await NewAsync();
        t.Engine.CancelGeneration();
        var r = await SpeakOne(t, "en", "hello");
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S89_a_cancel_after_a_segment_was_presented_keeps_the_record_of_what_was_said()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 1);
        var r = await Speak(t, Segment("ja", "りんごです"), Segment("en", "It is an apple."));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        var entry = History(t).Single();
        Assert.Equal(1, entry.SegmentsPresented);
        Assert.Equal(new[] { "りんごです" }, entry.Segments.Select(s => s.Text)); // only what reached the learner
        Assert.Equal("cancelled", entry.Outcome);
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
        Assert.Equal(5, SpokenCharacters(t)); // the presented segment still counts
    }

    [Fact]
    public async Task S89_a_failure_with_segments_presented_is_still_speech_failed_and_reports_how_many_were_presented()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 2, "device lost");
        var r = await Speak(t, Segment("en", "one"), Segment("en", "two"), Segment("en", "three"));
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Equal("u1", DataString(r, "utterance_id"));
        Assert.Equal(2, r.Data!.Value.GetProperty("segments_presented").GetInt32());
        Assert.Equal(2, History(t).Single().SegmentsPresented);
    }

    [Fact]
    public async Task S89_an_utterance_with_nothing_presented_never_enters_history()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "device lost");
        await SpeakOne(t, "en", "one");
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 0);
        await SpeakOne(t, "en", "two");
        Assert.Empty(History(t));
        Assert.Equal(ToolErrorCodes.NothingToReplay, (await Replay(t)).ErrorCode);
    }

    [Fact]
    public async Task S89_a_completed_outcome_counts_every_segment_as_presented_even_without_a_count()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = _ => new SpeechOutcome(SpeechStatuses.Completed, null);
        await Speak(t, Segment("en", "one"), Segment("en", "two"));
        Assert.Equal(2, History(t).Single().SegmentsPresented);
    }

    [Fact]
    public async Task S87_a_reset_during_speech_makes_the_late_call_cancelled_and_changes_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        using var release = new ManualResetEventSlim();
        t.Speech.Outcome = r =>
        {
            release.Wait(TimeSpan.FromSeconds(10)); // a port that ignores cancellation
            return new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        };
        var call = Task.Run(() => t.Engine.InvokeToolAsync("speak", JapaneseJson("りんご")));
        await t.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await t.Engine.ResetProfileAsync();
        release.Set();

        var r = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Empty(History(t));
        Assert.Empty(Assistance(t));
    }

    // ------------------------------------------------------------ S37e: audio model

    [Fact]
    public async Task S37e_a_ja_segment_containing_the_target_japanese_records_audio_model_for_that_target()
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("vocabulary", "apple", "りんご"));
        await Speak(t, Segment("ja", "これはりんごです"));
        var record = Assistance(t, AssistanceKinds.AudioModel).Single();
        Assert.Equal("apple", record.TargetKey);
        Assert.Equal("vocabulary", record.TargetDimension);
        Assert.Equal(task.TaskId, record.TaskId);
        Assert.Equal(t.Core.World.LastEventId, record.EventId);
        Assert.Equal(t.Core.CurrentTurnId, record.TurnId);
    }

    [Fact]
    public async Task S37e_detection_uses_nfkc_so_half_width_katakana_in_the_target_matches_full_width_speech()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "ｱｯﾌﾟﾙ"));
        await Speak(t, Segment("ja", "アップルをください"));
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
    }

    [Fact]
    public async Task S37e_detection_uses_nfkc_so_full_width_forms_in_the_speech_match_the_target()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "アップル"));
        await Speak(t, Segment("ja", "ｱｯﾌﾟﾙです"));
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
    }

    [Fact]
    public async Task S37e_whitespace_is_removed_on_both_sides()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "り んご"));
        await Speak(t, Segment("ja", "これは り ん　ご です"));
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
    }

    [Fact]
    public async Task S37e_a_ja_segment_without_the_target_records_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        await Speak(t, Segment("ja", "これはバナナです"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_an_en_segment_never_records_audio_model()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "ringo"));
        await Speak(t, Segment("en", "ringo means apple"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_the_target_text_in_a_reading_field_does_not_count_only_the_spoken_text()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        await Speak(t, Segment("ja", "バナナ", reading: "りんご"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_a_target_without_japanese_records_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("interaction", "greeting", null));
        await Speak(t, Segment("ja", "こんにちは"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_without_an_active_task_nothing_is_recorded()
    {
        await using var t = await NewAsync();
        await Speak(t, Segment("ja", "りんご"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_only_the_targets_whose_japanese_was_spoken_are_recorded()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"), ("vocabulary", "banana", "バナナ"), ("production", "request", null));
        await Speak(t, Segment("ja", "りんごをどうぞ"));
        var records = Assistance(t, AssistanceKinds.AudioModel);
        Assert.Equal("apple", records.Single().TargetKey);
    }

    [Fact]
    public async Task S37e_two_targets_spoken_in_one_utterance_each_get_a_record()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"), ("vocabulary", "banana", "バナナ"));
        await Speak(t, Segment("ja", "りんごとバナナ"));
        Assert.Equal(new[] { "apple", "banana" }, Assistance(t, AssistanceKinds.AudioModel).Select(r => r.TargetKey));
    }

    [Fact]
    public async Task S89_audio_model_is_recorded_only_when_the_segment_containing_the_target_was_presented()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 1, "stopped");
        await Speak(t, Segment("ja", "バナナ"), Segment("ja", "りんご"));
        Assert.Empty(Assistance(t, AssistanceKinds.AudioModel)); // the target segment never reached the learner

        t.Engine.BeginTurn("again");
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 2, "stopped");
        await Speak(t, Segment("ja", "バナナ"), Segment("ja", "りんご"));
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
    }

    [Fact]
    public async Task S89_audio_model_is_not_recorded_when_nothing_was_presented()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "device lost");
        await Speak(t, Segment("ja", "りんご"));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S37e_an_abandoned_task_no_longer_collects_audio_model()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        lock (t.Core.Sync) t.Core.Tasks.CommitAbandon(t.Core.CurrentTurnId);
        t.Core.FlushNotifications();
        await Speak(t, Segment("ja", "りんご"));
        Assert.Empty(Assistance(t));
    }

    // ------------------------------------------------------------ S22: failures change nothing

    [Fact]
    public async Task S22_a_refused_speak_changes_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        var revision = t.Engine.GetScene().Revision;
        var records = Assistance(t).Count;
        Assert.NotEqual(ToolStatuses.Success, (await SpeakOne(t, "ja", Japanese(61))).Status);
        Assert.NotEqual(ToolStatuses.Success, (await SpeakOne(t, "ja", "ringo")).Status);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
        Assert.Equal(records, Assistance(t).Count);
        Assert.Empty(History(t));
        Assert.Empty(t.Speech.Requests);
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S37d_speak_changes_neither_the_scene_nor_the_profile()
    {
        await using var t = await NewAsync();
        var revision = t.Engine.GetScene().Revision;
        var saves = t.Store.SaveCount;
        var r = await SpeakOne(t, "en", "hello");
        Assert.Equal(revision, r.Revision);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    // ------------------------------------------------------------ S80: hidden level

    [Theory]
    [InlineData("en", "text", "Your working level is high")]
    [InlineData("en", "text", "WORKING LEVEL")]
    [InlineData("en", "text", "That word is N5.")]
    [InlineData("en", "text", "see working_level")]
    [InlineData("ja", "text", "あなたのworking levelはN3です")]
    [InlineData("ja", "translation", "your working level")]
    [InlineData("ja", "reading", "working_level")]
    [InlineData("ja", "romaji", "Working Level")]
    public async Task S80_speech_mentioning_the_working_level_is_refused_with_hidden_level(string lang, string field, string value)
    {
        await using var t = await NewAsync();
        var segment = Segment(lang, lang == "ja" ? "りんご" : "hello");
        segment[field] = value;
        var r = await Speak(t, segment);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("hidden_level", r.Detail);
        Assert.Empty(t.Speech.Requests);
    }

    [Theory]
    [InlineData("This is a beginner word.")]
    [InlineData("Level up! Your level of focus is great.")]
    [InlineData("A working man works at a high level.")]
    public async Task S80_ordinary_speech_stays_allowed(string text)
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolStatuses.Success, (await SpeakOne(t, "en", text)).Status);
    }

    // ------------------------------------------------------------ helpers

    private static string EnglishJson(string text) => SpeakJson(null, Segment("en", text));

    private static string JapaneseJson(string text) => SpeakJson(null, Segment("ja", text));
}
