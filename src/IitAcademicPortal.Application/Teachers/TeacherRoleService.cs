using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>
/// Changes a teacher's editable roles and default role. Teacher stays assigned while the profile is active, Admin may be
/// assigned or removed, Student is never assigned, and Coordinator is not editable here: it follows batch-coordinator
/// assignments (see <see cref="ITeacherCoordinatorRoleSynchronizer"/>) and is kept as it is. The default role must be one of the
/// assigned roles. The last active Admin cannot lose the Admin role.
/// </summary>
public sealed class TeacherRoleService(
    UserManager<PortalUser> userManager,
    ITeacherStore store,
    IAuditEventRecorder recorder,
    IAuditActorDirectory actors)
{
    public async Task<TeacherResult<TeacherDetail>> UpdateAsync(Guid teacherId, TeacherRolesInput input, CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(teacherId, cancellationToken);
        if (profile is null || await userManager.FindByIdAsync(profile.UserId) is not { } user)
        {
            return TeacherResult<TeacherDetail>.NotFound;
        }

        var errors = new TeacherErrors();
        var requested = ValidateRoles(input, profile.IsActive, errors);
        if (errors.Any)
        {
            return TeacherResult<TeacherDetail>.Invalid(errors);
        }

        if (!profile.IsActive)
        {
            return TeacherResult<TeacherDetail>.Conflict("The roles of a deactivated teacher cannot be changed.");
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        await store.LockAdminInvariantAsync(cancellationToken);
        var current = PortalRoles.Normalize(await store.GetRolesAsync(user.Id, cancellationToken));

        // Coordinator follows assignments, so it is carried over untouched.
        var resulting = PortalRoles.Normalize(requested.Concat(current.Where(r => r == PortalRoles.Coordinator)));
        var removingAdmin = current.Contains(PortalRoles.Admin) && !resulting.Contains(PortalRoles.Admin);
        if (removingAdmin && user.AccountStatus == TeacherAccountStatus.Active
            && !await store.HasOtherActiveAdminAsync(user.Id, cancellationToken))
        {
            return TeacherResult<TeacherDetail>.Conflict(TeacherLifecycleService.LastAdminMessage);
        }

        if (input.DefaultRole is { } wanted && !resulting.Contains(wanted, StringComparer.Ordinal))
        {
            errors.Add("defaultRole", "The default role must be one of the assigned roles.");
            return TeacherResult<TeacherDetail>.Invalid(errors);
        }

        var newDefault = PortalRoles.ResolveDefault(resulting, input.DefaultRole ?? user.DefaultRole);
        var rolesChanged = !current.SequenceEqual(resulting);
        var defaultChanged = !string.Equals(user.DefaultRole, newDefault, StringComparison.Ordinal);
        if (!rolesChanged && !defaultChanged)
        {
            return await DetailAsync(teacherId, cancellationToken);
        }

        foreach (var role in resulting.Except(current))
        {
            Require(await userManager.AddToRoleAsync(user, role));
        }

        foreach (var role in current.Except(resulting))
        {
            Require(await userManager.RemoveFromRoleAsync(user, role));
        }

        var changes = new List<AuditFieldChange>();
        if (rolesChanged)
        {
            changes.Add(new AuditFieldChange("roles", TeacherSupport.RoleList(current), TeacherSupport.RoleList(resulting)));
        }

        if (defaultChanged)
        {
            changes.Add(new AuditFieldChange("defaultRole", user.DefaultRole, newDefault));
            user.DefaultRole = newDefault;
            Require(await userManager.UpdateAsync(user));
        }

        TeacherSupport.Stage(recorder, TeacherAuditEventDefinitions.RolesChanged, profile, changes);
        await store.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(teacherId, cancellationToken);
    }

    private static List<string> ValidateRoles(TeacherRolesInput input, bool profileActive, TeacherErrors errors)
    {
        var requested = new List<string>();
        if (input.Roles is null)
        {
            errors.Add("roles", "Choose the roles to assign.");
            return requested;
        }

        foreach (var role in input.Roles.Distinct(StringComparer.Ordinal))
        {
            if (role == PortalRoles.Student)
            {
                errors.Add("roles", "Student cannot be assigned to a teacher.");
            }
            else if (role == PortalRoles.Coordinator)
            {
                errors.Add("roles", "Coordinator is assigned automatically from batch-coordinator assignments and cannot be set here.");
            }
            else if (!PortalRoles.IsSupported(role))
            {
                errors.Add("roles", "Choose only supported roles.");
            }
            else
            {
                requested.Add(role);
            }
        }

        if (profileActive && !requested.Contains(PortalRoles.Teacher) && !errors.Has("roles"))
        {
            errors.Add("roles", "Teacher is required while the teacher profile is active.");
        }

        if (input.DefaultRole is { } defaultRole && !PortalRoles.IsSupported(defaultRole))
        {
            errors.Add("defaultRole", "Choose a supported role.");
        }

        return requested;
    }

    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("A role change failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        }
    }

    private async Task<TeacherResult<TeacherDetail>> DetailAsync(Guid teacherId, CancellationToken cancellationToken) =>
        new(TeacherOutcome.Ok, await TeacherSupport.WithActorsAsync((await store.GetDetailAsync(teacherId, cancellationToken))!, actors, cancellationToken));
}
