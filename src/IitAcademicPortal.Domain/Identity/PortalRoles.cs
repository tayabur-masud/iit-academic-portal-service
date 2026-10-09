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

    /// <summary>Fallback order for the sign-in role when an account has no valid stored default.</summary>
    public static IReadOnlyList<string> DefaultPrecedence { get; } = [Admin, Coordinator, Teacher, Student];

    public static bool IsSupported(string? role) => role is not null && All.Contains(role, StringComparer.Ordinal);

    /// <summary>
    /// The role an account enters without choosing: its stored default while that role is still assigned,
    /// otherwise the first assigned role in <see cref="DefaultPrecedence"/>. Null when no supported role is assigned.
    /// </summary>
    public static string? ResolveDefault(IEnumerable<string?> assignedRoles, string? storedDefault)
    {
        var assigned = Normalize(assignedRoles);
        return storedDefault is not null && assigned.Contains(storedDefault, StringComparer.Ordinal)
            ? storedDefault
            : DefaultPrecedence.FirstOrDefault(assigned.Contains);
    }

    /// <summary>Keeps only supported roles, de-duplicated and in canonical order.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string?> roles)
    {
        var assigned = roles.ToHashSet(StringComparer.Ordinal);
        return All.Where(assigned.Contains).ToArray();
    }
}
