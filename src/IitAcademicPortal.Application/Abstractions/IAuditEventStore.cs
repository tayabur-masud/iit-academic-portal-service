using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>Append and read access to formal audit events. There is no update or delete.</summary>
public interface IAuditEventStore
{
    /// <summary>Adds an event to the current unit of work; the caller's SaveChanges commits it.</summary>
    void Add(AuditEvent auditEvent);

    Task<AuditEventPage> SearchAsync(AuditSearchCriteria criteria, CancellationToken cancellationToken);

    Task<AuditEvent?> GetAsync(Guid id, CancellationToken cancellationToken);
}
