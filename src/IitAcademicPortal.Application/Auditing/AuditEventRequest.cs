using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>One field's previous and new value, offered to the change-summary builder.</summary>
public sealed record AuditFieldChange(string Field, object? OldValue, object? NewValue);

/// <summary>
/// What a feature asks the audit capability to record. The identity, time, correlation, and source are
/// derived by the server; the caller supplies only the action's own facts.
/// </summary>
public sealed record AuditEventRequest(AuditEventDefinition Definition, AuditOutcome Outcome)
{
    /// <summary>
    /// The stable ID of the user who acted. Pass the authenticated user for most events, and null for
    /// anonymous or system activity and for failed sign-ins.
    /// </summary>
    public string? ActorUserId { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    /// <summary>Safe context approved for this event type. Credential-like content is redacted regardless.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; init; }

    /// <summary>Candidate field changes. Only fields the definition approves keep their values.</summary>
    public IReadOnlyList<AuditFieldChange>? Changes { get; init; }
}

/// <summary>An invalid audit request from a feature, such as an oversized value or a category mismatch.</summary>
public sealed class AuditValidationException(string message) : Exception(message);
