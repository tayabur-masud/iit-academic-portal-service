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
    string? Cursor);

public sealed record AuditEventPage(IReadOnlyList<AuditEvent> Items, string? NextCursor);
