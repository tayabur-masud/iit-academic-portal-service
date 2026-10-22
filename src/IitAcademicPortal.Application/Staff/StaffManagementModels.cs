using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.Staff;

/// <summary>
/// The writable staff profile fields. <see cref="Email"/> is used only when creating: it names the account to create or link.
/// <see cref="ManualVerificationConfirmed"/> is the saving Admin's attestation for this change; it is recorded with the audit event,
/// never stored on the profile.
/// </summary>
public sealed record StaffInput(
    string? Email,
    string? Name,
    string? MobileNumber,
    Guid? DesignationId,
    bool? ManualVerificationConfirmed);

/// <summary>
/// A staff profile as an Admin sees it, with its account's email and standing. Never contains a credential, proof, or document.
/// <see cref="AccountCreated"/> and <see cref="SetupEmailRequested"/> are set only on the result of a create.
/// </summary>
public sealed record StaffDetail(
    Guid Id,
    string UserId,
    string Email,
    TeacherAccountStatus AccountStatus,
    bool HasAdminRole,
    string Name,
    string MobileNumber,
    Guid DesignationId,
    string? DesignationName,
    bool IsActive,
    DateTime CreatedAtUtc,
    string? CreatedByUserId,
    string? CreatedBy,
    DateTime LastModifiedAtUtc,
    string? LastModifiedByUserId,
    string? LastModifiedBy,
    bool? AccountCreated = null,
    bool? SetupEmailRequested = null);

/// <summary>The account a profile is for, as Staff Management needs to see it. Owned by User Management.</summary>
public sealed record StaffAccount(string UserId, TeacherAccountStatus Status, bool IsAdmin);

public sealed record StaffQuery(string? Search, int? Page, int? PageSize);

public sealed record StaffListPage(IReadOnlyList<StaffDetail> Items, int Page, long TotalCount, int TotalPages);

public enum StaffOutcome
{
    Ok,
    Created,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of a staff operation: a value, field-keyed errors, or a conflict explanation.</summary>
public sealed record StaffResult<T>(
    StaffOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public static StaffResult<T> NotFound(string message) => new(StaffOutcome.NotFound, Message: message);

    public static StaffResult<T> Invalid(StaffErrors errors) => new(StaffOutcome.Invalid, Errors: errors.ToDictionary());

    public static StaffResult<T> Conflict(string message) => new(StaffOutcome.Conflict, Message: message);
}

/// <summary>Field-keyed validation messages, collected before anything is written.</summary>
public sealed class StaffErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool Any => errors.Count > 0;

    public bool Has(string field) => errors.ContainsKey(field);

    public void Add(string field, string message)
    {
        if (!errors.TryGetValue(field, out var list))
        {
            errors[field] = list = [];
        }

        if (!list.Contains(message))
        {
            list.Add(message);
        }
    }

    public IReadOnlyDictionary<string, string[]> ToDictionary() => errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
}

/// <summary>
/// Raised by the store when the database refused a change: a second profile for one account, or a second account for one email, that
/// raced past the checks.
/// </summary>
public sealed class StaffConflictException(Exception inner) : Exception("A staff profile or account already exists.", inner);
