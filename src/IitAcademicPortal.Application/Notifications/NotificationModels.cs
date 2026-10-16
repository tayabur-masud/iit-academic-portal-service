using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// What a producer asks for: a business event, the stable source occurrence, and the intended recipients by stable user ID, with
/// safe context for the message. It names no channel, carries no feedback submission data, credential, or proof, and an owner may
/// replay the same request after a failure: the occurrence, recipient, and channel make it idempotent.
/// </summary>
public sealed record NotificationEventRequest(
    NotificationEventType EventType,
    string OccurrenceId,
    IReadOnlyList<string> RecipientUserIds,
    IReadOnlyDictionary<string, string>? Context = null);

public enum NotificationRequestStatus
{
    /// <summary>The request was processed; see the counts for each recipient outcome.</summary>
    Accepted,

    /// <summary>The request itself is malformed or uses unsupported context. Nothing was stored.</summary>
    Invalid,

    /// <summary>The work could not be retained. A secret-free failure signal was raised; the owner can replay the occurrence.</summary>
    PersistenceFailed,
}

public sealed record NotificationRequestResult(
    NotificationRequestStatus Status,
    int Accepted = 0,
    int Duplicates = 0,
    int Suppressed = 0,
    int Failed = 0,
    string? Reason = null)
{
    public static NotificationRequestResult Invalid(string reason) => new(NotificationRequestStatus.Invalid, Reason: reason);

    public static NotificationRequestResult PersistenceFailed { get; } = new(NotificationRequestStatus.PersistenceFailed);
}

public sealed record TemplateSnapshot(
    Guid TemplateId,
    NotificationEventType EventType,
    NotificationChannel Channel,
    bool Enabled,
    Guid RevisionId,
    int RevisionNumber,
    string Subject,
    string Body,
    string Placeholders);

public sealed record TemplateWithRevision(NotificationTemplate Template, NotificationTemplateRevision Revision);

/// <summary>One notification claimed for delivery, with the pinned wording the worker needs.</summary>
public sealed record ClaimedNotification(
    Guid Id,
    NotificationEventType EventType,
    NotificationChannel Channel,
    string RecipientUserId,
    string? Destination,
    string? Subject,
    string? Body,
    Guid TemplateRevisionId,
    string RevisionSubject,
    string RevisionBody,
    int AttemptCount);

public sealed record ClaimResult(IReadOnlyList<ClaimedNotification> ToSend, int Interrupted);

public sealed record AttemptCompletion(
    Guid NotificationId,
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    DeliveryAttemptOutcome Outcome,
    string? FailureCode,
    DateTime? ProviderAcceptedAtUtc,
    NotificationStatus NewStatus,
    DateTime? NextAttemptAtUtc,
    string? StatusFailureCode = null);

public sealed record RecoveryRequestClaim(Guid Id, string ProtectedAddress, int AttemptCount);

public sealed record ReminderPolicySnapshot(int LeadTimeMinutes, int Revision);

public sealed record NotificationSearch(
    string? Recipient,
    NotificationEventType? EventType,
    NotificationStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int Take,
    DateTime? AfterRequestedAtUtc,
    Guid? AfterId);

/// <summary>A history row. Never contains message content.</summary>
public sealed record NotificationSummary(
    Guid Id,
    NotificationEventType EventType,
    string RecipientUserId,
    string? Destination,
    NotificationChannel Channel,
    NotificationStatus Status,
    DateTime RequestedAtUtc,
    DateTime? SentAtUtc,
    bool ContentWithheld,
    Guid? ParentNotificationId,
    string? FailureCode);

public sealed record AttemptView(
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    DeliveryAttemptOutcome Outcome,
    string? FailureCode,
    DateTime? ProviderAcceptedAtUtc);

/// <summary>
/// A history record with its attempts. <see cref="Subject"/> and <see cref="Body"/> are present only for ordinary messages;
/// security messages never store them, so they are null and <see cref="NotificationSummary.ContentWithheld"/> is true.
/// </summary>
public sealed record NotificationDetail(
    NotificationSummary Summary,
    string OccurrenceId,
    int TemplateRevisionNumber,
    string? Subject,
    string? Body,
    IReadOnlyList<AttemptView> Attempts);

public sealed record NotificationPage(IReadOnlyList<NotificationSummary> Items, string? NextCursor);

public enum NotificationOutcome
{
    Ok,
    Accepted,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of an administration operation: a value, field-keyed errors, or a conflict with a stable reason code.</summary>
public sealed record NotificationResult<T>(
    NotificationOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Code = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is NotificationOutcome.Ok or NotificationOutcome.Accepted;

    public static NotificationResult<T> NotFound { get; } = new(NotificationOutcome.NotFound);

    public static NotificationResult<T> Invalid(NotificationErrors errors) => new(NotificationOutcome.Invalid, Errors: errors.ToDictionary());

    public static NotificationResult<T> Conflict(string code, string message) => new(NotificationOutcome.Conflict, Code: code, Message: message);
}

/// <summary>Field-keyed validation messages, collected before anything is written.</summary>
public sealed class NotificationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool Any => errors.Count > 0;

    public bool Has(string field) => errors.ContainsKey(field);

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() => errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}
