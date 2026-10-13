namespace IitAcademicPortal.Domain.Configuration;

/// <summary>An Upazila of exactly one District. Its name is unique within that District.</summary>
public sealed class Upazila : ConfigurationRecord
{
    public Guid DistrictId { get; set; }
}
