using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Students;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>A mailing or permanent address: free-text details and an Upazila. District and Division are derived, never submitted.</summary>
public sealed class StudentAddressRequest
{
    public string? Details { get; init; }

    public Guid? UpazilaId { get; init; }

    public string? CellNumber { get; init; }

    public StudentAddressInput ToInput() => new(Details, UpazilaId, CellNumber);
}

/// <summary>
/// One prior-education row. <c>category</c> is SscOLevel, HscALevel, BachelorGraduation, Master, Pgdit, or Other; every other value is
/// optional text kept exactly as written on the paper form. Send <c>id</c> to keep an existing row.
/// </summary>
public sealed class AcademicHistoryRequest
{
    public Guid? Id { get; init; }

    public string? Category { get; init; }

    public string? Examination { get; init; }

    public string? SchoolOrCollege { get; init; }

    public string? Board { get; init; }

    public string? Group { get; init; }

    public string? Department { get; init; }

    public string? University { get; init; }

    public string? Subject { get; init; }

    public string? Institution { get; init; }

    public string? Year { get; init; }

    public string? MarksObtained { get; init; }

    public string? MarksPercentage { get; init; }

    public string? Cgpa { get; init; }

    public string? Class { get; init; }

    public string? Division { get; init; }

    public AcademicHistoryInput ToInput() => new(
        Id, Category, Examination, SchoolOrCollege, Board, Group, Department, University, Subject, Institution, Year, MarksObtained,
        MarksPercentage, Cgpa, Class, Division);
}

/// <summary>
/// The admission form, used to create an admission (<see cref="CreateStudentAdmissionRequest"/>) or replace one. There is no password,
/// setup proof, attachment, or signature field; unknown fields are rejected.
/// </summary>
public class StudentAdmissionRequest
{
    public string? ApplicantName { get; init; }

    public string? FatherName { get; init; }

    public string? MotherName { get; init; }

    public DateOnly? DateOfBirth { get; init; }

    public string? PlaceOfBirth { get; init; }

    public string? MaritalStatus { get; init; }

    public string? Nationality { get; init; }

    public string? ApplicantMobile { get; init; }

    public StudentAddressRequest? MailingAddress { get; init; }

    public StudentAddressRequest? PermanentAddress { get; init; }

    public string? GuardianName { get; init; }

    public string? GuardianRelationship { get; init; }

    public string? GuardianAddress { get; init; }

    public string? GuardianCellNumber { get; init; }

    public Guid? DegreeId { get; init; }

    public Guid? BatchId { get; init; }

    public string? AdmissionSerialNumber { get; init; }

    public string? AdmissionExamRoll { get; init; }

    public string? ClassRoll { get; init; }

    public Guid? AssignedHallId { get; init; }

    public string? RegistrationNumber { get; init; }

    public AcademicHistoryRequest[]? AcademicHistory { get; init; }

    public StudentAdmissionInput ToInput(string? email = null) => new(
        email, ApplicantName, FatherName, MotherName, DateOfBirth, PlaceOfBirth, MaritalStatus, Nationality, ApplicantMobile,
        MailingAddress?.ToInput(), PermanentAddress?.ToInput(), GuardianName, GuardianRelationship, GuardianAddress, GuardianCellNumber,
        DegreeId, BatchId, AdmissionSerialNumber, AdmissionExamRoll, ClassRoll, AssignedHallId, RegistrationNumber,
        AcademicHistory?.Select(h => h.ToInput()).ToList());
}

/// <summary>Creates an admission. The email finds or creates the Student account; it cannot be changed through Student Management.</summary>
public sealed class CreateStudentAdmissionRequest : StudentAdmissionRequest
{
    public string? Email { get; init; }

    public StudentAdmissionInput ToCreateInput() => ToInput(Email);
}

public sealed class StudentQueryString
{
    /// <summary>Part of the applicant name, email, mobile, admission exam roll, class roll, or registration number.</summary>
    public string? Search { get; init; }

    public Guid? DegreeId { get; init; }

    public Guid? BatchId { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public StudentQuery ToQuery() => new(Search, DegreeId, BatchId, Page, PageSize);
}

public sealed record StudentAddressResponse(
    string? Details,
    string? CellNumber,
    Guid? UpazilaId,
    string? UpazilaName,
    Guid? DistrictId,
    string? DistrictName,
    Guid? DivisionId,
    string? DivisionName)
{
    public static StudentAddressResponse? From(StudentAddress? a) =>
        a is null ? null : new(a.Details, a.CellNumber, a.UpazilaId, a.UpazilaName, a.DistrictId, a.DistrictName, a.DivisionId, a.DivisionName);
}

public sealed record AcademicHistoryResponse(
    Guid Id,
    string Category,
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
    string? Division)
{
    public static AcademicHistoryResponse From(AcademicHistoryEntry h) => new(
        h.Id, h.Category.ToString(), h.Examination, h.SchoolOrCollege, h.Board, h.Group, h.Department, h.University, h.Subject,
        h.Institution, h.Year, h.MarksObtained, h.MarksPercentage, h.Cgpa, h.Class, h.Division);
}

public sealed record StudentAdmissionSummaryResponse(
    Guid Id, Guid DegreeId, string? DegreeName, Guid BatchId, string? BatchName, string? RegistrationNumber, DateTime CreatedAtUtc)
{
    public static StudentAdmissionSummaryResponse From(StudentAdmissionSummary a) =>
        new(a.Id, a.DegreeId, a.DegreeName, a.BatchId, a.BatchName, a.RegistrationNumber, a.CreatedAtUtc);
}

/// <summary>The shared Student profile. The email and status are the account's; no password, proof, or link is ever included.</summary>
public sealed record StudentProfileResponse(
    Guid Id,
    string UserId,
    string Email,
    string AccountStatus,
    string ApplicantName,
    string? FatherName,
    string? MotherName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? MaritalStatus,
    string? Nationality,
    string ApplicantMobile,
    StudentAddressResponse? MailingAddress,
    StudentAddressResponse? PermanentAddress,
    string? GuardianName,
    string? GuardianRelationship,
    string? GuardianAddress,
    string? GuardianCellNumber,
    IReadOnlyList<AcademicHistoryResponse> AcademicHistory,
    IReadOnlyList<StudentAdmissionSummaryResponse> Admissions)
{
    public static StudentProfileResponse From(StudentProfileDetail p) => new(
        p.Id, p.UserId, p.Email, p.AccountStatus.ToString(), p.ApplicantName, p.FatherName, p.MotherName, p.DateOfBirth, p.PlaceOfBirth,
        p.MaritalStatus, p.Nationality, p.ApplicantMobile, StudentAddressResponse.From(p.MailingAddress),
        StudentAddressResponse.From(p.PermanentAddress), p.GuardianName, p.GuardianRelationship, p.GuardianAddress, p.GuardianCellNumber,
        p.AcademicHistory.Select(AcademicHistoryResponse.From).ToList(), p.Admissions.Select(StudentAdmissionSummaryResponse.From).ToList());
}

/// <summary>One admission with its shared profile and the profile's other admissions (each a separate program).</summary>
public sealed record StudentAdmissionResponse(
    Guid Id,
    StudentProfileResponse Profile,
    IReadOnlyList<StudentAdmissionSummaryResponse> OtherAdmissions,
    Guid DegreeId,
    string? DegreeName,
    Guid BatchId,
    string? BatchName,
    string AdmissionSerialNumber,
    string AdmissionExamRoll,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClassRoll,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AssignedHallId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssignedHallName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationNumber,
    DateTime CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    DateTime LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy)
{
    public static StudentAdmissionResponse From(StudentAdmissionDetail d) => new(
        d.Id, StudentProfileResponse.From(d.Profile),
        d.Profile.Admissions.Where(a => a.Id != d.Id).Select(StudentAdmissionSummaryResponse.From).ToList(),
        d.DegreeId, d.DegreeName, d.BatchId, d.BatchName, d.AdmissionSerialNumber, d.AdmissionExamRoll, d.ClassRoll, d.AssignedHallId,
        d.AssignedHallName, d.RegistrationNumber, d.CreatedAtUtc, d.CreatedByUserId, d.CreatedBy, d.LastModifiedAtUtc,
        d.LastModifiedByUserId, d.LastModifiedBy);
}

/// <summary>
/// The result of creating an admission: the admission, whether a new Student account and profile were created, and whether the common
/// setup email was requested (only for a new account). It never contains the setup link or proof.
/// </summary>
public sealed record StudentAdmissionCreatedResponse(
    StudentAdmissionResponse Admission, bool AccountCreated, bool ProfileCreated, bool SetupEmailRequested)
{
    public static StudentAdmissionCreatedResponse From(StudentAdmissionCreated c) =>
        new(StudentAdmissionResponse.From(c.Admission), c.AccountCreated, c.ProfileCreated, c.SetupEmailRequested);
}

public sealed record StudentAdmissionListItemResponse(
    Guid Id,
    Guid StudentProfileId,
    string UserId,
    string ApplicantName,
    string Email,
    string AccountStatus,
    Guid DegreeId,
    string? DegreeName,
    Guid BatchId,
    string? BatchName,
    string AdmissionExamRoll,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ClassRoll,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AssignedHallName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RegistrationNumber)
{
    public static StudentAdmissionListItemResponse From(StudentAdmissionListItem i) => new(
        i.Id, i.StudentProfileId, i.UserId, i.ApplicantName, i.Email, i.AccountStatus.ToString(), i.DegreeId, i.DegreeName, i.BatchId,
        i.BatchName, i.AdmissionExamRoll, i.ClassRoll, i.AssignedHallName, i.RegistrationNumber);
}

public sealed record StudentAdmissionPageResponse(IReadOnlyList<StudentAdmissionListItemResponse> Items, int Page, long TotalCount, int TotalPages)
{
    public static StudentAdmissionPageResponse From(StudentListPage page) =>
        new(page.Items.Select(StudentAdmissionListItemResponse.From).ToList(), page.Page, page.TotalCount, page.TotalPages);
}

/// <summary>
/// What an admission for an email would do: <c>New</c> (a new account and profile), <c>ExistingStudent</c> (reuse; the profile is
/// included when it exists, to prefill the form), or <c>NotStudentAccount</c> (an admission would be refused).
/// </summary>
public sealed record StudentLookupResponse(string Status, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] StudentProfileResponse? Profile)
{
    public static StudentLookupResponse From(StudentLookup l) =>
        new(l.Status.ToString(), l.Profile is null ? null : StudentProfileResponse.From(l.Profile));
}

public sealed record EligibilityRequirementResponse(string Requirement, string Status, string Message);

/// <summary>
/// The computed graduation eligibility of one admission. Credit totals are omitted when their source could not supply them; an
/// unavailable source is never eligibility.
/// </summary>
public sealed record GraduationEligibilityResponse(
    Guid AdmissionId,
    bool Eligible,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? RequiredCredits,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CompletedCredits,
    IReadOnlyList<EligibilityRequirementResponse> Requirements)
{
    public static GraduationEligibilityResponse From(GraduationEligibility e) => new(
        e.AdmissionId, e.Eligible, e.RequiredCredits, e.CompletedCredits,
        e.Requirements.Select(r => new EligibilityRequirementResponse(r.Requirement.ToString(), r.Status.ToString(), r.Message)).ToList());
}
