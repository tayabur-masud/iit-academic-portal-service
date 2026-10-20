namespace IitAcademicPortal.Domain.Configuration;

/// <summary>
/// One band of the global Grade scale, shared by every Degree. <see cref="ConfigurationRecord.Name"/> is the letter grade, unique
/// among live bands. Percentage bounds are decimal values between 0 and 100; no extra decimal-rounding rule is imposed by the
/// feature. The lower bound is inclusive and the upper bound exclusive, except that the band ending at 100 includes it
/// (<see cref="UpperBoundInclusive"/>). NumericGrade is a decimal between 0.00 and 4.00 with at most two decimal places.
/// Comment is required. A band refers to no Degree, student, or course.
/// </summary>
public sealed class Grade : ConfigurationRecord
{
    public const decimal MaximumMarks = 100m;

    public const decimal MaximumNumericGrade = 4.00m;

    public const int MaxLetterGradeLength = 20;

    public const int MaxCommentLength = 500;

    public decimal MarksLowerBound { get; set; }

    public decimal MarksUpperBound { get; set; }

    public bool UpperBoundInclusive { get; set; }

    public decimal NumericGrade { get; set; }

    public string Comment { get; set; } = string.Empty;
}
