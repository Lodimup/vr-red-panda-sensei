using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Sockets;
using Sensei.Engine.Agent;
using Sensei.Engine.Tests.TestKit;
using static Sensei.Engine.Tests.Agent.AgentTestSupport;

namespace Sensei.Engine.Tests.Agent;

/// <summary>A pipeline response that only carries a status, to build the exception the OpenAI client throws.</summary>
internal sealed class FakePipelineResponse : PipelineResponse
{
    private readonly int _status;

    public FakePipelineResponse(int status)
    {
        _status = status;
    }

    public override int Status => _status;
    public override string ReasonPhrase => "reason";
    public override Stream? ContentStream { get; set; }
    protected override PipelineResponseHeaders HeadersCore { get; } = new EmptyHeaders();
    public override BinaryData Content => BinaryData.FromString("{}");
    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
    public override void Dispose()
    {
    }

    private sealed class EmptyHeaders : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, out string? value)
        {
            value = null;
            return false;
        }

        public override bool TryGetValues(string name, out IEnumerable<string>? values)
        {
            values = null;
            return false;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => Enumerable.Empty<KeyValuePair<string, string>>().GetEnumerator();
    }
}

public sealed class AgentErrorTests
{
    private static ClientResultException Http(int status) => new($"HTTP {status} from the provider", new FakePipelineResponse(status));

    private static async Task<TurnResult> FailWith(Exception error)
    {
        var chat = new ScriptedChatClient().EnqueueError(error);
        await using var t = await EngineAsync(chat);
        return await t.Engine.RunTurnAsync("hello");
    }

    // ---------------------------------------------------------------- S67 mapping by HTTP status

    [Theory]
    [InlineData(401, "auth_failed")]
    [InlineData(403, "auth_failed")]
    [InlineData(402, "quota_exceeded")]
    [InlineData(429, "quota_exceeded")]
    [InlineData(400, "provider_error")]
    [InlineData(404, "provider_error")]
    [InlineData(408, "provider_error")]
    [InlineData(500, "provider_error")]
    [InlineData(502, "provider_error")]
    [InlineData(503, "provider_error")]
    public async Task S67_http_status_of_the_provider_error_decides_the_error_code(int status, string expected)
    {
        var result = await FailWith(Http(status));
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal(expected, result.ErrorCode);
        Assert.Contains($"HTTP {status}", result.ErrorDetail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "auth_failed")]
    [InlineData(HttpStatusCode.Forbidden, "auth_failed")]
    [InlineData(HttpStatusCode.PaymentRequired, "quota_exceeded")]
    [InlineData(HttpStatusCode.TooManyRequests, "quota_exceeded")]
    [InlineData(HttpStatusCode.InternalServerError, "provider_error")]
    public async Task S67_a_client_result_exception_built_from_a_response_with_a_status_maps_the_same_way(HttpStatusCode status, string expected)
    {
        var result = await FailWith(new ClientResultException("failed", new FakePipelineResponse((int)status)));
        Assert.Equal(expected, result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task S132_an_http_request_exception_is_a_network_error_whatever_status_it_carries(HttpStatusCode status)
    {
        var result = await FailWith(new HttpRequestException("failed", null, status));
        Assert.Equal("network_error", result.ErrorCode);
        Assert.DoesNotContain("HTTP ", result.ErrorDetail);
    }

    [Fact]
    public async Task S67_the_status_inside_an_inner_exception_is_found()
    {
        var result = await FailWith(new InvalidOperationException("wrapper", Http(401)));
        Assert.Equal("auth_failed", result.ErrorCode);
    }

    [Fact]
    public async Task S67_a_status_inside_an_aggregate_exception_is_found()
    {
        var result = await FailWith(new AggregateException(Http(429)));
        Assert.Equal("quota_exceeded", result.ErrorCode);
    }

    // ---------------------------------------------------------------- S67 connection failures and timeouts

    [Fact]
    public async Task S67_a_connection_failure_is_a_network_error()
    {
        var result = await FailWith(new HttpRequestException("Connection refused"));
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_a_socket_error_is_a_network_error()
    {
        var result = await FailWith(new SocketException((int)SocketError.ConnectionReset));
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_an_io_error_is_a_network_error()
    {
        var result = await FailWith(new IOException("stream closed"));
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_a_timeout_is_a_network_error()
    {
        var result = await FailWith(new TimeoutException("took too long"));
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_a_request_that_timed_out_without_being_cancelled_by_us_is_a_network_error()
    {
        var result = await FailWith(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout", new TimeoutException()));
        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_a_client_result_exception_without_a_status_is_a_network_error()
    {
        var result = await FailWith(new ClientResultException("Service request failed.", null, new HttpRequestException("no route")));
        Assert.Equal("network_error", result.ErrorCode);
    }

    [Fact]
    public async Task S67_any_other_provider_failure_is_a_provider_error()
    {
        var result = await FailWith(new InvalidOperationException("the model returned nonsense"));
        Assert.Equal("provider_error", result.ErrorCode);
        Assert.Contains("nonsense", result.ErrorDetail);
    }

    // ---------------------------------------------------------------- S67 failed requests change nothing

    [Fact]
    public async Task S67_a_failed_request_leaves_the_scene_the_task_and_the_profile_unchanged()
    {
        var chat = new ScriptedChatClient().EnqueueError(Http(500));
        await using var t = await EngineAsync(chat);
        var scene = t.Engine.GetScene();
        var profile = t.Engine.GetProfile();

        var result = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal(scene.Revision, t.Engine.GetScene().Revision);
        Assert.Empty(t.Engine.GetScene().Instances);
        Assert.Null(t.Engine.GetActiveTask());
        Assert.Equal(profile.Evidence.Count, t.Engine.GetProfile().Evidence.Count);
        Assert.Equal(profile.WorkingLevel, t.Engine.GetProfile().WorkingLevel);
        Assert.Empty(result.ToolCalls);
        Assert.Empty(result.Spoken);
    }

    [Fact]
    public async Task S67_the_failed_turn_keeps_what_tools_already_did_before_the_failure()
    {
        var chat = new ScriptedChatClient()
            .EnqueueFunctionCalls(("c1", "test_probe", ProbeJson("spawn")))
            .EnqueueError(Http(503));
        await using var t = await EngineAsync(chat);

        var result = await t.Engine.RunTurnAsync("hello");

        Assert.Equal(TurnStatuses.Failed, result.Status);
        Assert.Equal("provider_error", result.ErrorCode);
        Assert.Equal(2, result.Rounds);
        Assert.Single(result.ToolCalls);
        Assert.Single(t.Engine.GetScene().Instances);
    }

    [Fact]
    public async Task S67_the_next_turn_after_a_failure_works()
    {
        var chat = new ScriptedChatClient().EnqueueError(Http(500)).EnqueueText("back");
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("one");
        var result = await t.Engine.RunTurnAsync("two");
        Assert.Equal(TurnStatuses.Completed, result.Status);
        Assert.Equal("back", result.UnspokenText);
    }

    [Fact]
    public async Task S117_a_failed_turn_still_leaves_the_learner_utterance()
    {
        var chat = new ScriptedChatClient().EnqueueError(Http(401));
        await using var t = await EngineAsync(chat);
        await t.Engine.RunTurnAsync("remember me");
        lock (t.Core.Sync) Assert.Equal("remember me", Assert.Single(t.Core.Utterances.All).Text);
    }

    // ---------------------------------------------------------------- unit: the mapper itself

    [Fact]
    public void S67_find_status_reads_client_result_exceptions()
    {
        Assert.Equal(429, ProviderErrors.FindStatus(Http(429)));
        Assert.Null(ProviderErrors.FindStatus(new InvalidOperationException("x")));
    }

    [Fact]
    public void S67_detail_is_one_short_line()
    {
        var (_, detail) = ProviderErrors.Map(new InvalidOperationException("line one\nline two " + new string('x', 1000)), Sensei.Engine.Trace.Secrets.None);
        Assert.DoesNotContain('\n', detail);
        Assert.True(detail.Length <= 300);
    }
}
