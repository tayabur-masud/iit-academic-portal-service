namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// The initial security event catalog. Business event types are declared by the feature that owns the
/// action, when that feature is built.
/// </summary>
public static class AuditEventDefinitions
{
    /// <summary>One event per sign-in attempt. A failure records no account.</summary>
    public static AuditEventDefinition SignIn { get; } = AuditEventDefinition.Security("auth.sign-in");

    public static AuditEventDefinition PasswordResetCompleted { get; } = AuditEventDefinition.Security("auth.password-reset.completed");

    /// <summary>
    /// A first-password setup attempt that was refused (invalid, expired, replayed, or stale link, or a rejected password). Like a
    /// failed reset it records no account, so the trail reveals nothing the public response does not. Success is a business event.
    /// </summary>
    public static AuditEventDefinition CredentialSetupRejected { get; } = AuditEventDefinition.Security("auth.credential-setup.rejected");

    /// <summary>Metadata carries the reason: Logout, PasswordReset, or IdleTimeout.</summary>
    public static AuditEventDefinition SessionRevoked { get; } = AuditEventDefinition.Security("auth.session.revoked");

    public static AuditEventDefinition RoleSwitched { get; } = AuditEventDefinition.Security("auth.role.switched");

    /// <summary>An authenticated request refused with HTTP 403. Anonymous 401 responses are not events.</summary>
    public static AuditEventDefinition AccessDenied { get; } = AuditEventDefinition.Security("access.denied");

    public static AuditEventDefinition AuditMutationAttempted { get; } = AuditEventDefinition.Security("audit.mutation.attempted");

    public static AuditEventDefinition AuditReviewAccessed { get; } = AuditEventDefinition.Security("audit.review.accessed");

    public static IReadOnlyList<AuditEventDefinition> Security { get; } =
    [
        SignIn,
        PasswordResetCompleted,
        CredentialSetupRejected,
        SessionRevoked,
        RoleSwitched,
        AccessDenied,
        AuditMutationAttempted,
        AuditReviewAccessed,
    ];
}
