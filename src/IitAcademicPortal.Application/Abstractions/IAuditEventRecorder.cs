using IitAcademicPortal.Application.Auditing;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// The server-side boundary every feature uses to write audit events. Clients never submit events.
/// </summary>
public interface IAuditEventRecorder
{
    /// <summary>
    /// Adds a mandatory business event to the caller's current unit of work without saving. The event commits
    /// in the same transaction as the business change, or neither commits. Also used for a policy-based
    /// business denial: stage the event and save it alone.
    /// </summary>
    /// <exception cref="AuditValidationException">The request is invalid; nothing is staged.</exception>
    void Stage(AuditEventRequest request);

    /// <summary>
    /// Records a security event through the durable outbox. This method never throws and never delays or
    /// changes the authentication or authorization decision that produced the event: if the outbox write
    /// fails, the safe envelope goes to the durable fallback sink and a critical alert is raised.
    /// </summary>
    Task RecordSecurityAsync(AuditEventRequest request);
}
