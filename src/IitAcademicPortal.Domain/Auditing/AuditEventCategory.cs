namespace IitAcademicPortal.Domain.Auditing;

/// <summary>Formal audit classes. Optional diagnostic events are not formal audit history.</summary>
public enum AuditEventCategory
{
    Business,
    Security,
}
