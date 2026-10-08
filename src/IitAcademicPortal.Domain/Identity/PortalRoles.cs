namespace IitAcademicPortal.Domain.Identity;

/// <summary>The controlled catalog of supported portal roles.</summary>
public static class PortalRoles
{
    public const string Admin = "Admin";
    public const string Student = "Student";
    public const string Teacher = "Teacher";
    public const string Coordinator = "Coordinator";

    /// <summary>Supported roles in their canonical display order.</summary>
    public static IReadOnlyList<string> All { get; } = [Admin, Student, Teacher, Coordinator];

    public static bool IsSupported(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);

    /// <summary>Keeps only supported roles, de-duplicated and in canonical order.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> roles)
    {
        var assigned = roles.ToHashSet(StringComparer.Ordinal);
        return All.Where(assigned.Contains).ToArray();
    }
}
