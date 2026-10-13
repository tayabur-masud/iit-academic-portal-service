namespace IitAcademicPortal.Domain.Configuration;

/// <summary>A board that conducts previous-education examinations. Independent of students and institutions.</summary>
public sealed class EducationBoard : ConfigurationRecord, ICodedRecord
{
    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
