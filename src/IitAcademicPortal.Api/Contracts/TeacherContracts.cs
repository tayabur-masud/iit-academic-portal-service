using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Teachers;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>Adds a teacher. A new teacher is always active, so there is no active flag; unknown fields are rejected.</summary>
public sealed class CreateTeacherRequest
{
    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public string? EmployeeStaffId { get; init; }

    public string? Phone { get; init; }

    public Guid? DesignationId { get; init; }

    public TeacherInput ToInput() => new(DisplayName, Email, EmployeeStaffId, Phone, DesignationId);
}

/// <summary>Replaces the editable profile fields. Setting <c>isActive</c> to false deactivates the teacher.</summary>
public sealed class UpdateTeacherRequest
{
    public string? DisplayName { get; init; }

    public string? Email { get; init; }

    public string? EmployeeStaffId { get; init; }

    public string? Phone { get; init; }

    public Guid? DesignationId { get; init; }

    public bool? IsActive { get; init; }

    public TeacherInput ToInput() => new(DisplayName, Email, EmployeeStaffId, Phone, DesignationId, IsActive);
}

/// <summary>The editable roles (Teacher, and optionally Admin) and the default role among all assigned roles.</summary>
public sealed class TeacherRolesRequest
{
    public string[]? Roles { get; init; }

    public string? DefaultRole { get; init; }

    public TeacherRolesInput ToInput() => new(Roles, DefaultRole);
}

public sealed class TeacherQueryString
{
    public string? Search { get; init; }

    /// <summary>Active, PendingCredentialSetup, or Deactivated; absent means every teacher.</summary>
    public string? Status { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public TeacherQuery ToQuery() => new(Search, Status, Page, PageSize);
}

/// <summary>A teacher. Carries no password, hash, token, or other secret.</summary>
public sealed record TeacherResponse(
    Guid Id,
    string UserId,
    string Email,
    string DisplayName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? EmployeeStaffId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Phone,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DesignationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DesignationName,
    bool IsActive,
    string AccountStatus,
    IReadOnlyList<string> Roles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DefaultRole,
    DateTime CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    DateTime LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy)
{
    public static TeacherResponse From(TeacherDetail d) => new(
        d.Id, d.UserId, d.Email, d.DisplayName, d.EmployeeStaffId, d.Phone, d.DesignationId, d.DesignationName, d.IsActive,
        d.AccountStatus.ToString(), d.Roles, d.DefaultRole, d.CreatedAtUtc, d.CreatedByUserId, d.CreatedBy,
        d.LastModifiedAtUtc, d.LastModifiedByUserId, d.LastModifiedBy);
}

public sealed record TeacherPageResponse(IReadOnlyList<TeacherResponse> Items, int Page, long TotalCount, int TotalPages)
{
    public static TeacherPageResponse From(TeacherListPage page) =>
        new(page.Items.Select(TeacherResponse.From).ToList(), page.Page, page.TotalCount, page.TotalPages);
}
