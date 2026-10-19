using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Students;

namespace IitAcademicPortal.Application.Students;

/// <summary>A mailing or permanent address as entered: free-text details, an Upazila, and an optional cell number.</summary>
public sealed record StudentAddressInput(string? Details, Guid? UpazilaId, string? CellNumber);

/// <summary>
/// One prior-education row as entered. <see cref="Id"/> identifies an existing row to keep; absent means a new row. The catalog IDs are
/// set when the school or college, university, institution, board, or group was chosen from Configuration; otherwise the typed name is
/// kept on its own.
/// </summary>
public sealed record AcademicHistoryInput(
    Guid? Id,
    string? Category,
    string? Examination,
    string? SchoolOrCollege,
    string? Board,
    string? Group,
    string? Department,
    string? University,
    string? Subject,
    string? Institution,
    string? Year,
    string? MarksObtained,
    string? MarksPercentage,
    string? Cgpa,
    string? Class,
    string? Division,
    Guid? SchoolOrCollegeId = null,
    Guid? UniversityId = null,
    Guid? InstitutionId = null,
    Guid? BoardId = null,
    Guid? GroupId = null);

/// <summary>
/// The writable fields of an admission form: the shared profile, its academic history, and this admission. <see cref="Email"/> is used
/// only when creating an admission (it finds or creates the Student account); the email of an existing account is changed in User
/// Management. The history list is the complete new list: rows left out are removed.
/// </summary>
public sealed record StudentAdmissionInput(
    string? Email,
    string? ApplicantName,
    string? FatherName,
    string? MotherName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? MaritalStatus,
    string? Nationality,
    string? ApplicantMobile,
    StudentAddressInput? MailingAddress,
    StudentAddressInput? PermanentAddress,
    string? GuardianName,
    string? GuardianRelationship,
    string? GuardianAddress,
    string? GuardianCellNumber,
    Guid? DegreeId,
    Guid? BatchId,
    string? AdmissionSerialNumber,
    string? AdmissionExamRoll,
    string? ClassRoll,
    Guid? AssignedHallId,
    string? RegistrationNumber,
    IReadOnlyList<AcademicHistoryInput>? AcademicHistory);

/// <summary>An address with its geographic path resolved from the Upazila, so District and Division can never disagree with it.</summary>
public sealed record StudentAddress(
    string? Details,
    string? CellNumber,
    Guid? UpazilaId,
    string? UpazilaName,
    Guid? DistrictId,
    string? DistrictName,
    Guid? DivisionId,
    string? DivisionName);

public sealed record AcademicHistoryEntry(
    Guid Id,
    AcademicHistoryCategory Category,
    string? Examination,
    string? SchoolOrCollege,
    string? Board,
    string? Group,
    string? Department,
    string? University,
    string? Subject,
    string? Institution,
    string? Year,
    string? MarksObtained,
    string? MarksPercentage,
    string? Cgpa,
    string? Class,
    string? Division,
    Guid? SchoolOrCollegeId = null,
    Guid? UniversityId = null,
    Guid? InstitutionId = null,
    Guid? BoardId = null,
    Guid? GroupId = null);

/// <summary>One admission of a Student, as listed on the profile (each program stays a separate admission).</summary>
public sealed record StudentAdmissionSummary(
    Guid Id, Guid DegreeId, string? DegreeName, Guid BatchId, string? BatchName, string? RegistrationNumber, DateTime CreatedAtUtc);

/// <summary>
/// The shared Student profile with its account state, academic history, and every admission. Never contains a password, setup proof,
/// or setup link; the email is the account's.
/// </summary>
public sealed record StudentProfileDetail(
    Guid Id,
    string UserId,
    string Email,
    TeacherAccountStatus AccountStatus,
    string ApplicantName,
    string? FatherName,
    string? MotherName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? MaritalStatus,
    string? Nationality,
    string ApplicantMobile,
    StudentAddress? MailingAddress,
    StudentAddress? PermanentAddress,
    string? GuardianName,
    string? GuardianRelationship,
    string? GuardianAddress,
    string? GuardianCellNumber,
    IReadOnlyList<AcademicHistoryEntry> AcademicHistory,
    IReadOnlyList<StudentAdmissionSummary> Admissions);

/// <summary>One admission with its shared profile. The created/last-modified facts are the admission's own.</summary>
public sealed record StudentAdmissionDetail(
    Guid Id,
    StudentProfileDetail Profile,
    Guid DegreeId,
    string? DegreeName,
    Guid BatchId,
    string? BatchName,
    string AdmissionSerialNumber,
    string AdmissionExamRoll,
    string? ClassRoll,
    Guid? AssignedHallId,
    string? AssignedHallName,
    string? RegistrationNumber,
    DateTime CreatedAtUtc,
    string? CreatedByUserId,
    string? CreatedBy,
    DateTime LastModifiedAtUtc,
    string? LastModifiedByUserId,
    string? LastModifiedBy);

/// <summary>A directory row: one admission and the safe parts of its Student identity.</summary>
public sealed record StudentAdmissionListItem(
    Guid Id,
    Guid StudentProfileId,
    string UserId,
    string ApplicantName,
    string Email,
    TeacherAccountStatus AccountStatus,
    Guid DegreeId,
    string? DegreeName,
    Guid BatchId,
    string? BatchName,
    string AdmissionExamRoll,
    string? ClassRoll,
    string? AssignedHallName,
    string? RegistrationNumber);

/// <summary>A directory filter. The search matches the applicant name, email, mobile, exam roll, class roll, or registration number.</summary>
public sealed record StudentListFilter(string? Search, Guid? DegreeId, Guid? BatchId);

public sealed record StudentListPage(IReadOnlyList<StudentAdmissionListItem> Items, int Page, long TotalCount, int TotalPages);

public sealed record StudentQuery(string? Search, Guid? DegreeId, Guid? BatchId, int? Page, int? PageSize);

/// <summary>What creating an admission did to the account: whether it was new and whether its setup email was requested.</summary>
public sealed record StudentAdmissionCreated(StudentAdmissionDetail Admission, bool AccountCreated, bool ProfileCreated, bool SetupEmailRequested);

public enum StudentLookupStatus
{
    /// <summary>No account uses the email: a new Student account and profile will be created.</summary>
    New,

    /// <summary>A Student account uses the email; it has a profile (returned) or will get one.</summary>
    ExistingStudent,

    /// <summary>The email belongs to an account that is not a Student account; an admission would be refused.</summary>
    NotStudentAccount,
}

/// <summary>What the admission form needs to know about an email before it is submitted.</summary>
public sealed record StudentLookup(StudentLookupStatus Status, StudentProfileDetail? Profile);

public enum StudentOutcome
{
    Ok,
    Created,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of a Student Management operation: a value, field-keyed errors, or a conflict explanation.</summary>
public sealed record StudentResult<T>(
    StudentOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is StudentOutcome.Ok or StudentOutcome.Created;

    public static StudentResult<T> NotFound { get; } = new(StudentOutcome.NotFound);

    public static StudentResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(StudentOutcome.Invalid, Errors: errors);

    public static StudentResult<T> Conflict(string message) => new(StudentOutcome.Conflict, Message: message);
}
