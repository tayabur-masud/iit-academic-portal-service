namespace IitAcademicPortal.Domain.Configuration;

/// <summary>A program IIT offers. Independent of Batch and Semester.</summary>
public sealed class Degree : ConfigurationRecord, ICodedRecord
{
    public decimal CreditsToBeCompleted { get; private set; }

    public string? Code { get; private set; }

    public string? NormalizedCode { get; private set; }

    public void SetCreditsToBeCompleted(decimal creditsToBeCompleted) => CreditsToBeCompleted = creditsToBeCompleted;

    public void SetCode(string? code)
    {
        Code = NameNormalizer.DisplayCode(code);
        NormalizedCode = NameNormalizer.NormalizeCode(code);
    }
}
