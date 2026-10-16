namespace IitAcademicPortal.Domain.Notifications;

/// <summary>
/// The one portal-wide lead time before a feedback window closes at which reminders are due. There is no default: until an Admin
/// saves a value no reminder is scheduled, and there is no per-window override.
/// </summary>
public sealed class FeedbackReminderPolicy : AuditedEntity
{
    /// <summary>The singleton row's identifier.</summary>
    public static readonly Guid SingletonId = new("00000000-0000-0000-0000-0000000000f1");

    /// <summary>A sanity bound of one year; the specification sets no maximum.</summary>
    public const int MaxLeadTimeMinutes = 525_600;

    public Guid Id { get; private set; } = SingletonId;

    public int LeadTimeMinutes { get; private set; }

    public int Revision { get; private set; }

    public static FeedbackReminderPolicy Create(int leadTimeMinutes) => new() { LeadTimeMinutes = leadTimeMinutes, Revision = 1 };

    public void Change(int leadTimeMinutes)
    {
        if (leadTimeMinutes != LeadTimeMinutes)
        {
            LeadTimeMinutes = leadTimeMinutes;
            Revision++;
        }
    }
}

/// <summary>
/// Pre-dispatch scheduling state for one reminder to one student for one window occurrence. It holds only identifiers and times:
/// never feedback text, ratings, submission identifiers, or whether anyone submitted.
/// </summary>
public sealed class FeedbackReminderSchedule
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string OccurrenceId { get; private set; } = string.Empty;

    public string RecipientUserId { get; private set; } = string.Empty;

    public DateTime CloseAtUtc { get; set; }

    public int PolicyRevision { get; set; }

    public DateTime DueAtUtc { get; set; }

    public ReminderScheduleState State { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public static FeedbackReminderSchedule Create(
        string occurrenceId, string recipientUserId, DateTime closeAtUtc, int policyRevision, DateTime dueAtUtc, DateTime nowUtc) =>
        new()
        {
            OccurrenceId = occurrenceId,
            RecipientUserId = recipientUserId,
            CloseAtUtc = closeAtUtc,
            PolicyRevision = policyRevision,
            DueAtUtc = dueAtUtc,
            State = ReminderScheduleState.Scheduled,
            UpdatedAtUtc = nowUtc,
        };
}
