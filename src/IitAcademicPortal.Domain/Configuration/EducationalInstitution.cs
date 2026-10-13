namespace IitAcademicPortal.Domain.Configuration;

public enum EducationalInstitutionType
{
    School,
    College,
    University,
}

/// <summary>A school, college, or university. Independent of students and boards.</summary>
public sealed class EducationalInstitution : ConfigurationRecord, ICodedRecord
{
    public EducationalInstitutionType Type { get; set; }

    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public string? Address { get; set; }

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
