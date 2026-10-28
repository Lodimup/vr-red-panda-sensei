using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Sensei.Engine.Agent;
using Sensei.Engine.Llm;
using Sensei.Engine.Learner;
using Sensei.Engine.Speech;
using Sensei.Engine.Storage;
using Sensei.Engine.Tests.Agent;
using Sensei.Engine.Tests.TestKit;
using Sensei.Engine.Trace;
using static Sensei.Engine.Tests.Integration.ReviewFinal4.ReviewFinal4Tests;

namespace Sensei.Engine.Tests.Integration.ReviewFinal4;

public sealed class ReviewFinal4Controls
{
    [Fact]
    public async Task C01_host_guard_does_catch_a_throwing_clr_property()
    {
        var chat = new ScriptedChatClient()
            .Enqueue((_, _) => Task.FromResult(new ChatResponse(ResultMessage(new ThrowingValue()))))
            .EnqueueText("never reached");
        var trace = new RecordingTraceSink();
        await using var t = await TestEngine.CreateAsync(chat: chat, trace: trace);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        var turn = await t.Engine.RunTurnAsync("two");
        Assert.Equal(TurnStatuses.Failed, turn.Status);
        Assert.Equal(TurnErrorCodes.ProviderError, turn.ErrorCode);
        Assert.Equal("outbound request rejected: not_serializable", turn.ErrorDetail);
        Assert.Equal(1, chat.RequestCount);
        Assert.Single(trace.Entries, e => e.ErrorCode == "outbound_rejected");
    }

    [Fact]
    public async Task C02_real_adapter_serializes_and_sanitizes_a_readable_clr_record()
    {
        const string key = "review4-readable+ZX42";
        using var server = new FakeChatServer().EnqueueText("done");
        using var wire = ChatClientFactory.Create(new LlmSettings("openai_compatible", key, "fake-model", server.BaseUrl));
        using var chat = new SeededWireClient(wire, new ReadableValue(key));
        await using var t = await TestEngine.CreateAsync(chatClient: chat);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("one")).Status);
        Assert.Equal(TurnStatuses.Completed, (await t.Engine.RunTurnAsync("two")).Status);
        var request = Assert.Single(server.Requests);
        Assert.Equal("Bearer " + key, request.Authorization);
        Assert.DoesNotContain(key, request.Body, StringComparison.Ordinal);
        Assert.Contains("[redacted]", request.Body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void C03_element_and_native_node_at_depth_1500_still_work(bool node)
    {
        var raw = new string('[', 1500) + "0" + new string(']', 1500);
        using var doc = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 20_001 });
        object value = node ? JsonNode.Parse(raw, documentOptions: new JsonDocumentOptions { MaxDepth = 20_001 })! : doc.RootElement.Clone();
        var clean = JsonRedaction.SanitizedValue(value, Secrets.None, out var oversized);
        Assert.False(oversized);
        Assert.Equal(raw, Assert.IsType<JsonElement>(clean).GetRawText());
        var messages = new List<ChatMessage> { ResultMessage(value) };
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        Assert.Same(messages, guard.Guarded(messages));
    }

    [Fact]
    public void C04_shallow_clr_backed_json_values_are_kept_and_overflow_is_traced()
    {
        var small = WrappedNode(32);
        Assert.IsType<JsonElement>(JsonRedaction.SanitizedValue(small, Secrets.None, out var oversized));
        Assert.False(oversized);
        using var guard = new SerializedRequestGuard(new ScriptedChatClient(), Secrets.None);
        var messages = new List<ChatMessage> { ResultMessage(small) };
        Assert.Same(messages, guard.Guarded(messages));
        var notes = new List<string>();
        var sent = OutboundMessages.Sanitized(new[] { ResultMessage(WrappedNode(1, 10_000)) }, Secrets.None, notes);
        Assert.Equal("[oversized value]", sent.Single().Contents.OfType<FunctionResultContent>().Single().Result);
        Assert.Single(notes);
    }

    [Theory]
    [InlineData(false, "null", 20_000, false)]
    [InlineData(true, "null", 20_000, false)]
    [InlineData(false, "empty", 19_999, false)]
    [InlineData(true, "empty", 19_999, false)]
    [InlineData(false, "nonempty", 20_000, true)]
    [InlineData(true, "nonempty", 20_000, true)]
    public async Task C05_neighbouring_text_budget_cases(bool streaming, string instructions, int texts, bool reject)
    {
        var inner = new ScriptedChatClient().EnqueueText("done");
        using var guard = new SerializedRequestGuard(inner, Secrets.None);
        var options = new ChatOptions { Instructions = instructions switch { "null" => null, "empty" => "", _ => "Be kind." } };
        var error = await Record.ExceptionAsync(() => InvokeAsync(guard, TextMessages(texts), options, streaming));
        if (reject)
        {
            Assert.Equal("too_many_values", Assert.IsType<OutboundRejectedException>(error).Reason);
            Assert.Equal(0, inner.RequestCount);
        }
        else
        {
            Assert.Null(error);
            Assert.Equal(1, inner.RequestCount);
        }
    }

    public sealed record ReadableValue(string Text);

    [Fact]
    public async Task C06_file_profile_gameplay_correction_restart_and_reset()
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scratchpad", "sensei-review4-" + Guid.NewGuid().ToString("N"));
        var store = new FileProfileStore(directory);
        var events = new RecordingEvents();
        var options = new SenseiEngineOptions { ChatClient = null, Speech = new RecordingSpeechOutput(), ProfileStore = store, Events = events };
        try
        {
            await using (var engine = await SenseiEngine.CreateAsync(options))
            {
                await engine.SetStartingBandAsync("early_beginner");
                await engine.SetPreferenceByPlayerAsync(new SupportPatch { ShowRomaji = false });
                engine.BeginTurn("cup");
                Assert.Equal("success", (await engine.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}")).Status);
                engine.MarkRoundStart();
                Assert.Equal("success", (await engine.InvokeToolAsync("start_challenge", """
                    {"family":"discover_request","instruction":{"english":"Select the cup"},
                     "skill_targets":[{"dimension":"production","key":"cup"}],
                     "predicates":[{"type":"selected","object_id":"cup_1"}]}
                    """)).Status);
                Assert.True(engine.PlayerSelect("cup_1").Ok);
                Assert.Null(engine.GetActiveTask());
                var selected = events.SceneEvents.Last(e => e.Kind == "selected");
                var attempt = await engine.InvokeToolAsync("record_attempt", """
                    {"target":{"dimension":"production","key":"cup"},"transcript_excerpt":"cup",
                     "communicative_success":true,"language_assessment":"correct","event_ids":[
                    """ + selected.EventId + "]}");
                Assert.Equal("success", attempt.Status);
                Assert.Equal("success", (await engine.InvokeToolAsync("remember_note", "{\"key\":\"favorite\",\"value\":\"tea\"}")).Status);
                Assert.Equal("success", (await engine.InvokeToolAsync("end_activity", "{\"next_suggestion\":\"Try the bag\"}")).Status);
                var persisted = (await store.LoadAsync("default", default))!;
                var evidence = Assert.Single(persisted.Evidence);
                Assert.Equal("task_completed", evidence.EngineOutcome);
                Assert.Equal(new[] { "player" }, evidence.Actors);
                Assert.False(evidence.Superseded);
                Assert.Equal("Try the bag", engine.GetSessionSummary().NextSuggestion);
                var corrected = await engine.CorrectLastTranscriptAsync("tea");
                Assert.Equal(TurnErrorCodes.LlmNotConfigured, corrected.ErrorCode);
                Assert.True(Assert.Single((await store.LoadAsync("default", default))!.Evidence).Superseded);
            }
            await using var restarted = await SenseiEngine.CreateAsync(options);
            Assert.Empty(restarted.GetScene().Instances);
            Assert.Null(restarted.GetActiveTask());
            Assert.Equal("early_beginner", restarted.GetProfile().StartingBand);
            Assert.Contains("show_romaji", restarted.GetProfile().Pinned);
            Assert.False(restarted.GetProfile().Support.ShowRomaji);
            Assert.Equal("tea", restarted.GetProfile().Notes["favorite"]);
            Assert.True(Assert.Single(restarted.GetProfile().Evidence).Superseded);
            restarted.BeginTurn("place a cup");
            Assert.Equal("success", (await restarted.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}")).Status);
            var before = restarted.GetScene().Revision;
            await restarted.ResetProfileAsync();
            Assert.Empty(restarted.GetScene().Instances);
            Assert.True(restarted.GetScene().Revision > before);
            Assert.Empty((await store.LoadAsync("default", default))!.Evidence);
            restarted.BeginTurn("place another cup");
            Assert.Equal("success", (await restarted.InvokeToolAsync("spawn_objects", "{\"objects\":[{\"catalog_id\":\"cup\"}]}")).Status);
            Assert.Equal("cup_2", Assert.Single(restarted.GetScene().Instances).Id);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }
}
