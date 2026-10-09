namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Server-derived context for the current operation. Every value comes from the trusted server side, never
/// from client-supplied claims.
/// </summary>
public interface IAuditRequestContext
{
    /// <summary>The authenticated session's user, or null when the request is anonymous or there is no request.</summary>
    string? ActorUserId { get; }

    /// <summary>A server-generated identifier relating the events of one request; null when there is no request.</summary>
    string? CorrelationId { get; }

    /// <summary>The client IP address from trusted forwarded headers; null when there is no request.</summary>
    string? Source { get; }
}
