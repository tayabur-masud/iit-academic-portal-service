namespace IitAcademicPortal.Domain.Students;

/// <summary>
/// The person-level Student record, linked one-to-one to a Student portal account. The account keeps the email, sign-in name, role,
/// and credential state; the profile holds the shared personal and contact details from the paper admission form, separate mailing and
/// permanent addresses (free-text details plus an Upazila, whose District and Division are derived), and the prior academic history.
/// One profile has one admission per program enrollment (<see cref="StudentAdmissionRecord"/>). A profile is never deleted.
/// </summary>
public sealed class StudentProfile : AuditedEntity
{
    public const int MaxTextLength = 200;
    public const int MaxAddressLength = 500;
    public const int MaxPhoneLength = 50;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string UserId { get; private set; } = string.Empty;

    public string ApplicantName { get; set; } = string.Empty;

    public string? FatherName { get; set; }

    public string? MotherName { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? PlaceOfBirth { get; set; }

    public string? MaritalStatus { get; set; }

    public string? Nationality { get; set; }

    /// <summary>Required contact number for the applicant. Retained for contact only; it is not verified.</summary>
    public string ApplicantMobile { get; set; } = string.Empty;

    public string? MailingAddressDetails { get; set; }

    public Guid? MailingUpazilaId { get; set; }

    public string? MailingCellNumber { get; set; }

    public string? PermanentAddressDetails { get; set; }

    public Guid? PermanentUpazilaId { get; set; }

    public string? PermanentCellNumber { get; set; }

    public string? GuardianName { get; set; }

    public string? GuardianRelationship { get; set; }

    public string? GuardianAddress { get; set; }

    public string? GuardianCellNumber { get; set; }

    /// <summary>Prior education shared by every admission of this person, in the order it was entered.</summary>
    public List<StudentAcademicHistory> AcademicHistory { get; private set; } = [];

    public static StudentProfile Create(string userId) => new() { UserId = userId };
}
