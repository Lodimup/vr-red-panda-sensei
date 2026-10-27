using System.Text.Json.Nodes;
using Sensei.Engine.Learner;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Tools;
using Xunit.Abstractions;
using static Sensei.Engine.Tests.Learner.LearnerKit;
using static Sensei.Engine.Tests.Foundation.SceneHelpers;

namespace Sensei.Engine.Tests.Learner.ReviewFixes;

public sealed class ReviewS2FindingsTests
{
    private readonly ITestOutputHelper _output;
    public ReviewS2FindingsTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData("key", "sk-demo123456789012")]
    [InlineData("japanese", "sk-demo123456789012")]
    [InlineData("key", "private-demo-value")]
    [InlineData("japanese", "private-demo-value")]
    public async Task F1_RecordAttemptMustRejectSecretsInPersistedTargetText(string field, string secret)
    {
        await using var t = await TestEngine.CreateAsync(knownSecrets: new[] { "private-demo-value" });
        t.Engine.BeginTurn("りんごをください");
        var args = JsonNode.Parse(AttemptJson("production", "ringo", "りんご"))!.AsObject();
        args["target"]![field] = secret;
        var result = await t.CallAsync("record_attempt", args.ToJsonString());
        _output.WriteLine($"status={result.Status}; stored_target_contains_secret={t.Store.RawJson("default")?.Contains(secret, StringComparison.Ordinal)}");
        // S77: reject persisted learner text; S58: profile never contains an API key.
        Assert.Equal(ToolErrorCodes.LooksLikeCredential, result.ErrorCode);
        Assert.Empty(t.Store.Peek("default")!.Evidence);
    }

    [Fact]
    public async Task F2_SecondManualLevelChangeMustResetEvidenceWindowAtSameClockTime()
    {
        await using var t = await TestEngine.CreateAsync();
        await t.Engine.SetStartingBandAsync("early_beginner");
        for (var i = 0; i < 4; i++)
            Assert.True((await SayAndAttempt(t, "production", "before" + i, "りんご", fresh: true)).IsSuccess);
        await t.Engine.SetStartingBandAsync("intermediate");
        Assert.Equal("n3", t.Engine.GetProfile().WorkingLevel);
        for (var i = 0; i < 4; i++)
            Assert.True((await SayAndAttempt(t, "production", "after" + i, "りんご", fresh: true)).IsSuccess);
        _output.WriteLine($"actual_level={t.Engine.GetProfile().WorkingLevel}; reasons={string.Join(",", t.Engine.GetProfile().LevelHistory.Select(h => h.Reason))}; latest_evidence_ids={string.Join(",", t.Engine.GetProfile().LevelHistory[^1].EvidenceIds ?? new List<string>())}");
        // S51/S102: only four attempts since the actual n5 -> n3 level change, not eight.
        Assert.Equal("n3", t.Engine.GetProfile().WorkingLevel);
        Assert.DoesNotContain(t.Engine.GetProfile().LevelHistory, h => h.Reason == "evidence_up");
    }

    [Fact]
    public async Task F3_CorrectingNewSessionMustNotSupersedePreviousSessionUtteranceWithSameLocalId()
    {
        var store = new InMemoryProfileStore();
        string oldAttemptId;
        string oldSession;
        await using (var first = await TestEngine.CreateAsync(store: store))
        {
            Assert.True((await SayAndAttempt(first, "production", "ringo", "りんご")).IsSuccess);
            var evidence = Assert.Single(Profile(first).Evidence);
            oldAttemptId = evidence.AttemptId;
            oldSession = evidence.SessionId;
            Assert.Equal("l1", evidence.SourceUtteranceId);
        }
        await using var second = await TestEngine.CreateAsync(store: store);
        Assert.True((await SayAndAttempt(second, "production", "hon", "本")).IsSuccess);
        Assert.NotEqual(oldSession, Profile(second).Evidence[^1].SessionId);
        await second.Engine.CorrectLastTranscriptAsync("ほん");
        // S99/S100: correction invalidates this utterance only, preserving unrelated historical evidence.
        var historical = Profile(second).Evidence.Single(e => e.AttemptId == oldAttemptId);
        _output.WriteLine($"old_attempt={oldAttemptId}; old_source={historical.SourceUtteranceId}; historical_superseded_in_store={store.Peek("default")!.Evidence.Single(e => e.AttemptId == oldAttemptId).Superseded}");
        Assert.False(historical.Superseded);
        Assert.False(store.Peek("default")!.Evidence.Single(e => e.AttemptId == oldAttemptId).Superseded);
    }

    [Fact]
    public async Task F4_DuplicateAttemptWithNewCredentialNoteMustReturnOriginalWithoutWriting()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんご");
        var first = await Attempt(t, "production", "ringo", "りんご");
        Assert.True(first.IsSuccess);
        var saves = t.Store.SaveCount;
        var retry = await Attempt(t, "production", "ringo", "りんご", note: "sk-demo123456789012");
        _output.WriteLine($"retry_status={retry.Status}; error={retry.ErrorCode}; save_count_delta={t.Store.SaveCount - saves}");
        // S47: this is a schema-valid second call for the same triple; the new note is never persisted.
        Assert.True(retry.IsSuccess, retry.ErrorCode + ": " + retry.Detail);
        Assert.True(DataOf(retry).GetProperty("duplicate").GetBoolean());
        Assert.Equal(Str(first, "attempt_id"), Str(retry, "attempt_id"));
        Assert.Equal(saves, t.Store.SaveCount);
        Assert.Single(Profile(t).Evidence);
    }

    [Fact]
    public async Task F5_ReviewQueueMustPreserveDimensionKeyTargetIdentity()
    {
        await using var t = await TestEngine.CreateAsync();
        t.Engine.BeginTurn("りんご");
        Assert.True((await Attempt(t, "production", "ringo", "りんご", success: false)).IsSuccess);
        Assert.True((await Attempt(t, "vocabulary", "ringo", "りんご", success: false)).IsSuccess);
        var result = await t.CallAsync("query_learner", "{}");
        var data = DataOf(result);
        Assert.Equal(2, data.GetProperty("targets").GetArrayLength());
        var due = data.GetProperty("review_due");
        _output.WriteLine($"targets={data.GetProperty("targets").GetRawText()}; review_due={due.GetRawText()}");
        // S97 explicitly extends (dimension,key) identity to the review queue: both targets are independently due.
        Assert.Equal(2, due.GetArrayLength());
        Assert.All(due.EnumerateArray(), row => Assert.Equal(System.Text.Json.JsonValueKind.Object, row.ValueKind));
        Assert.Equal(new[] { "production", "vocabulary" }, due.EnumerateArray().Select(row => row.GetProperty("dimension").GetString()));
        Assert.All(due.EnumerateArray(), row => Assert.Equal("ringo", row.GetProperty("key").GetString()));
    }
}
