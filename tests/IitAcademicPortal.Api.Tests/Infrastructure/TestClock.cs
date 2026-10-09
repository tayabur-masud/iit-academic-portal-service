namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>
/// Follows real time plus an offset the test can advance, so retry backoff and lease expiry can be tested
/// without waiting and without freezing time for the rest of the application.
/// </summary>
public sealed class TestClock : TimeProvider
{
    private long offsetTicks;

    public void Advance(TimeSpan by) => Interlocked.Add(ref offsetTicks, by.Ticks);

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks));
}
