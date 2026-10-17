namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by User Management, named <c>account.{action}</c>, with the account as the entity (<c>UserAccount</c>, the
/// stable user ID). Before and after values are kept only for the approved access fields. The email is personal data, so it is
/// recorded as "changed" only. No password, hash, security stamp, setup proof, or setup link is ever part of these events.
/// </summary>
public static class UserAccountAuditEventDefinitions
{
    public const string EntityType = "UserAccount";

    public static readonly string[] ApprovedFields = ["accountStatus", "roles", "defaultRole"];

    private static readonly string[] IndicatorFields = ["email"];

    public static AuditEventDefinition Created { get; } = Define("account.created");

    public static AuditEventDefinition EmailChanged { get; } = Define("account.email.changed");

    public static AuditEventDefinition RolesChanged { get; } = Define("account.roles.changed");

    public static AuditEventDefinition Deactivated { get; } = Define("account.deactivated");

    /// <summary>An Admin asked for a fresh setup email; earlier setup links stopped working.</summary>
    public static AuditEventDefinition CredentialSetupReissued { get; } = Define("account.credential-setup.reissued");

    /// <summary>The account holder set the first password with a valid setup link, and the account became Active.</summary>
    public static AuditEventDefinition CredentialSetupCompleted { get; } = Define("account.credential-setup.completed");

    private static AuditEventDefinition Define(string eventType) =>
        AuditEventDefinition.Business(eventType, ApprovedFields).WithChangeIndicators(IndicatorFields);
}
