using IitAcademicPortal.Domain.Authorization;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.Authorization;

/// <summary>
/// Record boundaries from the approved feature list, evaluated for the session's active role only so
/// a multi-role user never receives the union of their roles' permissions.
/// </summary>
public static class RecordAccessRules
{
    public static bool CanAccess(string? activeRole, string userId, object record) => activeRole switch
    {
        PortalRoles.Student => record is IStudentOwnedRecord owned
            && string.Equals(owned.StudentUserId, userId, StringComparison.Ordinal),
        PortalRoles.Teacher => record is ITeacherAssignedRecord assigned
            && assigned.AssignedTeacherUserIds.Contains(userId, StringComparer.Ordinal),
        PortalRoles.Coordinator => record is ICoordinatorAssignedRecord batch
            && batch.AssignedCoordinatorUserIds.Contains(userId, StringComparer.Ordinal),

        // Admin works through Admin Module capabilities (a module policy), not these record boundaries.
        _ => false,
    };
}
