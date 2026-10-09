namespace IitAcademicPortal.Domain.Auditing;

/// <summary>
/// <c>Failure</c> is an unsuccessful operation; <c>Denied</c> is a policy or authorization refusal.
/// </summary>
public enum AuditOutcome
{
    Success,
    Failure,
    Denied,
}
