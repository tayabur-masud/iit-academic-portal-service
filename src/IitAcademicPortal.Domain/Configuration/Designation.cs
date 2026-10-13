namespace IitAcademicPortal.Domain.Configuration;

/// <summary>A teaching or staff designation. Assignment to a Teacher is out of scope.</summary>
public sealed class Designation : ConfigurationRecord, ICodedRecord
{
    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
