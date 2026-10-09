namespace IitAcademicPortal.Domain.Auditing;

/// <summary>
/// Durable work record that delivers a security event to the formal audit store without affecting the
/// authentication or authorization decision that produced it.
/// </summary>
/// <remarks>
/// The envelope is already redacted and allowlisted before it is enqueued. An exhausted item is retained
/// for authorized recovery and is never silently discarded.
/// </remarks>
public sealed class SecurityAuditOutboxItem
{
    private SecurityAuditOutboxItem()
    {
    }

    public SecurityAuditOutboxItem(Guid eventId, string envelopeJson, DateTime enqueuedAtUtc)
    {
        EventId = eventId;
        EnvelopeJson = envelopeJson;
        EnqueuedAtUtc = enqueuedAtUtc;
        State = OutboxDeliveryState.Pending;
    }

    /// <summary>The identifier of the eventual formal event; the idempotency key.</summary>
    public Guid EventId { get; private set; }

    public string EnvelopeJson { get; private set; } = string.Empty;

    public DateTime EnqueuedAtUtc { get; private set; }

    public OutboxDeliveryState State { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public DateTime? LastAttemptAtUtc { get; private set; }

    /// <summary>A safe classified cause; never a raw exception payload.</summary>
    public string? LastFailureCode { get; private set; }

    /// <summary>Set while a worker owns the item; an expired lease may be claimed again.</summary>
    public DateTime? LeaseUntilUtc { get; private set; }

    public DateTime? DeliveredAtUtc { get; private set; }

    public DateTime? HandledAtUtc { get; private set; }

    public string? HandledBy { get; private set; }

    public string? HandledReason { get; private set; }
}
