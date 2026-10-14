using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Domain.Teachers;

/// <summary>
/// A teacher's directory record, linked one-to-one to a portal account. Credentials, email, and roles stay with the account;
/// the profile holds the teacher's name, optional staff identifier, optional phone, and optional designation. A profile is
/// never deleted: deactivating a teacher marks it inactive so history and references survive.
/// </summary>
public sealed class TeacherProfile : AuditedEntity
{
    public const int MaxPhoneLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string UserId { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string? EmployeeStaffId { get; private set; }

    /// <summary>Trimmed, single-spaced, upper-case staff ID; unique across all teacher profiles (active or not) when present.</summary>
    public string? NormalizedEmployeeStaffId { get; private set; }

    public string? Phone { get; private set; }

    public Guid? DesignationId { get; private set; }

    public bool IsActive { get; private set; } = true;

    public static TeacherProfile Create(string userId) => new() { UserId = userId };

    public void Update(string displayName, string? employeeStaffId, string? phone, Guid? designationId)
    {
        DisplayName = NameNormalizer.Display(displayName);
        EmployeeStaffId = NameNormalizer.DisplayCode(employeeStaffId);
        NormalizedEmployeeStaffId = NameNormalizer.NormalizeCode(employeeStaffId);
        Phone = NameNormalizer.DisplayCode(phone);
        DesignationId = designationId;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
