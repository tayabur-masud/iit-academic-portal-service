namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by Teacher Management, named <c>teacher.{action}</c>. Before and after values are kept only for the
/// approved fields. Email and phone are personal data, so they are recorded as "changed" only. No password, hash, token, or
/// credential is ever part of these events.
/// </summary>
public static class TeacherAuditEventDefinitions
{
    public static readonly string[] ApprovedFields = ["displayName", "employeeStaffId", "designationId", "isActive", "accountStatus", "roles", "defaultRole"];

    private static readonly string[] IndicatorFields = ["email", "phone"];

    public static AuditEventDefinition Created { get; } = Define("teacher.created");

    public static AuditEventDefinition Updated { get; } = Define("teacher.updated");

    public static AuditEventDefinition RolesChanged { get; } = Define("teacher.roles.changed");

    public static AuditEventDefinition Deactivated { get; } = Define("teacher.deactivated");

    /// <summary>Raised by the internal Coordinator synchronizer, in the calling assignment workflow's unit of work.</summary>
    public static AuditEventDefinition CoordinatorRoleSynchronized { get; } = Define("teacher.coordinator-role.synchronized");

    private static AuditEventDefinition Define(string eventType) =>
        AuditEventDefinition.Business(eventType, ApprovedFields).WithChangeIndicators(IndicatorFields);
}
