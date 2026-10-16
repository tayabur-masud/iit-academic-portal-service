using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Abstractions;

public enum AcceptResult
{
    Inserted,

    /// <summary>The source occurrence, recipient, and channel already have an original notification.</summary>
    Duplicate,
}

/// <summary>
/// Notification work for producers and the background workers. Every operation uses its own short-lived unit of work, so it never
/// shares a transaction or pending changes with the request that raised the event, and a failure here cannot undo that action.
/// </summary>
public interface INotificationStore
{
    /// <summary>The current revision of the event's template on every channel that has one.</summary>
    Task<IReadOnlyList<TemplateSnapshot>> GetTemplatesAsync(NotificationEventType eventType, CancellationToken cancellationToken);

    /// <summary>Stores a record and its dispatch state together. A replay of the same source occurrence is a <see cref="AcceptResult.Duplicate"/>.</summary>
    Task<AcceptResult> AcceptAsync(NotificationRecord record, NotificationDispatchState state, CancellationToken cancellationToken);

    /// <summary>
    /// Claims due work under an expiring lease. Work whose lease expired after a provider call had started is not retried: its
    /// outcome is unknown, so it becomes Uncertain and is counted in the result.
    /// </summary>
    Task<ClaimResult> ClaimDueAsync(DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken);

    /// <summary>Marks that the provider call is about to start. False when the lease was lost, in which case nothing may be sent.</summary>
    Task<bool> BeginSendAsync(Guid notificationId, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Appends the attempt fact and advances the dispatch state together.</summary>
    Task CompleteAttemptAsync(AttemptCompletion completion, CancellationToken cancellationToken);

    Task AddRecoveryRequestAsync(PasswordRecoveryRequest request, CancellationToken cancellationToken);

    /// <summary>Deletes recovery requests past their expiry and returns how many were removed.</summary>
    Task<int> DeleteExpiredRecoveryRequestsAsync(DateTime nowUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecoveryRequestClaim>> ClaimRecoveryRequestsAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, int maxAttempts, CancellationToken cancellationToken);

    Task DeleteRecoveryRequestAsync(Guid requestId, CancellationToken cancellationToken);
}

/// <summary>Feedback-reminder scheduling state for the scheduler. Holds identifiers and times only, never feedback data.</summary>
public interface IFeedbackReminderStore
{
    Task<ReminderPolicySnapshot?> GetPolicyAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedbackReminderSchedule>> GetSchedulesAsync(IReadOnlyCollection<string> occurrenceIds, CancellationToken cancellationToken);

    /// <summary>Scheduled (not yet dispatched or cancelled) rows whose occurrence is not in <paramref name="knownOccurrenceIds"/>.</summary>
    Task<IReadOnlyList<FeedbackReminderSchedule>> GetScheduledOutsideAsync(IReadOnlyCollection<string> knownOccurrenceIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<FeedbackReminderSchedule>> GetDueAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken);

    /// <summary>Saves new rows and changes to the given rows in one unit of work.</summary>
    Task SaveAsync(IReadOnlyCollection<FeedbackReminderSchedule> added, IReadOnlyCollection<FeedbackReminderSchedule> changed, CancellationToken cancellationToken);
}

public sealed class NotificationConflictException(string reason, Exception inner) : Exception("A notification rule in the database rejected the change.", inner)
{
    public string Reason { get; } = reason;
}

/// <summary>Notification administration for the request pipeline. It shares the request's unit of work so a staged audit event commits with the change.</summary>
public interface INotificationAdminStore
{
    Task<IReadOnlyList<TemplateWithRevision>> ListTemplatesAsync(CancellationToken cancellationToken);

    Task<TemplateWithRevision?> FindTemplateAsync(NotificationEventType eventType, CancellationToken cancellationToken);

    Task<NotificationTemplate?> FindTrackedTemplateAsync(NotificationEventType eventType, CancellationToken cancellationToken);

    void AddRevision(NotificationTemplateRevision revision);

    Task<IReadOnlyList<NotificationSummary>> SearchAsync(NotificationSearch search, CancellationToken cancellationToken);

    Task<NotificationDetail?> GetDetailAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<NotificationRecord?> FindRecordAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<NotificationStatus?> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken);

    Task<NotificationRecord?> FindResendAsync(Guid parentId, string idempotencyKey, CancellationToken cancellationToken);

    /// <summary>The statuses of the resends already linked to a record.</summary>
    Task<IReadOnlyList<NotificationStatus>> GetResendStatusesAsync(Guid parentId, CancellationToken cancellationToken);

    void AddNotification(NotificationRecord record, NotificationDispatchState state);

    Task<FeedbackReminderPolicy?> FindPolicyAsync(CancellationToken cancellationToken);

    void AddPolicy(FeedbackReminderPolicy policy);

    /// <exception cref="NotificationConflictException">A uniqueness rule in the database rejected the change.</exception>
    Task SaveAsync(CancellationToken cancellationToken);
}
