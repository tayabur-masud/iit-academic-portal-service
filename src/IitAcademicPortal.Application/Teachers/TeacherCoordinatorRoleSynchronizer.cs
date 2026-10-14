using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Teachers;

public enum CoordinatorSyncOutcome
{
    /// <summary>The account already matched the supplied state; nothing changed and nothing was audited.</summary>
    Unchanged,
    Added,
    Removed,
}

/// <summary>The caller asked for something that cannot be synchronized: an unknown account, or one without a usable teacher profile.</summary>
public sealed class CoordinatorSyncException(string message) : InvalidOperationException(message);

/// <summary>
/// Keeps the Coordinator role in step with a teacher's batch-coordinator assignments. This is an internal contract, not an
/// endpoint: the workflow that creates, changes, or ends an assignment calls it in the same unit of work, after working out whether
/// any active assignment remains, and must not commit the assignment if this fails.
/// </summary>
public interface ITeacherCoordinatorRoleSynchronizer
{
    /// <summary>
    /// Adds Coordinator when <paramref name="hasActiveCoordinatorAssignments"/> is true and removes it when false. Changes are
    /// staged on the caller's unit of work (including the audit event) and are saved by the caller, so the role and the
    /// assignment commit together or not at all. Repeating a call with the same state changes and audits nothing.
    /// </summary>
    /// <exception cref="CoordinatorSyncException">The user is unknown, or has no teacher profile, or is being made a Coordinator while the profile is inactive.</exception>
    Task<CoordinatorSyncOutcome> SynchronizeCoordinatorRoleAsync(
        string teacherUserId, bool hasActiveCoordinatorAssignments, CancellationToken cancellationToken);
}

public sealed class TeacherCoordinatorRoleSynchronizer(
    UserManager<PortalUser> userManager,
    ITeacherStore store,
    IAuditEventRecorder recorder) : ITeacherCoordinatorRoleSynchronizer
{
    public async Task<CoordinatorSyncOutcome> SynchronizeCoordinatorRoleAsync(
        string teacherUserId, bool hasActiveCoordinatorAssignments, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(teacherUserId)
            ?? throw new CoordinatorSyncException("No account has that identifier.");
        var profile = await store.FindProfileByUserAsync(teacherUserId, cancellationToken)
            ?? throw new CoordinatorSyncException("The account has no teacher profile.");
        if (hasActiveCoordinatorAssignments && !profile.IsActive)
        {
            throw new CoordinatorSyncException("A deactivated teacher cannot coordinate a batch.");
        }

        var current = PortalRoles.Normalize(await store.GetRolesAsync(teacherUserId, cancellationToken));
        var has = current.Contains(PortalRoles.Coordinator);
        if (has == hasActiveCoordinatorAssignments)
        {
            return CoordinatorSyncOutcome.Unchanged;
        }

        // Only the Coordinator row changes: Teacher, Admin, and Student are never touched here. The changes are staged without
        // saving, so they commit with the caller's assignment change.
        await store.StageRoleAsync(teacherUserId, PortalRoles.Coordinator, hasActiveCoordinatorAssignments, cancellationToken);
        var resulting = PortalRoles.Normalize(hasActiveCoordinatorAssignments
            ? current.Append(PortalRoles.Coordinator)
            : current.Where(r => r != PortalRoles.Coordinator));

        var changes = new List<AuditFieldChange> { new("roles", TeacherSupport.RoleList(current), TeacherSupport.RoleList(resulting)) };
        var newDefault = PortalRoles.ResolveDefault(resulting, user.DefaultRole);
        if (!string.Equals(user.DefaultRole, newDefault, StringComparison.Ordinal))
        {
            changes.Add(new AuditFieldChange("defaultRole", user.DefaultRole, newDefault));
            user.DefaultRole = newDefault;
        }

        TeacherSupport.Stage(recorder, TeacherAuditEventDefinitions.CoordinatorRoleSynchronized, profile, changes);
        return hasActiveCoordinatorAssignments ? CoordinatorSyncOutcome.Added : CoordinatorSyncOutcome.Removed;
    }
}
