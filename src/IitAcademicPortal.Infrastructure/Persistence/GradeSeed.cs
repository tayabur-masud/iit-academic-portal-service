namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// The stable ids of the ten initial Grade bands, highest first (A+ to F), as the
/// <c>CityCorporationsDegreeMinimumCgpaAndGrades</c> migration inserts them.
/// </summary>
public static class GradeSeed
{
    public static readonly Guid[] InitialIds =
    [
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000001"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000002"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000003"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000004"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000005"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000006"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000007"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000008"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-000000000009"),
        new("5b0c6a1e-7d3f-5e2a-9c4b-00000000000a"),
    ];
}
