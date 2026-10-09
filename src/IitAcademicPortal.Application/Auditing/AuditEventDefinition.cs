using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// An event type declared by the feature that owns the action: its category and the only fields whose
/// previous and new values may be recorded.
/// </summary>
/// <param name="EventType">Namespaced, lower-case, for example <c>course.created</c>.</param>
/// <param name="Category">Business or security.</param>
/// <param name="ApprovedChangeFields">Fields whose before and after values are safe to keep.</param>
/// <param name="ChangeIndicatorFields">Sensitive fields recorded only as "changed", never with values.</param>
public sealed record AuditEventDefinition(
    string EventType,
    AuditEventCategory Category,
    IReadOnlySet<string> ApprovedChangeFields,
    IReadOnlySet<string> ChangeIndicatorFields)
{
    private static readonly IReadOnlySet<string> None = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Declares a mandatory business event and the fields approved for before/after tracking.</summary>
    public static AuditEventDefinition Business(string eventType, params string[] approvedChangeFields) =>
        new(eventType, AuditEventCategory.Business, new HashSet<string>(approvedChangeFields, StringComparer.Ordinal), None);

    /// <summary>Same as <see cref="Business"/> but also records the listed sensitive fields as "changed" only.</summary>
    public AuditEventDefinition WithChangeIndicators(params string[] fields) =>
        this with { ChangeIndicatorFields = new HashSet<string>(fields, StringComparer.Ordinal) };

    public static AuditEventDefinition Security(string eventType) =>
        new(eventType, AuditEventCategory.Security, None, None);
}
