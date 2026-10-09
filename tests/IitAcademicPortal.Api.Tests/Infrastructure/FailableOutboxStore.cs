using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Wraps the real outbox store so a test can make enqueueing or delivery fail on demand.</summary>
public sealed class FailableOutboxStore(ISecurityAuditOutboxStore inner) : ISecurityAuditOutboxStore
{
    private int deliveryFailures;

    /// <summary>When true, every enqueue fails as if the outbox table were unavailable.</summary>
    public bool FailEnqueue { get; set; }

    /// <summary>Fails the next <paramref name="count"/> deliveries, as if the formal event store were failing.</summary>
    public void FailNextDeliveries(int count) => Interlocked.Exchange(ref deliveryFailures, count);

    public Task EnqueueAsync(Guid eventId, string envelopeJson, DateTime enqueuedAtUtc, CancellationToken cancellationToken) =>
        FailEnqueue
            ? throw new InvalidOperationException("The outbox is unavailable.")
            : inner.EnqueueAsync(eventId, envelopeJson, enqueuedAtUtc, cancellationToken);

    public Task<IReadOnlyList<SecurityAuditOutboxItem>> ClaimDueAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken) =>
        inner.ClaimDueAsync(nowUtc, lease, batchSize, cancellationToken);

    public Task DeliverAsync(SecurityAuditOutboxItem item, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref deliveryFailures) >= 0)
        {
            throw new InvalidOperationException("The formal audit store is unavailable.");
        }

        Interlocked.Exchange(ref deliveryFailures, 0);
        return inner.DeliverAsync(item, nowUtc, cancellationToken);
    }

    public Task RecordFailureAsync(
        Guid eventId, DateTime nowUtc, string failureCode, DateTime? nextAttemptAtUtc, bool exhausted, CancellationToken cancellationToken) =>
        inner.RecordFailureAsync(eventId, nowUtc, failureCode, nextAttemptAtUtc, exhausted, cancellationToken);

    public Task<SecurityAuditOutboxStatus> GetStatusAsync(CancellationToken cancellationToken) => inner.GetStatusAsync(cancellationToken);
}
