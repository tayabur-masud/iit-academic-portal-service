using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.MyProfile;

/// <summary>
/// My Profile: the signed-in account's own profile for its active role, read-only. The caller passes the user ID and active role from
/// the validated session; nothing in the request chooses the account or the role. Admin reads the Staff profile, Student the Student
/// profile, and Teacher or Coordinator the Teacher profile. A missing profile is returned as none, never replaced by another role's.
/// </summary>
public sealed class MyProfileService(IUserAccountStore accounts, IStaffStore staff, IStudentStore students, ITeacherStore teachers)
{
    public async Task<MyProfileResult> GetAsync(string userId, string? activeRole, CancellationToken cancellationToken)
    {
        if (!PortalRoles.IsSupported(activeRole))
        {
            return new MyProfileResult(MyProfileOutcome.Forbidden);
        }

        var email = await accounts.GetEmailAsync(userId, cancellationToken);
        if (email is null)
        {
            return new MyProfileResult(MyProfileOutcome.NotFound);
        }

        MyRoleProfile? profile = activeRole switch
        {
            PortalRoles.Admin => await staff.GetOwnProfileAsync(userId, cancellationToken),
            PortalRoles.Student => await students.GetOwnProfileAsync(userId, cancellationToken),
            _ => await teachers.GetOwnProfileAsync(userId, cancellationToken),
        };
        return new MyProfileResult(MyProfileOutcome.Ok, new MyProfileView(email, activeRole!, profile));
    }

    /// <summary>
    /// The name the account menu shows for the signed-in account: the name on the active role's profile (the one My Profile shows),
    /// otherwise the name on another of its profiles (Staff, then Teacher, then Student), otherwise its email. Null only when the account
    /// no longer exists.
    /// </summary>
    public async Task<string?> GetDisplayNameAsync(string userId, string? activeRole, CancellationToken cancellationToken)
    {
        var names = await accounts.GetNamesAsync(userId, cancellationToken);
        if (names is null)
        {
            return null;
        }

        var forActiveRole = activeRole switch
        {
            PortalRoles.Admin => names.StaffName,
            PortalRoles.Student => names.StudentName,
            PortalRoles.Teacher or PortalRoles.Coordinator => names.TeacherName,
            _ => null,
        };
        return new[] { forActiveRole, names.StaffName, names.TeacherName, names.StudentName, names.Email }
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
    }
}
