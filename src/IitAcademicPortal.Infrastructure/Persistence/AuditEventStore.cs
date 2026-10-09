using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>Append and read access to formal audit events. Nothing here updates or deletes an event.</summary>
public sealed class AuditEventStore(PortalDbContext db) : IAuditEventStore
{
    public void Add(AuditEvent auditEvent) => db.AuditEvents.Add(auditEvent);

    public async Task<AuditEventPage> SearchAsync(AuditSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var query = db.AuditEvents.AsNoTracking();

        if (criteria.FromUtc is { } from)
        {
            query = query.Where(e => e.OccurredAtUtc >= from);
        }

        if (criteria.ToUtc is { } to)
        {
            query = query.Where(e => e.OccurredAtUtc < to);
        }

        if (!string.IsNullOrEmpty(criteria.ActorUserId))
        {
            query = query.Where(e => e.ActorUserId == criteria.ActorUserId);
        }

        if (criteria.Category is { } category)
        {
            query = query.Where(e => e.Category == category);
        }

        if (!string.IsNullOrEmpty(criteria.EventType))
        {
            query = query.Where(e => e.EventType == criteria.EventType);
        }

        if (!string.IsNullOrEmpty(criteria.EntityType))
        {
            query = query.Where(e => e.EntityType == criteria.EntityType);
        }

        if (!string.IsNullOrEmpty(criteria.EntityId))
        {
            query = query.Where(e => e.EntityId == criteria.EntityId);
        }

        if (criteria.Outcome is { } outcome)
        {
            query = query.Where(e => e.Outcome == outcome);
        }

        if (!string.IsNullOrEmpty(criteria.CorrelationId))
        {
            query = query.Where(e => e.CorrelationId == criteria.CorrelationId);
        }

        // Keyset continuation after the last event of the previous page: strictly older, or the same
        // instant with a smaller ID. New inserts are newer, so they never shift later pages.
        if (AuditCursor.TryDecode(criteria.Cursor, out var cursorTime, out var cursorId))
        {
            query = query.Where(e => e.OccurredAtUtc < cursorTime
                || (e.OccurredAtUtc == cursorTime && e.Id.CompareTo(cursorId) < 0));
        }

        var rows = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Take(criteria.PageSize + 1)
            .ToListAsync(cancellationToken);

        if (rows.Count <= criteria.PageSize)
        {
            return new AuditEventPage(rows, null);
        }

        rows.RemoveAt(rows.Count - 1);
        var last = rows[^1];
        return new AuditEventPage(rows, AuditCursor.Encode(last.OccurredAtUtc, last.Id));
    }

    public Task<AuditEvent?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, cancellationToken);
}
