namespace IitAcademicPortal.Domain.Configuration;

public enum CourseType
{
    Mandatory,
    Elective,
}

/// <summary>
/// A course of one Degree's catalog. Its code is required and unique across every live course (active or not); its name need not be
/// unique. It has a positive credit-hour total with optional theory and lab parts (each between zero and the total, and summing to it
/// when both are given), a Mandatory or Elective type, and at most one prerequisite: a different active course of the same Degree,
/// never forming a cycle. A course is deactivated, never deleted, so later enrollment and result records can keep referring to it.
/// </summary>
public sealed class Course : ConfigurationRecord, ICodedRecord
{
    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public decimal CreditHours { get; set; }

    public decimal? TheoryCreditHours { get; set; }

    public decimal? LabCreditHours { get; set; }

    public CourseType Type { get; set; }

    public Guid DegreeId { get; set; }

    public Guid? PrerequisiteCourseId { get; set; }

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
