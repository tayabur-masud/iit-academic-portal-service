using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.Staff;

/// <summary>
/// Deactivates a staff member: the profile becomes inactive, the linked account is disabled, and every active session is revoked, in one
/// transaction with the audit event. The profile, roles, and history are kept; nothing is deleted and nothing here reactivates. An Admin
/// cannot deactivate their own account, and the last active Admin cannot be deactivated (under the same lock Teacher and User Management
/// take). Repeating a completed deactivation changes nothing.
/// </summary>
public sealed class StaffLifecycleService(
    UserManager<PortalUser> userManager,
    IStaffStore store,
    IAuthSessionRepository sessions,
    IAuditEventRecorder recorder,
    IAuditRequestContext request,
    IAuditActorDirectory actors,
    TimeProvider clock)
{
    public const string SelfDeactivationMessage = "You cannot deactivate your own staff account. Ask another Admin to do it.";

    public async Task<StaffResult<StaffDetail>> DeactivateAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var profile = await store.FindProfileAsync(profileId, cancellationToken);
        if (profile is null || await userManager.FindByIdAsync(profile.UserId) is not { } user)
        {
            return StaffResult<StaffDetail>.NotFound("No staff profile has that identifier.");
        }

        if (string.Equals(request.ActorUserId, user.Id, StringComparison.Ordinal))
        {
            return StaffResult<StaffDetail>.Conflict(SelfDeactivationMessage);
        }

        if (!profile.IsActive && user.AccountStatus == TeacherAccountStatus.Deactivated)
        {
            // Already deactivated: nothing changes, so nothing is audited.
            return await DetailAsync(profileId, cancellationToken);
        }

        await using var transaction = await store.BeginTransactionAsync(cancellationToken);
        await store.LockAdminInvariantAsync(cancellationToken);
        var isAdmin = await userManager.IsInRoleAsync(user, PortalRoles.Admin);
        if (isAdmin && user.AccountStatus == TeacherAccountStatus.Active && !await store.HasOtherActiveAdminAsync(user.Id, cancellationToken))
        {
            return StaffResult<StaffDetail>.Conflict(TeacherLifecycleService.LastAdminMessage);
        }

        var wasActive = profile.IsActive;
        var previousStatus = user.AccountStatus;
        profile.Deactivate();
        user.AccountStatus = TeacherAccountStatus.Deactivated;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            throw new InvalidOperationException("The account could not be disabled: " + string.Join("; ", updated.Errors.Select(e => e.Code)));
        }

        var revoked = await sessions.RevokeAllActiveForUserAsync(user.Id, SessionRevocationReason.AccountDeactivated, clock.GetUtcNow(), cancellationToken);
        recorder.Stage(new AuditEventRequest(StaffAuditEventDefinitions.Deactivated, AuditOutcome.Success)
        {
            EntityType = "StaffProfile",
            EntityId = profile.Id.ToString(),
            Metadata = new Dictionary<string, object?> { ["userId"] = user.Id, ["sessionsRevoked"] = revoked },
            Changes =
            [
                new AuditFieldChange("isActive", wasActive, false),
                new AuditFieldChange("accountStatus", previousStatus.ToString(), TeacherAccountStatus.Deactivated.ToString()),
            ],
        });

        await store.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(profileId, cancellationToken);
    }

    private async Task<StaffResult<StaffDetail>> DetailAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var detail = (await store.GetDetailAsync(profileId, cancellationToken))!;
        var ids = new[] { detail.CreatedByUserId, detail.LastModifiedByUserId }.OfType<string>().Distinct().ToArray();
        var emails = ids.Length == 0 ? new Dictionary<string, string>() : await actors.GetEmailsAsync(ids, cancellationToken);
        string? Email(string? id) => id is not null && emails.TryGetValue(id, out var email) ? email : null;
        return new StaffResult<StaffDetail>(StaffOutcome.Ok, detail with
        {
            CreatedBy = Email(detail.CreatedByUserId),
            LastModifiedBy = Email(detail.LastModifiedByUserId),
        });
    }
}
