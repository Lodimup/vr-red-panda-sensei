namespace Sensei.Engine.Tests.TestKit;

/// <summary>A <see cref="TimeProvider"/> whose time only moves when told.</summary>
public sealed class ManualClock : TimeProvider
{
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset? start = null)
    {
        _now = start ?? new DateTimeOffset(2026, 10, 27, 9, 30, 15, TimeSpan.Zero);
    }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset now) => _now = now;
}
