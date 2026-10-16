using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>Replaces a template's wording and enabled state. <c>expectedRevision</c> is the revision the Admin was editing.</summary>
public sealed class UpdateNotificationTemplateRequest
{
    public int? ExpectedRevision { get; init; }

    public string? Subject { get; init; }

    public string? Body { get; init; }

    public bool? Enabled { get; init; }

    public TemplateUpdate ToUpdate() => new(ExpectedRevision, Subject, Body, Enabled);
}

/// <summary>Draft wording and optional synthetic sample values for a preview. Nothing is sent or stored.</summary>
public sealed class PreviewNotificationTemplateRequest
{
    public string? Subject { get; init; }

    public string? Body { get; init; }

    public Dictionary<string, string>? SampleValues { get; init; }
}

public sealed record NotificationTemplateResponse(
    string EventType,
    string Slug,
    string Name,
    string Channel,
    bool IsSecurity,
    bool Enabled,
    int Revision,
    string Subject,
    string Body,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> RequiredPlaceholders,
    DateTime UpdatedAtUtc)
{
    public static NotificationTemplateResponse From(TemplateView view) => new(
        view.EventType.ToString(), view.EventType.ToSlug(), view.Name, view.Channel.ToString(), view.IsSecurity, view.Enabled,
        view.RevisionNumber, view.Subject, view.Body, view.Placeholders, view.RequiredPlaceholders, view.UpdatedAtUtc);
}

/// <summary>The plain text and the same message in the branded HTML layout, both made with sample data only.</summary>
public sealed record NotificationPreviewResponse(string Subject, string Body, string Html, bool Synthetic);

public sealed class NotificationQueryString
{
    public string? Recipient { get; init; }

    public string? EventType { get; init; }

    public string? Status { get; init; }

    public DateTime? FromUtc { get; init; }

    public DateTime? ToUtc { get; init; }

    public int? PageSize { get; init; }

    public string? Cursor { get; init; }

    public NotificationQuery ToQuery() => new(Recipient, EventType, Status, FromUtc, ToUtc, PageSize, Cursor);
}

/// <summary>A history row: delivery facts only, never message content.</summary>
public sealed record NotificationResponse(
    Guid Id,
    string EventType,
    string RecipientUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Destination,
    string Channel,
    string Status,
    DateTime RequestedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? SentAtUtc,
    bool ContentWithheld,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ParentNotificationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureCode)
{
    public static NotificationResponse From(NotificationSummary s) => new(
        s.Id, s.EventType.ToString(), s.RecipientUserId, s.Destination, s.Channel.ToString(), s.Status.ToString(), s.RequestedAtUtc,
        s.SentAtUtc, s.ContentWithheld, s.ParentNotificationId, s.FailureCode);
}

public sealed record NotificationPageResponse(IReadOnlyList<NotificationResponse> Items, string? NextCursor);

public sealed record NotificationAttemptResponse(
    int AttemptNumber,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string Outcome,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? ProviderAcceptedAtUtc);

/// <summary>
/// A history record with its attempts. An ordinary message adds its exact <c>subject</c> and <c>body</c>; a security message omits
/// both (they are never stored) and says <c>contentWithheld: true</c>.
/// </summary>
public sealed record NotificationDetailResponse(
    NotificationResponse Notification,
    string OccurrenceId,
    int TemplateRevision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Subject,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body,
    IReadOnlyList<NotificationAttemptResponse> Attempts)
{
    public static NotificationDetailResponse From(NotificationDetail d) => new(
        NotificationResponse.From(d.Summary), d.OccurrenceId, d.TemplateRevisionNumber, d.Subject, d.Body,
        d.Attempts.Select(a => new NotificationAttemptResponse(
            a.AttemptNumber, a.StartedAtUtc, a.CompletedAtUtc, a.Outcome.ToString(), a.FailureCode, a.ProviderAcceptedAtUtc)).ToList());
}

public sealed class FeedbackReminderPolicyRequest
{
    public int? LeadTimeMinutes { get; init; }
}

/// <summary><c>configured</c> is false until an Admin saves a value; <c>leadTimeMinutes</c> is then absent.</summary>
public sealed record FeedbackReminderPolicyResponse(
    bool Configured,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? LeadTimeMinutes)
{
    public static FeedbackReminderPolicyResponse From(ReminderPolicyView view) => new(view.Configured, view.LeadTimeMinutes);
}
