using System.Text.Json.Serialization;
using IitAcademicPortal.Application.MyProfile;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>
/// The signed-in account's My Profile: its email, the session's active role, and the profile that role reads, or null when the account
/// has none. Built field by field from <see cref="MyProfileView"/>, never from a management response, so no identifier, account status,
/// role list, audit fact, admission, result, or credential can appear.
/// </summary>
public sealed record MyProfileResponse(string Email, string ActiveRole, MyRoleProfileResponse? Profile)
{
    public static MyProfileResponse From(MyProfileView view) => new(view.Email, view.ActiveRole, view.Profile switch
    {
        MyStaffProfile staff => new MyStaffProfileResponse(staff.Name, staff.MobileNumber, staff.DesignationName),
        MyTeacherProfile teacher => new MyTeacherProfileResponse(teacher.DisplayName, teacher.EmployeeStaffId, teacher.Phone, teacher.DesignationName),
        MyStudentProfile student => MyStudentProfileResponse.From(student),
        _ => null,
    });
}

/// <summary>One role's profile; <c>kind</c> is <c>staff</c> (Admin), <c>teacher</c> (Teacher and Coordinator), or <c>student</c>.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(MyStaffProfileResponse), "staff")]
[JsonDerivedType(typeof(MyTeacherProfileResponse), "teacher")]
[JsonDerivedType(typeof(MyStudentProfileResponse), "student")]
public abstract record MyRoleProfileResponse;

public sealed record MyStaffProfileResponse(string Name, string MobileNumber, string? DesignationName) : MyRoleProfileResponse;

public sealed record MyTeacherProfileResponse(string DisplayName, string? EmployeeStaffId, string? Phone, string? DesignationName)
    : MyRoleProfileResponse;

public sealed record MyStudentProfileResponse(
    string ApplicantName,
    string? FatherName,
    string? MotherName,
    DateOnly? DateOfBirth,
    string? PlaceOfBirth,
    string? MaritalStatus,
    string? Nationality,
    string ApplicantMobile,
    MyAddressResponse? MailingAddress,
    MyAddressResponse? PermanentAddress,
    string? GuardianName,
    string? GuardianRelationship,
    string? GuardianAddress,
    string? GuardianCellNumber,
    IReadOnlyList<MyAcademicHistoryResponse> AcademicHistory) : MyRoleProfileResponse
{
    public static MyStudentProfileResponse From(MyStudentProfile p) => new(
        p.ApplicantName, p.FatherName, p.MotherName, p.DateOfBirth, p.PlaceOfBirth, p.MaritalStatus, p.Nationality, p.ApplicantMobile,
        Address(p.MailingAddress), Address(p.PermanentAddress), p.GuardianName, p.GuardianRelationship, p.GuardianAddress, p.GuardianCellNumber,
        p.AcademicHistory.Select(h => new MyAcademicHistoryResponse(
            h.Category, h.Examination, h.SchoolOrCollege, h.Board, h.Group, h.Department, h.University, h.Subject, h.Institution, h.Year,
            h.MarksObtained, h.MarksPercentage, h.Cgpa, h.Class, h.Division)).ToList());

    private static MyAddressResponse? Address(MyAddress? a) => a is null
        ? null
        : new MyAddressResponse(a.Details, a.CellNumber, a.UpazilaName, a.CityCorporationName, a.DistrictName, a.DivisionName);
}

public sealed record MyAddressResponse(
    string? Details, string? CellNumber, string? UpazilaName, string? CityCorporationName, string? DistrictName, string? DivisionName);

public sealed record MyAcademicHistoryResponse(
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
