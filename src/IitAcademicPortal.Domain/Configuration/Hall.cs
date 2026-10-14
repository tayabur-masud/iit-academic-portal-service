namespace IitAcademicPortal.Domain.Configuration;

/// <summary>A university residential hall. Independent of students; it has a name, an optional unique code, and an optional address.</summary>
public sealed class Hall : ConfigurationRecord, ICodedRecord
{
    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public string? Address { get; set; }

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
