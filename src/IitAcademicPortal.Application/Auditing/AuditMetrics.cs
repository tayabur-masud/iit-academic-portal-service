using System.Diagnostics.Metrics;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Operational counters for the audit path. They carry no event content or secrets. A deployment alerts on
/// the failure counters; the project has no metrics stack, so these are plain .NET metrics.
/// </summary>
public sealed class AuditMetrics : IDisposable
{
    public const string MeterName = "IitAcademicPortal.Audit";

    private readonly Meter meter;
    private readonly Counter<long> anonymousUnauthorized;
    private readonly Counter<long> outboxWriteFailures;
    private readonly Counter<long> retryExhausted;
    private readonly Counter<long> fallbackFailures;
    private readonly Counter<long> eventLossRisk;

    public AuditMetrics()
    {
        // Scoped to this instance so a listener can tell apart the counters of separate hosts in one process.
        meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        anonymousUnauthorized = meter.CreateCounter<long>(
            "audit.anonymous_unauthorized", description: "Anonymous HTTP 401 responses. These are counted, not audited.");
        outboxWriteFailures = meter.CreateCounter<long>(
            "audit.outbox.write_failures", description: "Security events that could not be written to the outbox.");
        retryExhausted = meter.CreateCounter<long>(
            "audit.outbox.retry_exhausted", description: "Outbox items that used every automatic retry and await recovery.");
        fallbackFailures = meter.CreateCounter<long>(
            "audit.fallback.failures", description: "Failures writing to the durable fallback sink.");
        eventLossRisk = meter.CreateCounter<long>(
            "audit.event_loss_risk", description: "Security events at risk of being lost.");
    }

    public void AnonymousUnauthorized() => anonymousUnauthorized.Add(1);

    public void OutboxWriteFailed() => outboxWriteFailures.Add(1);

    public void RetryExhausted() => retryExhausted.Add(1);

    public void FallbackFailed() => fallbackFailures.Add(1);

    public void EventLossRisked() => eventLossRisk.Add(1);

    public void Dispose() => meter.Dispose();
}
