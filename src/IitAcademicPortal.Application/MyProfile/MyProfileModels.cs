namespace IitAcademicPortal.Application.MyProfile;

/// <summary>
/// The signed-in account's own view for its active role: the account email, the active role, and the one profile that role reads
/// (null when the account has none). Allowlisted on purpose: no account, profile, history, or catalog identifier, no account status,
/// roles, audit facts, admissions, results, or credential data.
/// </summary>
public sealed record MyProfileView(string Email, string ActiveRole, MyRoleProfile? Profile);

/// <summary>One role's profile as its owner sees it. The concrete type says which profile it is.</summary>
public abstract record MyRoleProfile;

/// <summary>An Admin's Staff Management profile.</summary>
public sealed record MyStaffProfile(string Name, string MobileNumber, string? DesignationName) : MyRoleProfile;

/// <summary>A Teacher's profile, also shown to the same account acting as Coordinator.</summary>
public sealed record MyTeacherProfile(string DisplayName, string? EmployeeStaffId, string? Phone, string? DesignationName) : MyRoleProfile;

/// <summary>A Student's personal profile with its addresses, guardian, and prior education. Admissions and results are not part of it.</summary>
public sealed record MyStudentProfile(
    string ApplicantName,
    string? FatherName,
    string? MotherName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? MaritalStatus,
    string? Nationality,
    string ApplicantMobile,
    MyAddress? MailingAddress,
    MyAddress? PermanentAddress,
    string? GuardianName,
    string? GuardianRelationship,
    string? GuardianAddress,
    string? GuardianCellNumber,
    IReadOnlyList<MyAcademicHistoryEntry> AcademicHistory) : MyRoleProfile;

/// <summary>An address with its locality path as names; one of Upazila and City Corporation is null.</summary>
public sealed record MyAddress(
    string? Details, string? CellNumber, string? UpazilaName, string? CityCorporationName, string? DistrictName, string? DivisionName);

/// <summary>One prior-education row, in the order it was entered, with names rather than catalog references.</summary>
public sealed record MyAcademicHistoryEntry(
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
    string? Division);

/// <summary>The account's email and the name on each of its profiles; a name is null when the account has no such profile.</summary>
public sealed record AccountNames(string? Email, string? StaffName, string? TeacherName, string? StudentName);

public enum MyProfileOutcome
{
    Ok,

    /// <summary>The session has no supported active role.</summary>
    Forbidden,

    /// <summary>The signed-in account no longer resolves.</summary>
    NotFound,
}

public sealed record MyProfileResult(MyProfileOutcome Outcome, MyProfileView? View = null);
