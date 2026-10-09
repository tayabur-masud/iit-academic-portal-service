using System.Globalization;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>The raw filters of a review request as the client sent them, before validation.</summary>
public sealed record AuditSearchRequest(
    string? FromUtc,
    string? ToUtc,
    string? ActorUserId,
    string? Category,
    string? EventType,
    string? EntityType,
    string? EntityId,
    string? Outcome,
    string? CorrelationId,
    int? PageSize,
    string? Cursor);

/// <summary>A stored event together with its actor's current email, resolved when the event is viewed.</summary>
public sealed record AuditEventView(AuditEvent Event, string? ActorDisplay);

public sealed record AuditEventViewPage(IReadOnlyList<AuditEventView> Items, string? NextCursor);

/// <summary>Either a value, or field-level validation errors that become an HTTP 400.</summary>
public sealed record AuditReviewResult<T>(T? Value, IReadOnlyDictionary<string, string[]>? Errors)
{
    public bool IsValid => Errors is null;
}

/// <summary>
/// Search and detail for Admin review of audit history. It validates the filters, resolves actor emails for the
/// page in one lookup, and records each successful read as an <c>audit.review.accessed</c> security event.
/// </summary>
public sealed class AuditReviewService(
    IAuditEventStore store,
    IAuditActorDirectory actors,
    IAuditEventRecorder recorder)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<AuditReviewResult<AuditEventViewPage>> SearchAsync(
        AuditSearchRequest request, string? reviewerUserId, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        var criteria = Validate(request, errors);
        if (criteria is null)
        {
            return new AuditReviewResult<AuditEventViewPage>(null, errors);
        }

        var page = await store.SearchAsync(criteria, cancellationToken);
        var emails = await actors.GetEmailsAsync(
            page.Items.Where(e => e.ActorUserId is not null).Select(e => e.ActorUserId!).Distinct().ToArray(), cancellationToken);
        var items = page.Items
            .Select(e => new AuditEventView(e, e.ActorUserId is not null && emails.TryGetValue(e.ActorUserId, out var email) ? email : null))
            .ToList();

        await RecordAccessAsync(reviewerUserId, "search", entityId: null, FiltersUsed(request));
        return new AuditReviewResult<AuditEventViewPage>(new AuditEventViewPage(items, page.NextCursor), null);
    }

    public async Task<AuditEventView?> GetAsync(Guid eventId, string? reviewerUserId, CancellationToken cancellationToken)
    {
        var auditEvent = await store.GetAsync(eventId, cancellationToken);
        if (auditEvent is null)
        {
            return null;
        }

        var emails = await actors.GetEmailsAsync(
            auditEvent.ActorUserId is null ? [] : [auditEvent.ActorUserId], cancellationToken);
        await RecordAccessAsync(reviewerUserId, "detail", eventId.ToString(), filters: null);
        return new AuditEventView(
            auditEvent, auditEvent.ActorUserId is not null && emails.TryGetValue(auditEvent.ActorUserId, out var email) ? email : null);
    }

    // The access names who looked, what kind of access it was, and the filters used; it never copies the
    // contents of the events that were returned.
    private Task RecordAccessAsync(string? reviewerUserId, string kind, string? entityId, Dictionary<string, object?>? filters)
    {
        var metadata = new Dictionary<string, object?> { ["kind"] = kind };
        if (filters is { Count: > 0 })
        {
            metadata["filters"] = filters;
        }

        return recorder.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.AuditReviewAccessed, AuditOutcome.Success)
        {
            ActorUserId = reviewerUserId,
            EntityType = entityId is null ? null : "AuditEvent",
            EntityId = entityId,
            Metadata = metadata,
        });
    }

    private static Dictionary<string, object?> FiltersUsed(AuditSearchRequest r)
    {
        var filters = new Dictionary<string, object?>();
        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                filters[name] = value;
            }
        }

        Add("fromUtc", r.FromUtc);
        Add("toUtc", r.ToUtc);
        Add("actorUserId", r.ActorUserId);
        Add("category", r.Category);
        Add("eventType", r.EventType);
        Add("entityType", r.EntityType);
        Add("entityId", r.EntityId);
        Add("outcome", r.Outcome);
        Add("correlationId", r.CorrelationId);
        if (r.PageSize is { } size)
        {
            filters["pageSize"] = size;
        }

        return filters;
    }

    private static AuditSearchCriteria? Validate(AuditSearchRequest request, Dictionary<string, string[]> errors)
    {
        var from = ParseUtc(request.FromUtc, "fromUtc", errors);
        var to = ParseUtc(request.ToUtc, "toUtc", errors);
        if (from is not null && to is not null && to <= from)
        {
            errors["toUtc"] = ["The end of the range must be later than its start."];
        }

        var category = ParseEnum<AuditEventCategory>(request.Category, "category", errors);
        var outcome = ParseEnum<AuditOutcome>(request.Outcome, "outcome", errors);

        var pageSize = request.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"The page size must be between 1 and {MaxPageSize}."];
        }

        if (!string.IsNullOrEmpty(request.Cursor) && !AuditCursor.TryDecode(request.Cursor, out _, out _))
        {
            errors["cursor"] = ["The cursor is not valid. Start again from the first page."];
        }

        foreach (var (name, value, max) in new[]
        {
            ("actorUserId", request.ActorUserId, AuditEvent.MaxActorUserIdLength),
            ("eventType", request.EventType, AuditEvent.MaxEventTypeLength),
            ("entityType", request.EntityType, AuditEvent.MaxEntityTypeLength),
            ("entityId", request.EntityId, AuditEvent.MaxEntityIdLength),
            ("correlationId", request.CorrelationId, AuditEvent.MaxCorrelationIdLength),
        })
        {
            if (value is { Length: > 0 } && value.Length > max)
            {
                errors[name] = [$"This filter is limited to {max} characters."];
            }
        }

        return errors.Count > 0
            ? null
            : new AuditSearchCriteria(
                from, to, Blank(request.ActorUserId), category, Blank(request.EventType), Blank(request.EntityType),
                Blank(request.EntityId), outcome, Blank(request.CorrelationId), pageSize, Blank(request.Cursor));
    }

    private static DateTime? ParseUtc(string? text, string name, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // A value without an offset is read as UTC, never as the server's local time.
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            return value.UtcDateTime;
        }

        errors[name] = ["Enter a valid date and time, for example 2026-10-09T17:00:00Z."];
        return null;
    }

    private static T? ParseEnum<T>(string? text, string name, Dictionary<string, string[]> errors) where T : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value) && !int.TryParse(text, out _))
        {
            return value;
        }

        errors[name] = [$"Use one of: {string.Join(", ", Enum.GetNames<T>().Select(n => n.ToLowerInvariant()))}."];
        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
