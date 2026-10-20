using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Configuration;

namespace IitAcademicPortal.Api.Contracts.Configuration;

/// <summary>
/// The writable representation of a configuration record. A resource uses only some of these fields (for example a
/// degree has no <c>type</c>); fields it does not use are rejected with a field error.
/// </summary>
public sealed class ConfigurationRequest
{
    public string? Name { get; init; }

    public string? Code { get; init; }

    public string? Type { get; init; }

    public string? Address { get; init; }

    public decimal? CreditsToBeCompleted { get; init; }

    public decimal? MinimumCgpaForCompletion { get; init; }

    public decimal? CreditHours { get; init; }

    public decimal? TheoryCreditHours { get; init; }

    public decimal? LabCreditHours { get; init; }

    public Guid? PrerequisiteCourseId { get; init; }

    public bool? IsActive { get; init; }

    public Guid? DegreeId { get; init; }

    public Guid? StartingSemesterId { get; init; }

    public Guid? AcademicSessionId { get; init; }

    public Guid? DivisionId { get; init; }

    public Guid? DistrictId { get; init; }

    public ConfigurationInput ToInput() => new()
    {
        Name = Name,
        Code = Code,
        Type = Type,
        Address = Address,
        CreditsToBeCompleted = CreditsToBeCompleted,
        MinimumCgpaForCompletion = MinimumCgpaForCompletion,
        CreditHours = CreditHours,
        TheoryCreditHours = TheoryCreditHours,
        LabCreditHours = LabCreditHours,
        PrerequisiteCourseId = PrerequisiteCourseId,
        IsActive = IsActive,
        DegreeId = DegreeId,
        StartingSemesterId = StartingSemesterId,
        AcademicSessionId = AcademicSessionId,
        DivisionId = DivisionId,
        DistrictId = DistrictId,
    };
}

/// <summary>A stored record. Fields a resource does not have are omitted; normalized comparison values are never sent.</summary>
public sealed record ConfigurationItemResponse(
    Guid Id,
    string Name,
    bool IsActive,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Address,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CreditsToBeCompleted,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? MinimumCgpaForCompletion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? CreditHours,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? TheoryCreditHours,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] decimal? LabCreditHours,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PrerequisiteCourseId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DegreeId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? StartingSemesterId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AcademicSessionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DivisionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? DistrictId,
    DateTime CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    DateTime LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy)
{
    public static ConfigurationItemResponse From(ConfigurationItem item) => new(
        item.Id, item.Name, item.IsActive, item.Code, item.Type, item.Address, item.CreditsToBeCompleted, item.MinimumCgpaForCompletion,
        item.CreditHours, item.TheoryCreditHours, item.LabCreditHours, item.PrerequisiteCourseId, item.DegreeId, item.StartingSemesterId, item.AcademicSessionId, item.DivisionId, item.DistrictId,
        item.CreatedAtUtc, item.CreatedByUserId, item.CreatedBy, item.LastModifiedAtUtc, item.LastModifiedByUserId, item.LastModifiedBy);
}

/// <summary>
/// A page of records. <c>Page</c>, <c>TotalCount</c>, and <c>TotalPages</c> are present only when the request asked for a
/// numbered page.
/// </summary>
public sealed record ConfigurationPageResponse(
    IReadOnlyList<ConfigurationItemResponse> Items,
    string? NextCursor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Page = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? TotalCount = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? TotalPages = null);

/// <summary>
/// List filters. <c>divisionId</c> and <c>districtId</c> limit a geography list to one parent; <c>degreeId</c> limits a course list to
/// one Degree (used for prerequisite choices); <c>type</c> limits a course or institution list to one type.
/// </summary>
public sealed class ConfigurationQueryString
{
    public string? Search { get; init; }

    public string? Status { get; init; }

    public int? PageSize { get; init; }

    public string? Cursor { get; init; }

    /// <summary>A 1-based page number. Cannot be combined with <see cref="Cursor"/>.</summary>
    public int? Page { get; init; }

    public Guid? DivisionId { get; init; }

    public Guid? DistrictId { get; init; }

    public Guid? DegreeId { get; init; }

    /// <summary>Limits a list of a typed catalog (courses, educational institutions) to one type.</summary>
    public string? Type { get; init; }

    public ConfigurationQuery ToQuery() => new(Search, Status, DivisionId ?? DistrictId ?? DegreeId, PageSize, Cursor, Page, Type);
}
