namespace IitAcademicPortal.Domain.Sessions;

public enum SessionRevocationReason
{
    Logout,
    PasswordReset,
    SecurityAction,
    IdleTimeout,
    TeacherDeactivated,

    /// <summary>An Admin deactivated a generic (Admin or Student) account through User Management.</summary>
    AccountDeactivated,
}
