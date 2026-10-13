using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>Filters for audit search. A start time is inclusive and an end time is exclusive, both UTC.</summary>
public sealed record AuditSearchCriteria(
    DateTime? FromUtc,
    DateTime? ToUtc,
    string? ActorUserId,
    AuditEventCategory? Category,
    string? EventType,
    string? EntityType,
    string? EntityId,
    AuditOutcome? Outcome,
    string? CorrelationId,
    int PageSize,
    string? Cursor,
    int? Page = null);

/// <summary>
/// One page of events. <c>TotalCount</c> is set only for a numbered-page request (<c>Page</c>), which is the one mode that
/// counts every match; cursor paging never does.
/// </summary>
public sealed record AuditEventPage(IReadOnlyList<AuditEvent> Items, string? NextCursor, long? TotalCount = null);
