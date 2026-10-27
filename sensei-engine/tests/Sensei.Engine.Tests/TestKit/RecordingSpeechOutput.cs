using Sensei.Engine.Speech;

namespace Sensei.Engine.Tests.TestKit;

/// <summary>Records every speech request; outcome and delay are configurable. Cancellation during the delay returns <c>cancelled</c>.</summary>
public sealed class RecordingSpeechOutput : ISpeechOutput
{
    private readonly List<SpeechRequest> _requests = new();
    private readonly object _sync = new();

    /// <summary>Outcome per request; default <c>completed</c> with every segment presented.</summary>
    public Func<SpeechRequest, SpeechOutcome> Outcome { get; set; } = r => new SpeechOutcome(SpeechStatuses.Completed, null, r.Segments.Count);

    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    /// <summary>Completed when a request starts, for tests that cancel mid-speech.</summary>
    public TaskCompletionSource<SpeechRequest> Started { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<SpeechRequest> Requests
    {
        get { lock (_sync) return _requests.ToList(); }
    }

    public void ResetStarted() => Started = new TaskCompletionSource<SpeechRequest>(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<SpeechOutcome> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        lock (_sync) _requests.Add(request);
        Started.TrySetResult(request);
        if (Delay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(Delay, ct);
            }
            catch (OperationCanceledException)
            {
                return new SpeechOutcome(SpeechStatuses.Cancelled, null, 0);
            }
        }
        if (ct.IsCancellationRequested) return new SpeechOutcome(SpeechStatuses.Cancelled, null, 0);
        return Outcome(request);
    }
}
