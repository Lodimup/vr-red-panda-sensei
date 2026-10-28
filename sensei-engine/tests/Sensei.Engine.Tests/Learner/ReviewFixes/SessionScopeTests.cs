using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Learner.LearnerKit;

namespace Sensei.Engine.Tests.Learner.ReviewFixes;

/// <summary>Utterance ids (<c>l1</c>, <c>l2</c>, ...) restart in every engine session, so evidence lookups by utterance id are scoped to the session.</summary>
public sealed class SessionScopeTests
{
    [Fact]
    public async Task S93_an_earlier_sessions_attempt_with_the_same_local_utterance_id_does_not_move_the_aid_boundary()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            first.Engine.BeginTurn("こんにちは");
            // this attempt's source is the session's second utterance, "l2"
            var said = await SayAndAttempt(first, "production", "ringo", "りんご");
            Assert.True(said.IsSuccess, said.Detail);
            Assert.Equal("l2", Str(said, "source_utterance_id"));
        }

        await using var second = await TestEngine.CreateAsync(store: store);
        second.Engine.BeginTurn("ありがとう");
        Assist(second, AssistanceKinds.ModelAnswer, "production", "ringo");
        second.Engine.BeginTurn("りんご");
        var r = await Attempt(second, "production", "ringo", "りんご");
        Assert.True(r.IsSuccess, r.Detail);
        Assert.Equal("l2", Str(r, "source_utterance_id"));
        // The model answer was given after the previous learner utterance of this session: it counts. The earlier session's
        // attempt (source "l2" there) must not be resolved against this session's "l2" and hide it.
        Assert.Equal(new[] { AssistanceKinds.ModelAnswer }, StrArray(DataOf(r).GetProperty("assistance")));
        Assert.Equal(AttemptOutcomes.SuccessAfterModel, Str(r, "outcome"));
    }

    [Fact]
    public async Task S99_a_correction_supersedes_only_the_attempts_of_the_current_session_sourced_from_that_utterance()
    {
        var store = new InMemoryProfileStore();
        await using (var first = await TestEngine.CreateAsync(store: store))
            Assert.True((await SayAndAttempt(first, "production", "ringo", "りんご")).IsSuccess);
        await using var second = await TestEngine.CreateAsync(store: store);
        Assert.True((await SayAndAttempt(second, "production", "mizu", "みず")).IsSuccess);
        Assert.True((await SayAndAttempt(second, "production", "hon", "ほん")).IsSuccess);
        await second.Engine.CorrectLastTranscriptAsync("ほんを");
        var evidence = Profile(second).Evidence;
        Assert.Equal(new[] { false, false, true }, evidence.Select(e => e.Superseded));
        Assert.Equal(new[] { false, false, true }, store.Peek("default")!.Evidence.Select(e => e.Superseded));
    }
}
