using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>The writable teacher fields. <see cref="IsActive"/> applies only to an update; a new teacher is always active.</summary>
public sealed record TeacherInput(
    string? DisplayName,
    string? Email,
    string? EmployeeStaffId,
    string? Phone,
    Guid? DesignationId,
    bool? IsActive = null);

/// <summary>The editable roles a teacher should hold, and which assigned role the account enters at sign-in.</summary>
public sealed record TeacherRolesInput(IReadOnlyList<string>? Roles, string? DefaultRole);

/// <summary>A teacher as an Admin sees it. Never contains a credential, hash, or token.</summary>
public sealed record TeacherDetail(
    Guid Id,
    string UserId,
    string Email,
    string DisplayName,
    string? EmployeeStaffId,
    string? Phone,
    Guid? DesignationId,
    string? DesignationName,
    bool IsActive,
    TeacherAccountStatus AccountStatus,
    IReadOnlyList<string> Roles,
    string? DefaultRole,
    DateTime CreatedAtUtc,
    string? CreatedByUserId,
    string? CreatedBy,
    DateTime LastModifiedAtUtc,
    string? LastModifiedByUserId,
    string? LastModifiedBy);

public sealed record TeacherListFilter(string? NormalizedSearch, TeacherAccountStatus? Status);

public sealed record TeacherListPage(IReadOnlyList<TeacherDetail> Items, int Page, long TotalCount, int TotalPages);

public sealed record TeacherQuery(string? Search, string? Status, int? Page, int? PageSize);

public enum TeacherOutcome
{
    Ok,
    Created,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of a teacher operation: a value, field-keyed errors, or a conflict explanation.</summary>
public sealed record TeacherResult<T>(
    TeacherOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is TeacherOutcome.Ok or TeacherOutcome.Created;

    public static TeacherResult<T> NotFound { get; } = new(TeacherOutcome.NotFound);

    public static TeacherResult<T> Invalid(TeacherErrors errors) => new(TeacherOutcome.Invalid, Errors: errors.ToDictionary());

    public static TeacherResult<T> Conflict(string message) => new(TeacherOutcome.Conflict, Message: message);
}

/// <summary>Field-keyed validation messages, collected before anything is written.</summary>
public sealed class TeacherErrors
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
