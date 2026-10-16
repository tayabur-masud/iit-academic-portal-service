namespace IitAcademicPortal.Domain.Notifications;

/// <summary>The business events that can produce a notification. Producers name the event, never a channel.</summary>
public enum NotificationEventType
{
    TeacherInvitation,
    StudentInvitation,
    EmailConfirmation,
    PasswordReset,
    ResultsPublished,
    FeedbackWindowOpened,
    FeedbackClosingReminder,
}

/// <summary>
/// Delivery channels. Email is the only channel the portal delivers through: it is the only one with a seeded template and a registered
/// adapter. <see cref="Test"/> exists solely so a test can prove that a producer's request is unchanged when another channel is added; it
/// has no template, no production adapter, and is never delivered. Adding a real channel later is a new value, a seeded template, and an
/// adapter: no producer changes.
/// </summary>
public enum NotificationChannel
{
    Email,

    /// <summary>Reserved for the channel-independence conformance test. Never seeded and never registered outside tests.</summary>
    Test,
}

/// <summary>The current state of one notification execution, projected from its dispatch state.</summary>
public enum NotificationStatus
{
    /// <summary>Accepted and waiting for its first attempt.</summary>
    Pending,

    /// <summary>A retryable failure was recorded and another attempt is scheduled.</summary>
    Retrying,

    /// <summary>The delivery service accepted the message. Not a promise that it reached or was read in an inbox.</summary>
    Sent,

    /// <summary>No recipient address, a permanent rejection, or every automatic attempt was used. Eligible for an Admin resend.</summary>
    Failed,

    /// <summary>The ordinary template was disabled when the request arrived, so nothing was sent. Re-enabling never replays it.</summary>
    Suppressed,

    /// <summary>Whether the provider accepted the message cannot be established. Never retried automatically and not resendable.</summary>
    Uncertain,
}

public enum DeliveryAttemptOutcome
{
    Accepted,
    RetryableFailure,
    PermanentFailure,
    Uncertain,
}

public enum ReminderScheduleState
{
    Scheduled,
    Cancelled,
    Dispatched,
}

public static class NotificationEventTypeExtensions
{
    /// <summary>
    /// Security messages carry a link or proof: their rendered content is never stored, previewed with real values, or logged.
    /// </summary>
    public static bool IsSecurity(this NotificationEventType eventType) =>
        eventType is NotificationEventType.TeacherInvitation
            or NotificationEventType.StudentInvitation
            or NotificationEventType.EmailConfirmation
            or NotificationEventType.PasswordReset;

    /// <summary>The stable kebab-case name used in routes, for example <c>password-reset</c>.</summary>
    public static string ToSlug(this NotificationEventType eventType) =>
        System.Text.RegularExpressions.Regex.Replace(eventType.ToString(), "(?<!^)([A-Z])", "-$1").ToLowerInvariant();

    public static bool TryParseSlug(string? slug, out NotificationEventType eventType)
    {
        foreach (var candidate in Enum.GetValues<NotificationEventType>())
        {
            if (string.Equals(candidate.ToSlug(), slug, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), slug, StringComparison.OrdinalIgnoreCase))
            {
                eventType = candidate;
                return true;
            }
        }

        eventType = default;
        return false;
    }
}
