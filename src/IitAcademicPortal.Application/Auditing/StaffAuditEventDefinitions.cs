namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by Staff Management, named <c>staff.profile.{action}</c>. The name and Designation keep their before and
/// after values; the mobile number is personal data, so it is recorded as "changed" only. A create or update's metadata carries the
/// account, whether the account was created, and the saving Admin's <c>manualVerificationConfirmed</c> attestation; a deactivation's
/// carries the account and the number of revoked sessions. No credential, proof, or document is ever part of these events.
/// </summary>
public static class StaffAuditEventDefinitions
{
    public static readonly string[] ApprovedFields = ["name", "designationId", "isActive", "accountStatus"];

    private static readonly string[] IndicatorFields = ["mobileNumber"];

    public static AuditEventDefinition Created { get; } = Define("staff.profile.created");

    public static AuditEventDefinition Updated { get; } = Define("staff.profile.updated");

    public static AuditEventDefinition Deactivated { get; } = Define("staff.profile.deactivated");

    private static AuditEventDefinition Define(string eventType) =>
        AuditEventDefinition.Business(eventType, ApprovedFields).WithChangeIndicators(IndicatorFields);
}
