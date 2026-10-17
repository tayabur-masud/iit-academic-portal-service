using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Application.UserManagement;

/// <summary>
/// Maintains account identity and access: the registered email, the permitted roles and default role, and deactivation. A Teacher
/// account (one with a teacher profile) is delegated to Teacher Management so its profile and roles stay consistent; Coordinator
/// is never edited here because it follows batch-coordinator assignments. Every change that could remove the last active Admin takes
/// the same transaction lock as Teacher Management before deciding. Changes and their audit events commit together. Authorization
/// is the caller's job; the caller's own account comes from the request context.
/// </summary>
public sealed class UserAccessService(
    UserManager<PortalUser> userManager,
    ITeacherStore unitOfWork,
    IUserAccountStore accounts,
    IAuditActorDirectory actors,
    IAuditEventRecorder audit,
    IAuditRequestContext requestContext,
    IAuthSessionRepository sessions,
    TeacherProfileService teacherProfiles,
    TeacherRoleService teacherRoles,
    TeacherLifecycleService teacherLifecycle,
    TimeProvider clock)
{
    public const string StaleMessage = "The account was changed by someone else. Reload it and try again.";
    public const string SelfDeactivationMessage = "You cannot deactivate your own account. Ask another Admin to do it.";

    /// <summary>
    /// Changes the registered (sign-in and setup) email, keeping the stable user ID. A stale <c>expectedConcurrencyStamp</c> or a
    /// duplicate email is refused with nothing changed. The security stamp rotates, so links already sent to the old address stop
    /// working. A Teacher's email is changed through the teacher profile operation.
    /// </summary>
    public async Task<AccountResult<UserAccountDetail>> UpdateEmailAsync(string userId, AccountEmailInput input, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AccountResult<UserAccountDetail>.NotFound;
        }

        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var email = (input.Email ?? string.Empty).Trim();
        if (UserProvisioningService.ValidateEmail(email) is { } emailError)
        {
            errors["email"] = [emailError];
        }

        if (string.IsNullOrWhiteSpace(input.ExpectedConcurrencyStamp))
        {
            errors["expectedConcurrencyStamp"] = ["Reload the account before changing it."];
        }

        if (errors.Count > 0)
        {
            return AccountResult<UserAccountDetail>.Invalid(errors);
        }

        if (!string.Equals(input.ExpectedConcurrencyStamp, user.ConcurrencyStamp, StringComparison.Ordinal))
        {
            return AccountResult<UserAccountDetail>.Conflict(StaleMessage);
        }

        if (string.Equals(userManager.NormalizeEmail(email), user.NormalizedEmail, StringComparison.Ordinal))
        {
            return await DetailAsync(user.Id, cancellationToken);
        }

        if (await userManager.FindByEmailAsync(email) is { } other && other.Id != user.Id)
        {
            return AccountResult<UserAccountDetail>.Conflict(UserProvisioningService.DuplicateEmailMessage);
        }

        if (await unitOfWork.FindProfileByUserAsync(user.Id, cancellationToken) is { } profile)
        {
            var teacher = await teacherProfiles.UpdateAsync(
                profile.Id,
                new TeacherInput(profile.DisplayName, email, profile.EmployeeStaffId, profile.Phone, profile.DesignationId),
                cancellationToken);
            return await FromTeacherAsync(teacher, user.Id, cancellationToken);
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var previous = user.Email;
            user.Email = email;
            user.UserName = email;

            // Saves the account too, with Identity's normalization, uniqueness, and concurrency checks.
            var updated = await userManager.UpdateSecurityStampAsync(user);
            if (!updated.Succeeded)
            {
                return UserProvisioningService.IdentityFailure(updated);
            }

            Stage(UserAccountAuditEventDefinitions.EmailChanged, user.Id, [new AuditFieldChange("email", previous, email)]);
            if (await SaveAsync(cancellationToken) is { } conflict)
            {
                return conflict;
            }

            await transaction.CommitAsync(cancellationToken);
        }

        return await DetailAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Changes the roles under the clarified policy. A Teacher account is delegated to the Teacher role owner (Teacher kept while
    /// active, Admin added or removed, Coordinator preserved). A Student account holds only Student. Any other account keeps its
    /// approved roles: Teacher needs a teacher profile, Student cannot be combined, and Coordinator is never set here. The default
    /// role must be one of the resulting roles, and the last active Admin keeps the Admin role.
    /// </summary>
    public async Task<AccountResult<UserAccountDetail>> UpdateRolesAsync(string userId, AccountRolesInput input, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AccountResult<UserAccountDetail>.NotFound;
        }

        if (await unitOfWork.FindProfileByUserAsync(user.Id, cancellationToken) is { } profile)
        {
            var teacher = await teacherRoles.UpdateAsync(profile.Id, new TeacherRolesInput(input.Roles, input.DefaultRole), cancellationToken);
            return await FromTeacherAsync(teacher, user.Id, cancellationToken);
        }

        var current = PortalRoles.Normalize(await unitOfWork.GetRolesAsync(user.Id, cancellationToken));
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var requested = ValidateRoles(input, current, errors);
        if (errors.Count > 0)
        {
            return AccountResult<UserAccountDetail>.Invalid(errors);
        }

        if (requested.Contains(PortalRoles.Teacher) && !current.Contains(PortalRoles.Teacher))
        {
            return AccountResult<UserAccountDetail>.Conflict("Teacher access needs a teacher profile. Add the teacher through Teacher Management.");
        }

        if (user.AccountStatus == TeacherAccountStatus.Deactivated)
        {
            return AccountResult<UserAccountDetail>.Conflict("The roles of a deactivated account cannot be changed.");
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockAdminInvariantAsync(cancellationToken);
        current = PortalRoles.Normalize(await unitOfWork.GetRolesAsync(user.Id, cancellationToken));

        // Coordinator follows assignments, so it is carried over untouched.
        var resulting = PortalRoles.Normalize(requested.Concat(current.Where(r => r == PortalRoles.Coordinator)));
        if (current.Contains(PortalRoles.Admin) && !resulting.Contains(PortalRoles.Admin)
            && user.AccountStatus == TeacherAccountStatus.Active
            && !await unitOfWork.HasOtherActiveAdminAsync(user.Id, cancellationToken))
        {
            return AccountResult<UserAccountDetail>.Conflict(TeacherLifecycleService.LastAdminMessage);
        }

        if (input.DefaultRole is { } wanted && !resulting.Contains(wanted, StringComparer.Ordinal))
        {
            return AccountResult<UserAccountDetail>.Invalid("defaultRole", "The default role must be one of the assigned roles.");
        }

        var newDefault = PortalRoles.ResolveDefault(resulting, input.DefaultRole ?? user.DefaultRole);
        var rolesChanged = !current.SequenceEqual(resulting);
        var defaultChanged = !string.Equals(user.DefaultRole, newDefault, StringComparison.Ordinal);
        if (!rolesChanged && !defaultChanged)
        {
            return await DetailAsync(user.Id, cancellationToken);
        }

        foreach (var role in resulting.Except(current))
        {
            UserProvisioningService.Require(await userManager.AddToRoleAsync(user, role));
        }

        foreach (var role in current.Except(resulting))
        {
            UserProvisioningService.Require(await userManager.RemoveFromRoleAsync(user, role));
        }

        var changes = new List<AuditFieldChange>();
        if (rolesChanged)
        {
            changes.Add(new AuditFieldChange("roles", string.Join(", ", current), string.Join(", ", resulting)));
        }

        if (defaultChanged)
        {
            changes.Add(new AuditFieldChange("defaultRole", user.DefaultRole, newDefault));
            user.DefaultRole = newDefault;
            var updated = await userManager.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return UserProvisioningService.IdentityFailure(updated);
            }
        }

        Stage(UserAccountAuditEventDefinitions.RolesChanged, user.Id, changes);
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(user.Id, cancellationToken);
    }

    /// <summary>
    /// Deactivates an account: sign-in is blocked and every active session is revoked, while the account, its roles, and history are
    /// kept. An Admin cannot deactivate their own account, and the last active Admin cannot be deactivated. A Teacher account is
    /// deactivated through Teacher Management so its profile follows. Repeating it on a deactivated account changes nothing.
    /// </summary>
    public async Task<AccountResult<UserAccountDetail>> DeactivateAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AccountResult<UserAccountDetail>.NotFound;
        }

        if (string.Equals(requestContext.ActorUserId, user.Id, StringComparison.Ordinal))
        {
            return AccountResult<UserAccountDetail>.Conflict(SelfDeactivationMessage);
        }

        if (await unitOfWork.FindProfileByUserAsync(user.Id, cancellationToken) is { } profile)
        {
            return await FromTeacherAsync(await teacherLifecycle.DeactivateAsync(profile.Id, cancellationToken), user.Id, cancellationToken);
        }

        if (user.AccountStatus == TeacherAccountStatus.Deactivated)
        {
            return await DetailAsync(user.Id, cancellationToken);
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        await unitOfWork.LockAdminInvariantAsync(cancellationToken);
        var roles = await unitOfWork.GetRolesAsync(user.Id, cancellationToken);
        if (roles.Contains(PortalRoles.Admin, StringComparer.Ordinal)
            && user.AccountStatus == TeacherAccountStatus.Active
            && !await unitOfWork.HasOtherActiveAdminAsync(user.Id, cancellationToken))
        {
            return AccountResult<UserAccountDetail>.Conflict(TeacherLifecycleService.LastAdminMessage);
        }

        var previous = user.AccountStatus;
        user.AccountStatus = TeacherAccountStatus.Deactivated;
        var updated = await userManager.UpdateAsync(user);
        if (!updated.Succeeded)
        {
            return UserProvisioningService.IdentityFailure(updated);
        }

        var revoked = await sessions.RevokeAllActiveForUserAsync(
            user.Id, SessionRevocationReason.AccountDeactivated, clock.GetUtcNow(), cancellationToken);
        audit.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.Deactivated, AuditOutcome.Success)
        {
            EntityType = UserAccountAuditEventDefinitions.EntityType,
            EntityId = user.Id,
            Metadata = new Dictionary<string, object?> { ["sessionsRevoked"] = revoked },
            Changes = [new AuditFieldChange("accountStatus", previous.ToString(), TeacherAccountStatus.Deactivated.ToString())],
        });
        if (await SaveAsync(cancellationToken) is { } conflict)
        {
            return conflict;
        }

        await transaction.CommitAsync(cancellationToken);
        return await DetailAsync(user.Id, cancellationToken);
    }

    private static List<string> ValidateRoles(AccountRolesInput input, IReadOnlyList<string> current, Dictionary<string, string[]> errors)
    {
        var requested = new List<string>();
        if (input.Roles is null || input.Roles.Count == 0)
        {
            errors["roles"] = ["Choose the roles to assign."];
            return requested;
        }

        foreach (var role in input.Roles.Distinct(StringComparer.Ordinal))
        {
            if (role == PortalRoles.Coordinator)
            {
                errors["roles"] = ["Coordinator is assigned automatically from batch-coordinator assignments and cannot be set here."];
            }
            else if (!PortalRoles.IsSupported(role))
            {
                errors["roles"] = ["Choose only supported roles."];
            }
            else
            {
                requested.Add(role);
            }
        }

        if (!errors.ContainsKey("roles"))
        {
            var studentAccount = current.Contains(PortalRoles.Student);
            if (studentAccount && (requested.Count != 1 || requested[0] != PortalRoles.Student))
            {
                errors["roles"] = ["A Student account can hold only the Student role."];
            }
            else if (!studentAccount && requested.Contains(PortalRoles.Student))
            {
                errors["roles"] = ["Student cannot be combined with other roles."];
            }
        }

        if (input.DefaultRole is { } defaultRole && !PortalRoles.IsSupported(defaultRole))
        {
            errors["defaultRole"] = ["Choose a supported role."];
        }

        return requested;
    }

    private void Stage(AuditEventDefinition definition, string userId, IReadOnlyList<AuditFieldChange> changes) =>
        audit.Stage(new AuditEventRequest(definition, AuditOutcome.Success)
        {
            EntityType = UserAccountAuditEventDefinitions.EntityType,
            EntityId = userId,
            Changes = changes,
        });

    private async Task<AccountResult<UserAccountDetail>?> SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveAsync(cancellationToken);
            return null;
        }
        catch (TeacherConflictException conflict)
        {
            return AccountResult<UserAccountDetail>.Conflict(
                conflict.Kind == TeacherConflictKind.Email ? UserProvisioningService.DuplicateEmailMessage : StaleMessage);
        }
    }

    private async Task<AccountResult<UserAccountDetail>> FromTeacherAsync<T>(TeacherResult<T> result, string userId, CancellationToken cancellationToken) =>
        result.Outcome switch
        {
            TeacherOutcome.Ok or TeacherOutcome.Created => await DetailAsync(userId, cancellationToken),
            TeacherOutcome.NotFound => AccountResult<UserAccountDetail>.NotFound,
            TeacherOutcome.Invalid => AccountResult<UserAccountDetail>.Invalid(result.Errors!),
            _ => AccountResult<UserAccountDetail>.Conflict(result.Message ?? StaleMessage),
        };

    private async Task<AccountResult<UserAccountDetail>> DetailAsync(string userId, CancellationToken cancellationToken) =>
        new(AccountOutcome.Ok, await UserDirectoryService.GetDetailAsync(accounts, actors, userId, cancellationToken));
}
