namespace IitAcademicPortal.Domain.Configuration;

/// <summary>A District of exactly one Division. Its name is unique within that Division.</summary>
public sealed class District : ConfigurationRecord
{
    public Guid DivisionId { get; set; }
}
