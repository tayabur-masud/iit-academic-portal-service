using System.Text.Json.Serialization;
using IitAcademicPortal.Application.UserManagement;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>
/// Provisions a generic account. <c>accountType</c> is Admin or Student; Teacher is added through Teacher Management and Coordinator
/// comes from a batch-coordinator assignment. There is no password field: the account holder sets it through the setup email.
/// </summary>
public sealed class CreateUserAccountRequest
{
    public string? Email { get; init; }

    public string? AccountType { get; init; }

    public CreateAccountInput ToInput() => new(Email, AccountType);
}

/// <summary>Changes the registered email. <c>expectedConcurrencyStamp</c> is the value last read, so a stale edit is refused.</summary>
public sealed class UpdateUserEmailRequest
{
    public string? Email { get; init; }

    public string? ExpectedConcurrencyStamp { get; init; }

    public AccountEmailInput ToInput() => new(Email, ExpectedConcurrencyStamp);
}

/// <summary>The roles an account should hold and the default among them. Coordinator is never accepted; it follows assignments.</summary>
public sealed class UpdateUserRolesRequest
{
    public string[]? Roles { get; init; }

    public string? DefaultRole { get; init; }

    public AccountRolesInput ToInput() => new(Roles, DefaultRole);
}

public sealed class UserQueryString
{
    /// <summary>A registered email (or part of one) or an exact user ID.</summary>
    public string? Search { get; init; }

    /// <summary>Admin, Student, Teacher, or Coordinator; absent means every role.</summary>
    public string? Role { get; init; }

    /// <summary>Active, PendingCredentialSetup, or Deactivated; absent means every status.</summary>
    public string? Status { get; init; }

    public int? Page { get; init; }

    public int? PageSize { get; init; }

    public UserQuery ToQuery() => new(Search, Role, Status, Page, PageSize);
}

/// <summary>
/// A portal account. Carries no password, hash, security stamp, setup proof, or setup link. <c>concurrencyStamp</c> is Identity's
/// optimistic-concurrency value, not a credential. <c>teacherId</c> is present when Teacher Management owns the account's profile.
/// </summary>
public sealed record UserAccountResponse(
    string UserId,
    string Email,
    string AccountStatus,
    IReadOnlyList<string> Roles,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DefaultRole,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TeacherId,
    string ConcurrencyStamp,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTime? LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy)
{
    public static UserAccountResponse From(UserAccountDetail d) => new(
        d.UserId, d.Email, d.AccountStatus.ToString(), d.Roles, d.DefaultRole, d.TeacherId, d.ConcurrencyStamp,
        d.CreatedAtUtc, d.CreatedByUserId, d.CreatedBy, d.LastModifiedAtUtc, d.LastModifiedByUserId, d.LastModifiedBy);
}

public sealed record UserAccountPageResponse(IReadOnlyList<UserAccountResponse> Items, int Page, long TotalCount, int TotalPages)
{
    public static UserAccountPageResponse From(UserListPage page) =>
        new(page.Items.Select(UserAccountResponse.From).ToList(), page.Page, page.TotalCount, page.TotalPages);
}

/// <summary>
/// The result of an Admin setup-email reissue: the account and whether the fresh email was durably requested. It never carries the
/// link or proof; delivery progress is visible in notification history.
/// </summary>
public sealed record CredentialSetupReissueResponse(UserAccountResponse Account, bool SetupEmailRequested, string Message);
