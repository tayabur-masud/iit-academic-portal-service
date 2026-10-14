using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using IitAcademicPortal.Domain.Teachers;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>
/// Deactivates a teacher: the profile becomes inactive, the account is disabled, and every active session is revoked, in one
/// transaction with the audit event. Roles, profile, and history are kept; nothing is deleted. The last active Admin cannot be
/// deactivated.
/// </summary>
public sealed class TeacherLifecycleService(
    UserManager<PortalUser> userManager,
    ITeacherStore store,
    IAuthSessionRepository sessions,
    IAuditEventRecorder recorder,
    IAuditActorDirectory actors,
    TimeProvider clock)
{
    public const string LastAdminMessage = "This is the last active Admin account, so it cannot be deactivated or lose the Admin role.";

    public async Task<TeacherResult<TeacherDetail>> DeactivateAsync(Guid teacherId, CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(teacherId, cancellationToken);
        if (profile is null || await userManager.FindByIdAsync(profile.UserId) is not { } user)
        {
            return TeacherResult<TeacherDetail>.NotFound;
        }

        if (!profile.IsActive && user.AccountStatus == TeacherAccountStatus.Deactivated)
        {
            // Already deactivated: nothing changes, so nothing is audited.
            return new TeacherResult<TeacherDetail>(TeacherOutcome.Ok, await DetailAsync(teacherId, cancellationToken));
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        if (await ApplyAsync(profile, user, cancellationToken) is { } refused)
        {
            return refused;
        }

        await store.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new TeacherResult<TeacherDetail>(TeacherOutcome.Ok, await DetailAsync(teacherId, cancellationToken));
    }

    /// <summary>
    /// The deactivation itself, for a caller that already holds an open transaction (an update that also deactivates). Returns a
    /// refusal, or null when the changes are staged and the caller should save and commit.
    /// </summary>
    internal async Task<TeacherResult<TeacherDetail>?> ApplyAsync(TeacherProfile profile, PortalUser user, CancellationToken cancellationToken)
    {
        await store.LockAdminInvariantAsync(cancellationToken);
        var roles = await store.GetRolesAsync(user.Id, cancellationToken);
        if (roles.Contains(PortalRoles.Admin, StringComparer.Ordinal)
            && user.AccountStatus == TeacherAccountStatus.Active
            && !await store.HasOtherActiveAdminAsync(user.Id, cancellationToken))
        {
            return TeacherResult<TeacherDetail>.Conflict(LastAdminMessage);
        }

        var previousStatus = user.AccountStatus;
        var wasActive = profile.IsActive;
        profile.Deactivate();
        user.AccountStatus = TeacherAccountStatus.Deactivated;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("The account could not be disabled: " + string.Join("; ", updated.Errors.Select(e => e.Code)));
        }

        var revoked = await sessions.RevokeAllActiveForUserAsync(
            user.Id, SessionRevocationReason.TeacherDeactivated, clock.GetUtcNow(), cancellationToken);

        TeacherSupport.Stage(recorder, TeacherAuditEventDefinitions.Deactivated, profile,
        [
            new("isActive", wasActive, false),
            new("accountStatus", previousStatus.ToString(), TeacherAccountStatus.Deactivated.ToString()),
        ],
        new Dictionary<string, object?> { ["sessionsRevoked"] = revoked });
        return null;
    }

    private async Task<TeacherDetail> DetailAsync(Guid teacherId, CancellationToken cancellationToken) =>
        await TeacherSupport.WithActorsAsync((await store.GetDetailAsync(teacherId, cancellationToken))!, actors, cancellationToken);
}
