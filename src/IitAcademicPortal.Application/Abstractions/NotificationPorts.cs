using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>An account as the notification capability needs to know it. Resolved when work is accepted, never cached.</summary>
public sealed record RecipientInfo(string UserId, string? Email, bool IsDeactivated);

/// <summary>Identity-owned lookup of recipient addresses by stable user ID.</summary>
public interface IRecipientDirectory
{
    /// <summary>Returns each existing account; IDs of removed accounts are absent.</summary>
    Task<IReadOnlyDictionary<string, RecipientInfo>> FindAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}

public enum SecurityContentStatus
{
    /// <summary>Content values were produced for one authorized delivery attempt.</summary>
    Ready,

    /// <summary>The owning workflow no longer authorizes this message (account removed, address changed, not eligible).</summary>
    Ineligible,

    /// <summary>The owning workflow has no secure-link provider yet, so a link cannot be produced.</summary>
    Unavailable,
}

/// <summary>The outcome of asking the owning account workflow for security content. <see cref="Values"/> live only in memory.</summary>
public sealed record SecurityContentResult(SecurityContentStatus Status, string? Code = null, IReadOnlyDictionary<string, string>? Values = null)
{
    public static SecurityContentResult Eligible { get; } = new(SecurityContentStatus.Ready);

    public static SecurityContentResult Refused(string code) => new(SecurityContentStatus.Ineligible, code);

    public static SecurityContentResult NoProvider(string code) => new(SecurityContentStatus.Unavailable, code);
}

/// <summary>
/// The owning account workflow's contract for one security event: it decides whether a message is authorized and, for one
/// delivery attempt only, produces the link or proof. Nothing it returns may be persisted or logged.
/// </summary>
public interface ISecurityContentProvider
{
    NotificationEventType EventType { get; }

    /// <summary>Checks eligibility without generating any proof (used before accepting a resend).</summary>
    Task<SecurityContentResult> CheckAsync(string recipientUserId, string destination, CancellationToken cancellationToken);

    /// <summary>Produces fresh values (link, expiry text) for one delivery attempt, or refuses.</summary>
    Task<SecurityContentResult> PrepareAsync(string recipientUserId, string destination, CancellationToken cancellationToken);
}

public enum FeedbackWindowStatus
{
    Open,
    Closed,
    Cancelled,
    Superseded,
}

/// <summary>
/// What the owning feedback workflow tells the scheduler about one window occurrence. It carries only the occurrence, labels, the
/// current close time, and the eligible students. Feedback submissions, ratings, and submission state are never part of it.
/// </summary>
public sealed record FeedbackWindowSnapshot(
    string OccurrenceId,
    string CourseLabel,
    string WindowLabel,
    DateTime CloseAtUtc,
    FeedbackWindowStatus Status,
    IReadOnlyList<string> EligibleStudentUserIds);

/// <summary>Authoritative source of feedback windows, supplied by the feedback-window owner. No owner exists yet.</summary>
public interface IFeedbackWindowSource
{
    /// <summary>The windows whose reminders may still need scheduling or cancelling, with their current state.</summary>
    Task<IReadOnlyList<FeedbackWindowSnapshot>> GetWindowsAsync(CancellationToken cancellationToken);
}

/// <summary>Protects short-lived secrets (a submitted address) at rest. Backed by ASP.NET Core Data Protection.</summary>
public interface ITransientSecretProtector
{
    string Protect(string value);

    /// <exception cref="InvalidOperationException">The value was tampered with or protected under another key.</exception>
    string Unprotect(string protectedValue);
}
