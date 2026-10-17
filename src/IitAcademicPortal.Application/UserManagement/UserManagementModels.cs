using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.UserManagement;

/// <summary>
/// A portal account as an Admin sees it: identity and access only. Never contains a password, hash, security stamp, setup proof, or
/// setup link. <see cref="ConcurrencyStamp"/> is Identity's optimistic-concurrency value (not a credential), echoed back on an email
/// change so a stale edit is refused. <see cref="TeacherId"/> links to Teacher Management when the account has a teacher profile.
/// The created/last-modified facts come from the teacher profile, or else from the account's own audit history, and are null for an
/// account that has neither (for example the bootstrap Admin).
/// </summary>
public sealed record UserAccountDetail(
    string UserId,
    string Email,
    TeacherAccountStatus AccountStatus,
    IReadOnlyList<string> Roles,
    string? DefaultRole,
    Guid? TeacherId,
    string ConcurrencyStamp,
    DateTime? CreatedAtUtc = null,
    string? CreatedByUserId = null,
    string? CreatedBy = null,
    DateTime? LastModifiedAtUtc = null,
    string? LastModifiedByUserId = null,
    string? LastModifiedBy = null);

/// <summary>A directory filter. The search matches the normalized email (contains) or the exact user ID.</summary>
public sealed record UserListFilter(string? Search, string? Role, TeacherAccountStatus? Status);

public sealed record UserListPage(IReadOnlyList<UserAccountDetail> Items, int Page, long TotalCount, int TotalPages);

public sealed record UserQuery(string? Search, string? Role, string? Status, int? Page, int? PageSize);

/// <summary>A generic account to provision. <see cref="AccountType"/> is Admin or Student; other account types have their own owners.</summary>
public sealed record CreateAccountInput(string? Email, string? AccountType);

public sealed record AccountEmailInput(string? Email, string? ExpectedConcurrencyStamp);

public sealed record AccountRolesInput(IReadOnlyList<string>? Roles, string? DefaultRole);

public enum AccountOutcome
{
    Ok,
    Created,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of an account operation: a value, field-keyed errors, or a conflict explanation.</summary>
public sealed record AccountResult<T>(
    AccountOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is AccountOutcome.Ok or AccountOutcome.Created;

    public static AccountResult<T> NotFound { get; } = new(AccountOutcome.NotFound);

    public static AccountResult<T> Invalid(IReadOnlyDictionary<string, string[]> errors) => new(AccountOutcome.Invalid, Errors: errors);

    public static AccountResult<T> Invalid(string field, string message) => Invalid(new Dictionary<string, string[]> { [field] = [message] });

    public static AccountResult<T> Conflict(string message) => new(AccountOutcome.Conflict, Message: message);
}
