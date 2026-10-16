namespace IitAcademicPortal.Domain.Notifications;

/// <summary>
/// One logical delivery execution for a source occurrence, recipient, and channel. Immutable after it is accepted: the runtime
/// database role cannot update or delete it. Mutable delivery state lives in <see cref="NotificationDispatchState"/> and each try
/// is a <see cref="NotificationAttempt"/>.
/// </summary>
/// <remarks>
/// The recipient is a stable value, not a foreign key, so the record survives removal of the account. For security messages
/// (invitation, confirmation, password reset) <see cref="Subject"/> and <see cref="Body"/> are always null and
/// <see cref="ContentWithheld"/> is true: rendered content, links, and proofs are never stored.
/// </remarks>
public sealed class NotificationRecord
{
    public const int MaxOccurrenceIdLength = 200;
    public const int MaxDestinationLength = 320;
    public const int MaxIdempotencyKeyLength = 128;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public NotificationEventType EventType { get; private set; }

    public string OccurrenceId { get; private set; } = string.Empty;

    public string RecipientUserId { get; private set; } = string.Empty;

    /// <summary>The address this execution is fixed to; null when the recipient had none (the record is then Failed).</summary>
    public string? Destination { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public Guid TemplateRevisionId { get; private set; }

    public DateTime RequestedAtUtc { get; private set; }

    public string? Subject { get; private set; }

    public string? Body { get; private set; }

    public bool ContentWithheld { get; private set; }

    /// <summary>The failed record this one re-sends; null for an original execution.</summary>
    public Guid? ParentNotificationId { get; private set; }

    public string? RequestIdempotencyKey { get; private set; }

    public static NotificationRecord Create(
        NotificationEventType eventType,
        string occurrenceId,
        string recipientUserId,
        string? destination,
        Guid templateRevisionId,
        DateTime requestedAtUtc,
        string? subject,
        string? body,
        Guid? parentNotificationId = null,
        string? requestIdempotencyKey = null,
        NotificationChannel channel = NotificationChannel.Email)
    {
        var withheld = eventType.IsSecurity();
        return new NotificationRecord
        {
            EventType = eventType,
            OccurrenceId = occurrenceId,
            RecipientUserId = recipientUserId,
            Destination = destination,
            Channel = channel,
            TemplateRevisionId = templateRevisionId,
            RequestedAtUtc = requestedAtUtc,
            Subject = withheld ? null : subject,
            Body = withheld ? null : body,
            ContentWithheld = withheld,
            ParentNotificationId = parentNotificationId,
            RequestIdempotencyKey = requestIdempotencyKey,
        };
    }
}

/// <summary>
/// The mutable operational state of one notification: status, attempt count, retry time, and worker lease. Workers change only
/// this row; the history API projects the current status from it.
/// </summary>
public sealed class NotificationDispatchState
{
    public const int MaxFailureCodeLength = 64;

    public Guid NotificationId { get; private set; }

    public NotificationStatus Status { get; set; }

    /// <summary>Completed attempts so far.</summary>
    public int AttemptCount { get; set; }

    public DateTime? NextAttemptAtUtc { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    /// <summary>Set just before the provider call. A lease that expires after this is set means acceptance is unknown.</summary>
    public DateTime? SendStartedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public string? FailureCode { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public static NotificationDispatchState Create(Guid notificationId, NotificationStatus status, DateTime nowUtc, string? failureCode = null) =>
        new()
        {
            NotificationId = notificationId,
            Status = status,
            NextAttemptAtUtc = status == NotificationStatus.Pending ? nowUtc : null,
            FailureCode = failureCode,
            UpdatedAtUtc = nowUtc,
        };
}

/// <summary>An append-only fact about one try. Never holds a proof, rendered security content, credentials, or a raw provider error.</summary>
public sealed class NotificationAttempt
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid NotificationId { get; private set; }

    public int AttemptNumber { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime CompletedAtUtc { get; private set; }

    public DeliveryAttemptOutcome Outcome { get; private set; }

    public string? FailureCode { get; private set; }

    /// <summary>When the provider accepted the message, if known.</summary>
    public DateTime? ProviderAcceptedAtUtc { get; private set; }

    public static NotificationAttempt Create(
        Guid notificationId, int attemptNumber, DateTime startedAtUtc, DateTime completedAtUtc,
        DeliveryAttemptOutcome outcome, string? failureCode, DateTime? providerAcceptedAtUtc) =>
        new()
        {
            NotificationId = notificationId,
            AttemptNumber = attemptNumber,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            Outcome = outcome,
            FailureCode = failureCode,
            ProviderAcceptedAtUtc = providerAcceptedAtUtc,
        };
}
