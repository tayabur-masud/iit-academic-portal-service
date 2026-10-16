using System.Text;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>The raw history filters as they arrive from a request; the service validates and normalizes them.</summary>
public sealed record NotificationQuery(
    string? Recipient,
    string? EventType,
    string? Status,
    DateTime? FromUtc,
    DateTime? ToUtc,
    int? PageSize,
    string? Cursor);

/// <summary>An opaque position in history ordered by request time descending, then identifier descending.</summary>
public static class NotificationCursor
{
    public static string Encode(DateTime requestedAtUtc, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{requestedAtUtc.Ticks}|{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out DateTime requestedAtUtc, out Guid id)
    {
        (requestedAtUtc, id) = (default, Guid.Empty);
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 200)
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('|');
            if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParseExact(parts[1], "N", out id)
                || ticks < 0 || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            requestedAtUtc = new DateTime(ticks, DateTimeKind.Utc);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>
/// Searches the permanent notification history with bounded keyset batches and returns safe detail. History carries delivery
/// facts only: security messages never have content, and no feedback or proof data exists here. Authorization is the caller's job.
/// </summary>
public sealed class NotificationHistoryService(INotificationAdminStore store)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxRecipientLength = 320;

    public async Task<NotificationResult<NotificationPage>> SearchAsync(NotificationQuery query, CancellationToken cancellationToken)
    {
        var errors = new NotificationErrors();

        NotificationEventType? eventType = null;
        if (!string.IsNullOrWhiteSpace(query.EventType))
        {
            if (NotificationEventTypeExtensions.TryParseSlug(query.EventType.Trim(), out var parsed))
            {
                eventType = parsed;
            }
            else
            {
                errors.Add("eventType", "Choose a supported event type.");
            }
        }

        NotificationStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<NotificationStatus>(query.Status.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors.Add("status", "The status must be Pending, Retrying, Sent, Failed, Suppressed, or Uncertain.");
            }
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"The page size must be between 1 and {MaxPageSize}.");
        }

        var recipient = string.IsNullOrWhiteSpace(query.Recipient) ? null : query.Recipient.Trim();
        if (recipient is { Length: > MaxRecipientLength })
        {
            errors.Add("recipient", $"The recipient filter can have at most {MaxRecipientLength} characters.");
        }

        var from = query.FromUtc is { } f ? AsUtc(f) : (DateTime?)null;
        var to = query.ToUtc is { } t ? AsUtc(t) : (DateTime?)null;
        if (from is not null && to is not null && from >= to)
        {
            errors.Add("toUtc", "The end of the range must be after its start. The start is included and the end is not.");
        }

        DateTime? afterAt = null;
        Guid? afterId = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (NotificationCursor.TryDecode(query.Cursor, out var at, out var id))
            {
                (afterAt, afterId) = (at, id);
            }
            else
            {
                errors.Add("cursor", "The cursor is not valid. Start again from the first page.");
            }
        }

        if (errors.Any)
        {
            return NotificationResult<NotificationPage>.Invalid(errors);
        }

        // One extra row tells whether another batch exists without counting every match.
        var rows = await store.SearchAsync(
            new NotificationSearch(recipient, eventType, status, from, to, pageSize + 1, afterAt, afterId), cancellationToken);

        string? next = null;
        if (rows.Count > pageSize)
        {
            rows = rows.Take(pageSize).ToList();
            next = NotificationCursor.Encode(rows[^1].RequestedAtUtc, rows[^1].Id);
        }

        return new NotificationResult<NotificationPage>(NotificationOutcome.Ok, new NotificationPage(rows, next));
    }

    public async Task<NotificationResult<NotificationDetail>> GetAsync(Guid notificationId, CancellationToken cancellationToken) =>
        await store.GetDetailAsync(notificationId, cancellationToken) is { } detail
            ? new NotificationResult<NotificationDetail>(NotificationOutcome.Ok, detail)
            : NotificationResult<NotificationDetail>.NotFound;

    // A date without a zone is read as UTC, as every filter in this API is UTC.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}
