using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Configuration;

namespace IitAcademicPortal.Api.Contracts.Configuration;

/// <summary>The complete desired Grade scale for <c>PUT /api/admin/configuration/grades/scale</c>.</summary>
public sealed class GradeScaleRequest
{
    public List<GradeBandRequest>? Bands { get; init; }

    public IReadOnlyList<GradeBandInput>? ToInput() => Bands?.Select(b => b.ToInput()).ToList();
}

/// <summary>One band of a requested scale. Leave <see cref="Id"/> out to create a band; a live band left out of the scale is deleted.</summary>
public sealed class GradeBandRequest
{
    public Guid? Id { get; init; }

    public string? LetterGrade { get; init; }

    public decimal? MarksLowerBound { get; init; }

    public decimal? MarksUpperBound { get; init; }

    public bool? UpperBoundInclusive { get; init; }

    public decimal? NumericGrade { get; init; }

    public string? Comment { get; init; }

    public bool? IsActive { get; init; }

    public GradeBandInput ToInput() =>
        new(Id, LetterGrade, MarksLowerBound, MarksUpperBound, UpperBoundInclusive, NumericGrade, Comment, IsActive);
}

public sealed record GradeBandResponse(
    Guid Id,
    string LetterGrade,
    decimal MarksLowerBound,
    decimal MarksUpperBound,
    bool UpperBoundInclusive,
    decimal NumericGrade,
    string Comment,
    bool IsActive,
    DateTime CreatedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CreatedBy,
    DateTime LastModifiedAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedByUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? LastModifiedBy)
{
    public static GradeBandResponse From(GradeBand band) => new(
        band.Id, band.LetterGrade, band.MarksLowerBound, band.MarksUpperBound, band.UpperBoundInclusive, band.NumericGrade, band.Comment,
        band.IsActive, band.CreatedAtUtc, band.CreatedByUserId, band.CreatedBy, band.LastModifiedAtUtc, band.LastModifiedByUserId, band.LastModifiedBy);
}

/// <summary>The whole scale in one page, highest band first; <c>nextCursor</c> is always null, keeping the shared list shape.</summary>
public sealed record GradeScalePageResponse(IReadOnlyList<GradeBandResponse> Items, string? NextCursor);
