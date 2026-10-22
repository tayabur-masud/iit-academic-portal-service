using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Staff;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>
/// Creates a staff member from an email. A new email gets a pending Admin account and the profile; the email of an existing Admin account
/// that is not deactivated and has no profile gets the profile only. <c>manualVerificationConfirmed</c> must be true. Unknown fields (a
/// user id, a password, a role, a verified flag) are rejected.
/// </summary>
public sealed class CreateStaffProfileRequest
{
    public string? Email { get; init; }

    public string? Name { get; init; }

    public string? MobileNumber { get; init; }

    public Guid? DesignationId { get; init; }

    public bool? ManualVerificationConfirmed { get; init; }

    public StaffInput ToInput() => new(Email, Name, MobileNumber, DesignationId, ManualVerificationConfirmed);
}

/// <summary>Replaces a staff profile's Name, Mobile number, and Designation. The account and its email cannot be changed here.</summary>
public sealed class UpdateStaffProfileRequest
{
    public string? Name { get; init; }

    public string? MobileNumber { get; init; }

    public Guid? DesignationId { get; init; }

    public bool? ManualVerificationConfirmed { get; init; }

    public StaffInput ToInput() => new(null, Name, MobileNumber, DesignationId, ManualVerificationConfirmed);
}

public sealed class StaffQueryString
{
    /// <summary>Part of a name, account email, mobile number, or Designation name.</summary>
    public string? Search { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public StaffQuery ToQuery() => new(Search, Page, PageSize);
}

/// <summary>
/// A staff profile with its account's email, status, and whether it still holds the Admin role. Carries no credential, proof, document,
/// or verified state; the verification confirmation of each change is in the audit history. <c>accountCreated</c> and
/// <c>setupEmailRequested</c> appear only in the response to a create.
/// </summary>
public sealed record StaffProfileResponse(
    Guid Id,
    string UserId,
    string Email,
    string AccountStatus,
    bool HasAdminRole,
    string Name,
    string MobileNumber,
    Guid DesignationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DesignationName,
    bool IsActive,
    DateTime CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    DateTime LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AccountCreated,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? SetupEmailRequested)
{
    public static StaffProfileResponse From(StaffDetail d) => new(
        d.Id, d.UserId, d.Email, d.AccountStatus.ToString(), d.HasAdminRole, d.Name, d.MobileNumber, d.DesignationId, d.DesignationName,
        d.IsActive, d.CreatedAtUtc, d.CreatedByUserId, d.CreatedBy, d.LastModifiedAtUtc, d.LastModifiedByUserId, d.LastModifiedBy,
        d.AccountCreated, d.SetupEmailRequested);
}

public sealed record StaffPageResponse(IReadOnlyList<StaffProfileResponse> Items, int Page, long TotalCount, int TotalPages)
{
    public static StaffPageResponse From(StaffListPage page) =>
        new(page.Items.Select(StaffProfileResponse.From).ToList(), page.Page, page.TotalCount, page.TotalPages);
}
