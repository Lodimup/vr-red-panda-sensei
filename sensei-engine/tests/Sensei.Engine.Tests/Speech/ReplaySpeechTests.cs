using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using static Sensei.Engine.Tests.Speech.SpeechKit;

namespace Sensei.Engine.Tests.Speech;

/// <summary>replay_speech: S39, S90, S89.</summary>
public sealed class ReplaySpeechTests
{
    // ------------------------------------------------------------ S39: what is replayed

    [Fact]
    public async Task S39_with_an_empty_history_there_is_nothing_to_replay()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.NothingToReplay, r.ErrorCode);
        Assert.Empty(t.Speech.Requests);
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S39_an_unknown_utterance_id_is_nothing_to_replay()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        var r = await Replay(t, "u99");
        Assert.Equal(ToolErrorCodes.NothingToReplay, r.ErrorCode);
        Assert.Single(t.Speech.Requests);
    }

    [Fact]
    public async Task S39_the_default_is_the_latest_utterance()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "first");
        await SpeakOne(t, "en", "second");
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal("second", t.Speech.Requests[2].Segments.Single().Text);
    }

    [Fact]
    public async Task S39_an_utterance_id_picks_that_utterance()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "first");
        await SpeakOne(t, "en", "second");
        await Replay(t, "u1");
        Assert.Equal("first", t.Speech.Requests[2].Segments.Single().Text);
    }

    [Fact]
    public async Task S39_the_replay_gets_a_new_utterance_id_and_the_whole_segment_list()
    {
        await using var t = await NewAsync();
        await Speak(t, Segment("ja", "りんご", "りんご", "ringo", "apple"), Segment("en", "an apple"));
        var r = await Replay(t);
        Assert.Equal("u2", DataString(r, "utterance_id"));
        Assert.Equal("completed", DataString(r, "outcome"));
        var request = t.Speech.Requests[1];
        Assert.Equal("u2", request.UtteranceId);
        Assert.Equal(t.Speech.Requests[0].Segments, request.Segments);
        long generation;
        lock (t.Core.Sync) generation = t.Core.CurrentTurn!.GenerationId;
        Assert.Equal(generation, request.GenerationId);
        Assert.Empty(r.AffectedIds);
    }

    [Fact]
    public async Task S39_an_utterance_from_an_earlier_turn_can_be_replayed()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "from turn one");
        t.Engine.BeginTurn("say that again");
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal("from turn one", t.Speech.Requests[1].Segments.Single().Text);
    }

    [Fact]
    public async Task S39_the_replay_is_itself_in_history_so_a_second_replay_repeats_the_same_words()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        await Replay(t);
        await Replay(t);
        Assert.Equal(new[] { "u1", "u2", "u3" }, History(t).Select(e => e.UtteranceId));
        Assert.Equal(new[] { false, true, true }, History(t).Select(e => e.IsReplay));
        Assert.All(t.Speech.Requests, r => Assert.Equal("hello", r.Segments.Single().Text));
    }

    [Fact]
    public async Task S39_a_replay_by_id_of_a_replay_works()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        await Replay(t); // u2
        await SpeakOne(t, "en", "other");
        var r = await Replay(t, "u2");
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Equal("hello", t.Speech.Requests[3].Segments.Single().Text);
    }

    [Fact]
    public async Task S39_the_utterance_ids_are_the_next_ones_not_reused()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "a");
        await Replay(t);
        Assert.Equal("u3", DataString(await SpeakOne(t, "en", "b"), "utterance_id"));
    }

    // ------------------------------------------------------------ S90: pace, captions

    [Fact]
    public async Task S90_pace_defaults_to_the_current_support_pace_not_the_original_pace()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello", pace: "slow");
        await Replay(t);
        Assert.Equal("normal", t.Speech.Requests[1].Pace);

        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        await Replay(t);
        Assert.Equal("slow", t.Speech.Requests[2].Pace);
    }

    [Fact]
    public async Task S90_an_explicit_pace_wins()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        await Replay(t, pace: "slow");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { Pace = "slow" });
        await Replay(t, pace: "normal");
        Assert.Equal(new[] { "normal", "slow", "normal" }, t.Speech.Requests.Select(r => r.Pace));
    }

    [Fact]
    public async Task S90_an_unknown_pace_is_invalid_arguments()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await Replay(t, pace: "fast")).ErrorCode);
        Assert.Equal(ToolErrorCodes.InvalidArguments, (await t.CallAsync("replay_speech", """{"voice":"kyoko"}""")).ErrorCode);
        Assert.Single(t.Speech.Requests);
    }

    [Fact]
    public async Task S91_a_replay_carries_the_captions_in_force_now()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        await t.Engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowTranslation = false, ShowRomaji = false });
        await Replay(t);
        Assert.Equal(new SpeechCaptions(true, true, true, true), t.Speech.Requests[0].EffectiveCaptions);
        Assert.Equal(new SpeechCaptions(true, true, false, false), t.Speech.Requests[1].EffectiveCaptions);
    }

    // ------------------------------------------------------------ S39 / S90: repetition

    [Fact]
    public async Task S39_a_replay_records_repetition_for_every_skill_target_of_the_active_task()
    {
        await using var t = await NewAsync();
        var task = StartTask(t, ("listening", "apple", null), ("vocabulary", "banana", null));
        await SpeakOne(t, "en", "listen carefully");
        Assert.Empty(Assistance(t));

        await Replay(t);

        var records = Assistance(t);
        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.Equal(AssistanceKinds.Repetition, r.Kind);
            Assert.Equal(task.TaskId, r.TaskId);
            Assert.Equal(t.Core.CurrentTurnId, r.TurnId);
            Assert.Equal(t.Core.World.LastEventId, r.EventId);
        });
        Assert.Equal(new[] { ("listening", "apple"), ("vocabulary", "banana") }, records.Select(r => (r.TargetDimension!, r.TargetKey)));
    }

    [Fact]
    public async Task S39_every_replay_records_repetition_again()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await SpeakOne(t, "en", "listen");
        await Replay(t);
        await Replay(t);
        Assert.Equal(2, Assistance(t, AssistanceKinds.Repetition).Count);
    }

    [Fact]
    public async Task S39_with_no_active_task_a_replay_records_nothing()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "listen");
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Success, r.Status);
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S90_a_replay_records_repetition_never_audio_model_even_when_it_says_the_target()
    {
        await using var t = await NewAsync();
        StartTask(t, ("vocabulary", "apple", "りんご"));
        t.Speech.Outcome = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);
        await Speak(t, Segment("ja", "りんご")); // audio_model once
        Assert.Single(Assistance(t, AssistanceKinds.AudioModel));

        await Replay(t);

        Assert.Single(Assistance(t, AssistanceKinds.AudioModel)); // still only the original
        Assert.Single(Assistance(t, AssistanceKinds.Repetition));
    }

    [Fact]
    public async Task S39_a_replay_follows_the_task_that_is_active_when_it_is_replayed()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello"); // no task yet
        var task = StartTask(t, ("listening", "apple", null));
        await Replay(t);
        Assert.Equal(task.TaskId, Assistance(t).Single().TaskId);
    }

    // ------------------------------------------------------------ S90 / S89: outcomes

    [Fact]
    public async Task S90_a_failed_replay_with_nothing_presented_is_speech_failed_and_records_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await SpeakOne(t, "en", "hello");
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 0, "no audio device");

        var r = await Replay(t);

        Assert.Equal(ToolStatuses.Failure, r.Status);
        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Contains("no audio device", r.Detail);
        Assert.Empty(Assistance(t));
        Assert.Single(History(t));
    }

    [Fact]
    public async Task S90_a_cancelled_replay_with_nothing_presented_records_nothing()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await SpeakOne(t, "en", "hello");
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 0);

        var r = await Replay(t);

        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Empty(Assistance(t));
        Assert.Single(History(t));
    }

    [Fact]
    public async Task S90_a_failed_replay_with_a_segment_presented_still_records_repetition_and_history()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await Speak(t, Segment("en", "one"), Segment("en", "two"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 1, "stopped");

        var r = await Replay(t);

        Assert.Equal(ToolErrorCodes.SpeechFailed, r.ErrorCode);
        Assert.Single(Assistance(t, AssistanceKinds.Repetition));
        var entry = History(t).Last();
        Assert.True(entry.IsReplay);
        Assert.Equal(1, entry.SegmentsPresented);
        Assert.Equal("u2", entry.UtteranceId);
    }

    [Fact]
    public async Task S90_a_cancelled_replay_with_a_segment_presented_keeps_its_record()
    {
        await using var t = await NewAsync();
        StartTask(t, ("listening", "apple", null));
        await Speak(t, Segment("en", "one"), Segment("en", "two"));
        t.Speech.Outcome = Outcome(SpeechStatuses.Cancelled, 1);

        var r = await Replay(t);

        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Single(Assistance(t, AssistanceKinds.Repetition));
        Assert.Equal(2, History(t).Count);
    }

    [Fact]
    public async Task S89_an_utterance_that_was_only_partly_presented_is_replayed_as_far_as_it_was_heard()
    {
        await using var t = await NewAsync();
        t.Speech.Outcome = Outcome(SpeechStatuses.Failed, 2, "stopped");
        await Speak(t, Segment("en", "one"), Segment("en", "two"), Segment("en", "three"));
        t.Speech.Outcome = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);

        await Replay(t);

        Assert.Equal(new[] { "one", "two" }, t.Speech.Requests[1].Segments.Select(s => s.Text));
    }

    [Fact]
    public async Task S27_cancel_generation_during_a_replay_cancels_the_speech_and_the_call()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        t.Speech.ResetStarted();
        t.Speech.Delay = TimeSpan.FromSeconds(30);
        var call = t.Engine.InvokeToolAsync("replay_speech", "{}");
        await t.Speech.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        t.Engine.CancelGeneration();

        var r = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Single(History(t));
        Assert.Empty(Assistance(t));
    }

    [Fact]
    public async Task S27_a_replay_after_cancel_generation_is_cancelled_without_reaching_the_port()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        t.Engine.CancelGeneration();
        var r = await Replay(t);
        Assert.Equal(ToolStatuses.Cancelled, r.Status);
        Assert.Single(t.Speech.Requests);
    }

    [Fact]
    public async Task S87_a_reset_clears_speech_history_so_there_is_nothing_to_replay()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        await t.Engine.ResetProfileAsync();
        t.Engine.BeginTurn("after reset");
        Assert.Equal(ToolErrorCodes.NothingToReplay, (await Replay(t)).ErrorCode);
    }

    // ------------------------------------------------------------ S22 / S80

    [Fact]
    public async Task S22_a_replay_with_nothing_to_replay_changes_nothing()
    {
        await using var t = await NewAsync();
        var revision = t.Engine.GetScene().Revision;
        var saves = t.Store.SaveCount;
        await Replay(t);
        Assert.Equal(revision, t.Engine.GetScene().Revision);
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Empty(History(t));
        Assert.Equal(0, SpokenCharacters(t));
    }

    [Fact]
    public async Task S39_a_replay_changes_neither_the_scene_nor_the_profile()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        var revision = t.Engine.GetScene().Revision;
        var saves = t.Store.SaveCount;
        var r = await Replay(t);
        Assert.Equal(revision, r.Revision);
        Assert.Equal(saves, t.Store.SaveCount);
    }

    [Fact]
    public async Task S39_a_replay_counts_as_one_tool_call_of_the_budget_of_eight()
    {
        await using var t = await NewAsync();
        await SpeakOne(t, "en", "hello");
        for (var i = 0; i < 7; i++) Assert.Equal(ToolStatuses.Success, (await Replay(t)).Status);
        var ninth = await Replay(t);
        Assert.Equal(ToolErrorCodes.TurnBudgetExhausted, ninth.ErrorCode);
    }
}
