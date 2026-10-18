namespace IitAcademicPortal.Application.Configuration;

/// <summary>Which records a list shows. Active is the default so inactive values stay out of new selections.</summary>
public enum ConfigurationStatus
{
    Active,
    Inactive,
    All,
}

/// <summary>The writable fields of every configuration resource. A resource accepts only the fields it uses.</summary>
public sealed record ConfigurationInput
{
    public string? Name { get; init; }

    public string? Code { get; init; }

    public string? Type { get; init; }

    public string? Address { get; init; }

    public decimal? CreditsToBeCompleted { get; init; }

    public bool? IsActive { get; init; }

    public Guid? DegreeId { get; init; }

    public Guid? StartingSemesterId { get; init; }

    public Guid? AcademicSessionId { get; init; }

    public Guid? DivisionId { get; init; }

    public Guid? DistrictId { get; init; }
}

/// <summary>A stored record as the client sees it. Normalized comparison values are never included.</summary>
public sealed record ConfigurationItem
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public string? Code { get; init; }

    public string? Type { get; init; }

    public string? Address { get; init; }

    public decimal? CreditsToBeCompleted { get; init; }

    public Guid? DegreeId { get; init; }

    public Guid? StartingSemesterId { get; init; }

    public Guid? AcademicSessionId { get; init; }

    public Guid? DivisionId { get; init; }

    public Guid? DistrictId { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    /// <summary>The stable id of the account that created it; null when no signed-in account did (for example a seed script).</summary>
    public string? CreatedByUserId { get; init; }

    /// <summary>That account's current email, resolved when the record is read; null if there is no account or it was removed.</summary>
    public string? CreatedBy { get; init; }

    public DateTime LastModifiedAtUtc { get; init; }

    public string? LastModifiedByUserId { get; init; }

    public string? LastModifiedBy { get; init; }
}

public sealed record ConfigurationQuery(
    string? Search,
    string? Status,
    Guid? ParentId,
    int? PageSize,
    string? Cursor,
    int? Page = null);

/// <summary>
/// One page of records. <c>Page</c>, <c>TotalCount</c>, and <c>TotalPages</c> are set only for a numbered-page request,
/// the one mode that counts every match; cursor paging never does.
/// </summary>
public sealed record ConfigurationPage(
    IReadOnlyList<ConfigurationItem> Items,
    string? NextCursor,
    int? Page = null,
    long? TotalCount = null,
    int? TotalPages = null);

public enum ConfigurationOutcome
{
    Ok,
    Created,
    Deleted,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>The result of a configuration operation: a value, field-keyed errors, or a conflict explanation.</summary>
public sealed record ConfigurationResult<T>(
    ConfigurationOutcome Outcome,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    string? Message = null)
{
    public bool Succeeded => Outcome is ConfigurationOutcome.Ok or ConfigurationOutcome.Created or ConfigurationOutcome.Deleted;
}

/// <summary>Field-keyed validation messages, collected before anything is written.</summary>
public sealed class ConfigurationErrors
{
    private readonly Dictionary<string, List<string>> errors = new(StringComparer.Ordinal);

    public bool Any => errors.Count > 0;

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

/// <summary>Raised by the store when the database rejected a change: a duplicate that raced past the application check.</summary>
public sealed class ConfigurationConflictException(ConfigurationConflictKind kind, Exception inner)
    : Exception("A configuration rule in the database rejected the change.", inner)
{
    public ConfigurationConflictKind Kind { get; } = kind;
}

public enum ConfigurationConflictKind
{
    Duplicate,
}
