namespace IitAcademicPortal.Domain.Configuration;

/// <summary>
/// A City Corporation of exactly one District, a District child alongside its Upazilas. It has no code, and its name is
/// unique among the City Corporations of that District.
/// </summary>
public sealed class CityCorporation : ConfigurationRecord
{
    public Guid DistrictId { get; set; }
}
