namespace IitAcademicPortal.Application.Configuration;

/// <summary>One band of a requested scale. A band with an <see cref="Id"/> updates that band; one without creates a band.</summary>
public sealed record GradeBandInput(
    Guid? Id,
    string? LetterGrade,
    decimal? MarksLowerBound,
    decimal? MarksUpperBound,
    bool? UpperBoundInclusive,
    decimal? NumericGrade,
    string? Comment,
    bool? IsActive);

/// <summary>A stored band, with who created and last changed it.</summary>
public sealed record GradeBand(
    Guid Id,
    string LetterGrade,
    decimal MarksLowerBound,
    decimal MarksUpperBound,
    bool UpperBoundInclusive,
    decimal NumericGrade,
    string Comment,
    bool IsActive,
    DateTime CreatedAtUtc,
    string? CreatedByUserId,
    string? CreatedBy,
    DateTime LastModifiedAtUtc,
    string? LastModifiedByUserId,
    string? LastModifiedBy);

public enum GradeScaleOutcome
{
    Ok,
    Invalid,
    NotFound,
}

/// <summary>
/// The outcome of a Grade scale call: the bands (a list or a replacement), one band (a read), or field-keyed errors. Errors are
/// keyed <c>bands</c> for the scale as a whole and <c>bands[i].field</c> for one band of the request.
/// </summary>
public sealed record GradeScaleResult(
    GradeScaleOutcome Outcome,
    IReadOnlyList<GradeBand>? Items = null,
    GradeBand? Item = null,
    IReadOnlyDictionary<string, string[]>? Errors = null);
