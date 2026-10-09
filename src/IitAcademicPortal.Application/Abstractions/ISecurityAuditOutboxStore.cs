using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>Counts for monitoring the security-event outbox.</summary>
public sealed record SecurityAuditOutboxStatus(int Pending, int RetryScheduled, int Exhausted, DateTime? OldestUndeliveredEnqueuedAtUtc);

/// <summary>
/// The durable security-event outbox. It uses its own short-lived database contexts, so an enqueue is never
/// tangled with the request's pending changes or transaction.
/// </summary>
public interface ISecurityAuditOutboxStore
{
    Task EnqueueAsync(Guid eventId, string envelopeJson, DateTime enqueuedAtUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Claims due items by setting a short lease with a conditional update, so only one worker processes an
    /// item at a time. A crashed worker's lease expires and the item is claimed again.
    /// </summary>
    Task<IReadOnlyList<SecurityAuditOutboxItem>> ClaimDueAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the formal event and marks the item delivered. A replay whose event ID already exists is
    /// acknowledged without creating a second event.
    /// </summary>
    Task DeliverAsync(SecurityAuditOutboxItem item, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt and schedules the next one, or marks the item exhausted.</summary>
    Task RecordFailureAsync(
        Guid eventId, DateTime nowUtc, string failureCode, DateTime? nextAttemptAtUtc, bool exhausted, CancellationToken cancellationToken);

    Task<SecurityAuditOutboxStatus> GetStatusAsync(CancellationToken cancellationToken);
}
