using Sensei.Engine.Events;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

/// <summary>show_hint: S38a–S38c, S80, S89.</summary>
public sealed class ShowHintTests
{
    // ------------------------------------------------------------ S38a: content and card

    [Fact]
    public async Task S38a_empty_content_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content());
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Contains("content", r.Detail);
        Assert.Empty(t.Events.Hints);
    }

    [Fact]
    public async Task S38a_missing_content_or_kind_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("show_hint", """{"kind":"explanation"}""")).ErrorCode);
        t.Engine.BeginTurn("n2");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("show_hint", """{"content":{"english":"x"}}""")).ErrorCode);
        t.Engine.BeginTurn("n3");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("show_hint", """{"kind":"explanation","content":null}""")).ErrorCode);
    }

    [Theory]
    [InlineData("japanese", "水")]
    [InlineData("reading", "みず")]
    [InlineData("romaji", "mizu")]
    [InlineData("english", "water")]
    public async Task S38a_each_content_field_alone_is_enough(string field, string value)
    {
        await using var t = await NewAsync();
        var content = new System.Text.Json.Nodes.JsonObject { [field] = value };
        var r = await Hint(t, "vocabulary_card", content);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Single(t.Events.Hints);
    }

    [Theory]
    [InlineData("japanese")]
    [InlineData("reading")]
    [InlineData("romaji")]
    [InlineData("english")]
    public async Task S38a_S123_a_card_whose_only_field_is_empty_is_invalid_arguments(string field)
    {
        await using var t = await NewAsync();
        var content = new System.Text.Json.Nodes.JsonObject { [field] = "" };
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Hint(t, "translation", content)).ErrorCode);
        Assert.Empty(t.Events.Hints);
    }

    [Theory]
    [InlineData("japanese", 60)]
    [InlineData("reading", 60)]
    [InlineData("romaji", 80)]
    [InlineData("english", 160)]
    public async Task S38a_field_lengths_are_accepted_at_the_limit_and_refused_one_over(string field, int limit)
    {
        await using var t = await NewAsync();
        var ok = Content();
        ok[field] = Repeat("a", limit);
        Assert.Equal(ToolStatuses.Success, (await Hint(t, "explanation", ok)).Status);

        var over = Content();
        over[field] = Repeat("a", limit + 1);
        var r = await Hint(t, "explanation", over);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Contains(field, r.Detail);
        Assert.Single(t.Events.Hints);
    }

    [Fact]
    public async Task S38a_field_lengths_count_unicode_scalar_values()
    {
        await using var t = await NewAsync();
        var ok = Content(japanese: Repeat(NonBmpKanji, 60), english: Repeat(NonBmpEmoji, 160)); // 120 and 320 code units
        Assert.Equal(ToolStatuses.Success, (await Hint(t, "model_answer", ok)).Status);
        var over = Content(japanese: Repeat(NonBmpKanji, 61));
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Hint(t, "model_answer", over)).ErrorCode);
    }

    [Fact]
    public async Task S21_an_unknown_kind_or_content_property_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Hint(t, "answer_key", Content(english: "x"))).ErrorCode);
        t.Engine.BeginTurn("n2");
        Assert.Equal(ToolErrorCodes.InvalidArguments,
            (await t.CallAsync("show_hint", """{"kind":"explanation","content":{"english":"x","kanji":"y"}}""")).ErrorCode);
        t.Engine.BeginTurn("n3");
        Assert.Equal(ToolErrorCodes.InvalidArguments,
            (await t.CallAsync("show_hint", """{"kind":"explanation","content":{"english":"x"},"speak":"yes"}""")).ErrorCode);
    }

    [Fact]
    public async Task S38a_the_card_is_raised_through_on_hint_and_returned_in_data()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "partial_phrase", Content("りんご", "りんご", "ringo", "apple"), targetKey: "apple");
        Assert.Equal(ToolStatuses.Success, r.Status);

        var card = t.Events.Hints.Single();
        Assert.Equal("partial_phrase", card.Kind);
        Assert.Equal("りんご", card.Japanese);
        Assert.Equal("りんご", card.Reading);
        Assert.Equal("ringo", card.Romaji);
        Assert.Equal("apple", card.English);
        Assert.Equal("apple", card.TargetKey);

        var data = r.Data!.Value;
        var inData = data.GetProperty("card");
        Assert.Equal("partial_phrase", inData.GetProperty("kind").GetString());
        Assert.Equal("りんご", inData.GetProperty("japanese").GetString());
        Assert.Equal("apple", inData.GetProperty("target_key").GetString());
        Assert.False(data.TryGetProperty("utterance_id", out _));
        Assert.False(data.TryGetProperty("outcome", out _));
    }

    [Fact]
    public async Task S113_omitted_card_fields_are_omitted_from_the_card_in_data()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "translation", Content(english: "water"));
        var card = r.Data!.Value.GetProperty("card");
        Assert.Equal("water", card.GetProperty("english").GetString());
        Assert.False(card.TryGetProperty("japanese", out _));
        Assert.False(card.TryGetProperty("target_key", out _));
    }

    [Fact]
    public async Task S38a_every_kind_is_accepted()
    {
        await using var t = await NewAsync();
        foreach (var kind in new[] { "partial_phrase", "model_answer", "explanation", "vocabulary_card", "translation" })
            Assert.Equal(ToolStatuses.Success, (await Hint(t, kind, Content(english: "x"))).Status);
        Assert.Equal(5, t.Events.Hints.Count);
    }

    [Fact]
    public async Task S38a_a_vocabulary_card_never_creates_or_names_a_scene_object()
    {
        await using var t = await NewAsync();
        var revision = t.Engine.GetScene().Revision;
        var r = await Hint(t, "vocabulary_card", Content("りんご", "りんご", "ringo", "apple"));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Empty(r.AffectedIds);
        Assert.Equal(revision, r.Revision);
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Empty(t.Events.SceneEvents);
    }

    [Fact]
    public async Task S38a_a_card_does_not_move_the_scene_revision_when_objects_exist()
    {
        await using var t = await NewAsync();
        Spawn(t, "apple");
        var before = t.Engine.GetScene();
        await Hint(t, "vocabulary_card", Content("バナナ", english: "banana"));
        var after = t.Engine.GetScene();
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(before.Instances.Count, after.Instances.Count);
    }

    [Fact]
    public async Task S38a_a_hint_changes_nothing_in_the_profile()
    {
        await using var t = await NewAsync();
        var saves = t.Store.SaveCount;
        await Hint(t, "explanation", Content(english: "the particle は marks the topic"));
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S38a_a_handler_that_throws_on_hint_does_not_undo_the_call()
    {
        await using var t = await NewAsync(events: new RecordingEvents { ThrowFrom = "OnHint" });
        StartTask(t, ("vocabulary", "apple", null));
        var r = await Hint(t, "model_answer", Content("りんご"));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Single(Assistance(t, AssistanceKinds.ModelAnswer));
    }

    // ------------------------------------------------------------ S38b: assistance records

    [Theory]
    [InlineData("partial_phrase", AssistanceKinds.PartialPhrase)]
    [InlineData("model_answer", AssistanceKinds.ModelAnswer)]
    [InlineData("explanation", AssistanceKinds.Explanation)]
    [InlineData("vocabulary_card", AssistanceKinds.VocabularyCard)]
    [InlineData("translation", AssistanceKinds.TranslationHint)]
    public async Task S38b_the_kind_is_recorded_for_every_skill_target_of_the_active_task(string kind, string expected)
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("vocabulary", "apple", null), ("production", "request_apple", null));
        await Hint(t, kind, Content(english: "x"));

        var records = Assistance(t);
        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(expected, r.Kind);
            Assert.Equal(task.TaskId, r.TaskId);
            Assert.Equal(t.Core.CurrentTurnId, r.TurnId);
            Assert.Equal(t.Core.World.LastEventId, r.EventId);
        });
        Assert.Equal(new[] { ("vocabulary", "apple"), ("production", "request_apple") }, records.Select(r => (r.TargetDimension!, r.TargetKey)));
    }

    [Fact]
    public async Task S38b_translation_is_recorded_as_translation_hint_not_translation()
    {
        await using var t = await NewAsync();
        StartTask(t, ("reading", "kanji_water", null));
        await Hint(t, "translation", Content(english: "water"));
        Assert.DoesNotContain(Assistance(t), r => r.Kind == "translation");
        Assert.Single(Assistance(t, "translation_hint"));
    }

    [Fact]
    public async Task S38b_a_target_key_records_only_that_key()
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("vocabulary", "apple", null), ("production", "request_apple", null));
        await Hint(t, "partial_phrase", Content("りん"), targetKey: "request_apple");
        var record = Assistance(t).Single();
        Assert.Equal("request_apple", record.TargetKey);
        Assert.Equal("production", record.TargetDimension);
        Assert.Equal(task.TaskId, record.TaskId);
    }

    [Fact]
    public async Task S38b_a_target_key_not_in_the_active_task_is_recorded_by_key_alone_matching_every_dimension()
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("vocabulary", "apple", null));
        await Hint(t, "explanation", Content(english: "x"), targetKey: "particle_wo");
        var record = Assistance(t).Single();
        Assert.Equal("particle_wo", record.TargetKey);
        Assert.Null(record.TargetDimension);
        Assert.Equal(task.TaskId, record.TaskId);
        Assert.True(record.AppliesTo("production", "particle_wo"));
        Assert.True(record.AppliesTo("reading", "particle_wo"));
    }

    [Fact]
    public async Task S38b_with_no_active_task_and_a_target_key_the_key_is_recorded_without_a_task()
    {
        await using var t = await NewAsync();
        await Hint(t, "model_answer", Content("りんごをください"), targetKey: "request_apple");
        var record = Assistance(t).Single();
        Assert.Equal("request_apple", record.TargetKey);
        Assert.Null(record.TaskId);
    }

    [Fact]
    public async Task S38b_with_no_active_task_and_no_target_key_nothing_is_recorded_but_the_hint_is_shown()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "model_answer", Content("りんごをください"));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Single(t.Events.Hints);
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S38b_a_target_key_matching_two_dimensions_of_the_task_records_both()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null), ("production", "apple", null));
        await Hint(t, "vocabulary_card", Content("りんご"), targetKey: "apple");
        Assert.Equal(new[] { "vocabulary", "production" }, Assistance(t).Select(r => r.TargetDimension!));
    }

    [Fact]
    public async Task S22_a_refused_hint_records_nothing_and_raises_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        var r = await Hint(t, "model_answer", Content());
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Empty(Assistance(t));
        Assert.Empty(t.Events.Hints);
    }

    [Fact]
    public async Task S27_a_hint_after_cancel_generation_is_cancelled_and_records_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        t.Engine.CancelGeneration();
        var r = await Hint(t, "model_answer", Content("りんご"));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Empty(Assistance(t));
        Assert.Empty(t.Events.Hints);
    }

    // ------------------------------------------------------------ S38c: speak

    [Fact]
    public async Task S38c_without_speak_nothing_is_spoken()
    {
        await using var t = await NewAsync();
        await Hint(t, "model_answer", Content("りんご", english: "apple"));
        await Hint(t, "model_answer", Content("りんご", english: "apple"), speak: false);
        Assert.Empty(t.Speech.Requests);
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S38c_speak_true_says_the_japanese_as_ja_and_the_english_as_en_in_that_order()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "model_answer", Content("りんごをください", "りんごをください", "ringo o kudasai", "Please give me an apple."), speak: true);
        Assert.Equal(ToolStatuses.Success, r.Status);

        var request = t.Speech.Requests.Single();
        Assert.Equal(2, request.Segments.Count);
        Assert.Equal(new SpeechSegment("ja", "りんごをください", "りんごをください", "ringo o kudasai", null), request.Segments[0]);
        Assert.Equal(new SpeechSegment("en", "Please give me an apple.", null, null, null), request.Segments[1]);
        Assert.Equal("u1", request.UtteranceId);
        Assert.Equal("normal", request.Pace);
    }

    [Fact]
    public async Task S38c_data_carries_the_card_the_utterance_id_and_the_outcome()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "model_answer", Content("りんご"), speak: true);
        var data = r.Data!.Value;
        Assert.Equal("model_answer", data.GetProperty("card").GetProperty("kind").GetString());
        Assert.Equal("u1", data.GetProperty("utterance_id").GetString());
        Assert.Equal("completed", data.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task S38c_only_the_japanese_or_only_the_english_makes_one_segment()
    {
        await using var t = await NewAsync();
        await Hint(t, "model_answer", Content("りんご", "りんご", "ringo"), speak: true);
        await Hint(t, "translation", Content(english: "apple", reading: "りんご"), speak: true);
        Assert.Equal(new[] { "ja" }, t.Speech.Requests[0].Segments.Select(s => s.Lang));
        Assert.Equal(new[] { "en" }, t.Speech.Requests[1].Segments.Select(s => s.Lang));
    }

    [Fact]
    public async Task S38c_speak_true_with_only_reading_or_romaji_has_nothing_to_say_and_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "partial_phrase", Content(reading: "りんご", romaji: "ringo"), speak: true);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Empty(t.Events.Hints);
        Assert.Empty(Assistance(t));
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S38c_the_rules_of_S37b_apply_to_the_spoken_fields()
    {
        await using var t = await NewAsync();
        var romajiAsJapanese = await Hint(t, "model_answer", Content("ringo o kudasai"), speak: true);
        Assert.Equal(ToolErrorCodes.InvalidArguments, romajiAsJapanese.ErrorCode);
        Assert.Equal("split_language_segments", romajiAsJapanese.Detail);

        var kanaAsEnglish = await Hint(t, "model_answer", Content("りんご", english: "say りんご"), speak: true);
        Assert.Equal("split_language_segments", kanaAsEnglish.Detail);

        Assert.Empty(t.Events.Hints);
        Assert.Empty(t.Speech.Requests);
    }

    [Fact]
    public async Task S38c_without_speak_the_fields_are_not_held_to_the_speech_language_rules()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content("ringo", english: "say りんご"));
        Assert.Equal(ToolStatuses.Success, r.Status);
    }

    [Fact]
    public async Task S38c_spoken_hint_characters_count_toward_the_480_of_the_turn()
    {
        await using var t = await NewAsync();
        await Hint(t, "explanation", Content(english: English(160)), speak: true);
        await Hint(t, "explanation", Content(english: English(160)), speak: true);
        await SpeakOne(t, "en", English(100));
        Assert.Equal(420, SpokenCharacters(t));

        var jp = Content(japanese: Japanese(30), english: English(31)); // 61 would pass 480
        var refused = await Hint(t, "explanation", jp, speak: true);
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, refused.ErrorCode);

        var exact = await Hint(t, "explanation", Content(japanese: Japanese(30), english: English(30)), speak: true); // 480
        Assert.Equal(ToolStatuses.Success, exact.Status);
        Assert.Equal(480, SpokenCharacters(t));
        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, (await SpeakOne(t, "en", "a")).ErrorCode);
    }

    [Fact]
    public async Task S37c_a_spoken_hint_over_the_total_is_refused_before_anything_is_shown_or_recorded()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        for (var i = 0; i < 3; i++) await SpeakOne(t, "en", English(160));
        var requests = t.Speech.Requests.Count;

        var r = await Hint(t, "model_answer", Content("りんご"), speak: true);

        Assert.Equal(ToolErrorCodes.SpeechLimitExceeded, r.ErrorCode);
        Assert.Empty(t.Events.Hints);
        Assert.Empty(Assistance(t));
        Assert.Equal(requests, t.Speech.Requests.Count);
    }

    [Fact]
    public async Task S37c_a_hint_without_speak_is_not_limited_by_the_spoken_total()
    {
        await using var t = await NewAsync();
        for (var i = 0; i < 3; i++) await SpeakOne(t, "en", English(160));
        var r = await Hint(t, "explanation", Content(english: English(160)));
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Single(t.Events.Hints);
        Assert.Equal(480, SpokenCharacters(t));
    }

    [Fact]
    public async Task S38c_the_hint_is_shown_before_the_speech_starts()
    {
        var events = new RecordingEvents();
        var speech = new RecordingSpeechOutput();
        var shownWhenSpeechStarted = -1;
        speech.Outcome = r =>
        {
            shownWhenSpeechStarted = events.Hints.Count;
            return new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        };
        await using var t = await NewAsync(speech, events);
        await Hint(t, "model_answer", Content("りんご"), speak: true);
        Assert.Equal(1, shownWhenSpeechStarted);
    }

    [Fact]
    public async Task S38c_a_spoken_hint_gets_the_current_captions_and_pace()
    {
        await using var t = await NewAsync();
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowJapanese = false, Pace = "slow" });
        await Hint(t, "model_answer", Content("りんご"), speak: true);
        var request = t.Speech.Requests.Single();
        Assert.Equal("slow", request.Pace);
        Assert.False(request.EffectiveCaptions.ShowJapanese);
        Assert.True(request.EffectiveCaptions.ShowTranslation);
    }

    [Fact]
    public async Task S38c_the_spoken_hint_is_kept_in_speech_history_and_can_be_replayed()
    {
        await using var t = await NewAsync();
        await Hint(t, "model_answer", Content("りんご"), speak: true);
        Assert.Single(History(t));
        var replay = await Replay(t);
        Assert.Equal(ToolStatuses.Success, replay.Status);
        Assert.Equal("りんご", t.Speech.Requests[1].Segments.Single().Text);
    }

    // ------------------------------------------------------------ S89: validate, show, then speak

    [Fact]
    public async Task S89_a_speech_failure_fails_the_call_but_keeps_the_hint_and_its_assistance()
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("vocabulary", "apple", null));
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "no audio device");

        var r = await Hint(t, "model_answer", Content("りんご"), speak: true);

        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Contains("no audio device", r.Detail);
        Assert.Single(t.Events.Hints); // the card was shown
        var record = Assistance(t).Single();
        Assert.Equal(AssistanceKinds.ModelAnswer, record.Kind);
        Assert.Equal(task.TaskId, record.TaskId);
        Assert.Empty(History(t));
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S89_a_cancelled_speech_keeps_the_hint_and_its_assistance_and_the_call_is_cancelled()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 0);
        var r = await Hint(t, "explanation", Content(english: "x"), speak: true);
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Single(t.Events.Hints);
        Assert.Single(Assistance(t, AssistanceKinds.Explanation));
    }

    [Fact]
    public async Task S27_cancel_generation_during_a_spoken_hint_cancels_the_speech_and_keeps_the_hint_record()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        t.Speech.Delay = TimeSpan.FromSeconds(30);
        var call = t.Engine.InvokeToolAsync("show_hint", HintJson("model_answer", Content("りんご"), speak: true));
        await t.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        t.Engine.CancelGeneration();

        var r = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Single(t.Events.Hints);
        Assert.Single(Assistance(t, AssistanceKinds.ModelAnswer));
        Assert.Empty(History(t));
    }

    [Fact]
    public async Task S89_a_failure_after_the_japanese_segment_was_presented_keeps_that_segment_in_history()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 1, "stopped");
        var r = await Hint(t, "model_answer", Content("りんご", english: "apple"), speak: true);
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        var entry = History(t).Single();
        Assert.Equal(1, entry.SegmentsPresented);
        Assert.Equal(new[] { "りんご" }, entry.Segments.Select(s => s.Text));
        Assert.Equal(3, SpokenCharacters(t)); // only the presented segment counts
    }

    [Fact]
    public async Task S89_the_hint_assistance_is_recorded_once_even_when_the_speech_is_replayed_later()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        await Hint(t, "model_answer", Content("りんご"), speak: true);
        Assert.Single(Assistance(t, AssistanceKinds.ModelAnswer));
        await Replay(t);
        Assert.Single(Assistance(t, AssistanceKinds.ModelAnswer));
    }

    [Fact]
    public async Task S37e_a_spoken_hint_that_says_the_target_japanese_also_records_audio_model()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        await Hint(t, "model_answer", Content("りんごをください"), speak: true);
        Assert.Single(Assistance(t, AssistanceKinds.ModelAnswer));
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));
    }

    [Fact]
    public async Task S37e_a_hint_that_is_only_shown_never_records_audio_model()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        await Hint(t, "model_answer", Content("りんごをください"));
        Assert.Empty(Assistance(t, AssistanceKinds.AudioModel));
    }

    // ------------------------------------------------------------ S80

    [Theory]
    [InlineData("japanese", "あなたのworking levelは")]
    [InlineData("reading", "working_level")]
    [InlineData("romaji", "WORKING LEVEL")]
    [InlineData("english", "Your working level is N3.")]
    public async Task S80_hint_content_mentioning_the_working_level_is_refused_with_hidden_level(string field, string value)
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", null));
        var content = Content(english: "fine");
        content[field] = value;
        var r = await Hint(t, "explanation", content);
        Assert.Equal(ToolErrorCodes.InvalidArguments, r.ErrorCode);
        Assert.Equal("hidden_level", r.Detail);
        Assert.Empty(t.Events.Hints);
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S80_a_hint_may_talk_about_jlpt_levels_in_general()
    {
        await using var t = await NewAsync();
        Assert.Equal(ToolStatuses.Success, (await Hint(t, "explanation", Content(english: "This word is N5 vocabulary."))).Status);
    }

    // ------------------------------------------------------------ the hint card never carries the level

    [Fact]
    public async Task S80_the_hint_card_has_no_working_level_field()
    {
        await using var t = await NewAsync();
        var r = await Hint(t, "explanation", Content(english: "x"));
        Assert.DoesNotContain("level", r.Data!.Value.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(typeof(HintCard).GetProperties(), p => p.Name.Contains("Level", StringComparison.OrdinalIgnoreCase));
    }
}
