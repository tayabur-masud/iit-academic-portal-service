using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Application.UserManagement;

public enum CredentialSetupStatus
{
    Succeeded,
    InvalidLink,
    PasswordRejected,
}

public sealed record CredentialSetupOutcome(CredentialSetupStatus Status, IReadOnlyList<string> PasswordErrors)
{
    public static CredentialSetupOutcome Succeeded { get; } = new(CredentialSetupStatus.Succeeded, []);

    public static CredentialSetupOutcome InvalidLink { get; } = new(CredentialSetupStatus.InvalidLink, []);
}

/// <summary>The result of an Admin reissue: the account, and whether the fresh setup email was durably requested.</summary>
public sealed record CredentialSetupReissue(UserAccountDetail Account, bool SetupEmailRequested);

/// <summary>
/// The one first-password setup flow for every newly provisioned account, whatever its role and owner. The proof is an Identity token
/// from a dedicated provider and purpose (<see cref="Purpose"/>), never a password-reset proof; it is bound to the account's
/// security stamp, so a reissue or a completed setup invalidates every earlier link. Proofs and links exist only in memory for one
/// delivery attempt (see <see cref="Notifications.AccountCredentialSetupSecurityContentProvider"/>) and are never stored, returned to an
/// Admin, audited, or logged.
/// </summary>
public sealed class AccountCredentialSetupService(
    UserManager<PortalUser> userManager,
    ITeacherStore unitOfWork,
    IUserAccountStore accounts,
    IAuditActorDirectory actors,
    IAuditEventRecorder audit,
    INotificationRequestService notifications,
    ILogger<AccountCredentialSetupService> logger)
{
    /// <summary>The Identity token provider name and token purpose. Distinct from password recovery's provider and purpose.</summary>
    public const string Purpose = "PortalAccountCredentialSetup";

    public const string TokenProvider = Purpose;

    public const string NotPendingMessage = "Only an account that is waiting for credential setup can get a new setup email.";

    /// <summary>
    /// The request every account owner makes after committing a new account: Teacher Management, generic Admin/Student provisioning,
    /// and the batch-coordinator assignment owner for a new Coordinator. The occurrence is the account ID, so a replay is a duplicate.
    /// </summary>
    public static NotificationEventRequest InitialRequest(string userId) =>
        new(NotificationEventType.AccountCredentialSetup, userId, [userId]);

    /// <summary>A fresh setup token for one delivery attempt. Never persisted or logged.</summary>
    public static Task<string> GenerateProofAsync(UserManager<PortalUser> userManager, PortalUser user) =>
        userManager.GenerateUserTokenAsync(user, TokenProvider, Purpose);

    /// <summary>
    /// Public completion. Validates the password policy first (so the response cannot depend on whether the account exists), then
    /// the setup proof against the dedicated purpose, the pending status, and the current security stamp. On success the password is
    /// added, the account becomes Active, the stamp rotates (consuming this and any other outstanding link), and a safe audit event
    /// commits in the same transaction. Every refusal changes nothing and returns the same generic result.
    /// </summary>
    public async Task<CredentialSetupOutcome> CompleteAsync(string? userId, string? proof, string? newPassword, CancellationToken cancellationToken)
    {
        var policyErrors = PasswordPolicy.Validate(newPassword);
        if (policyErrors.Count > 0)
        {
            await RecordRejectedAsync("password_rejected");
            return new CredentialSetupOutcome(CredentialSetupStatus.PasswordRejected, policyErrors);
        }

        var user = string.IsNullOrWhiteSpace(userId) || string.IsNullOrEmpty(proof) ? null : await userManager.FindByIdAsync(userId);
        if (user is null
            || user.AccountStatus != TeacherAccountStatus.PendingCredentialSetup
            || !await userManager.VerifyUserTokenAsync(user, TokenProvider, Purpose, proof!))
        {
            logger.LogWarning("Account credential setup rejected: invalid setup link");
            await RecordRejectedAsync("invalid_link");
            return CredentialSetupOutcome.InvalidLink;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        // A pending account normally has no password. If another path left one behind, the setup replaces it: until setup completes
        // the account cannot sign in, so that password never granted access.
        if (user.PasswordHash is not null)
        {
            UserProvisioningService.Require(await userManager.RemovePasswordAsync(user));
        }

        var added = await userManager.AddPasswordAsync(user, newPassword!);
        if (!added.Succeeded)
        {
            // A concurrent completion that won the race leaves a password or a new stamp behind: that is a used link, not a bad password.
            if (added.Errors.Any(e => e.Code is nameof(IdentityErrorDescriber.UserAlreadyHasPassword) or nameof(IdentityErrorDescriber.ConcurrencyFailure)))
            {
                await RecordRejectedAsync("invalid_link");
                return CredentialSetupOutcome.InvalidLink;
            }

            await RecordRejectedAsync("password_rejected");
            return new CredentialSetupOutcome(CredentialSetupStatus.PasswordRejected, added.Errors.Select(e => e.Description).ToArray());
        }

        user.AccountStatus = TeacherAccountStatus.Active;
        var activated = await userManager.UpdateSecurityStampAsync(user);
        if (!activated.Succeeded)
        {
            await RecordRejectedAsync("invalid_link");
            return CredentialSetupOutcome.InvalidLink;
        }

        audit.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.CredentialSetupCompleted, AuditOutcome.Success)
        {
            // The account holder acts, without a session.
            ActorUserId = user.Id,
            EntityType = UserAccountAuditEventDefinitions.EntityType,
            EntityId = user.Id,
            Changes = [new AuditFieldChange("accountStatus", TeacherAccountStatus.PendingCredentialSetup.ToString(), TeacherAccountStatus.Active.ToString())],
        });
        await unitOfWork.SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Account credential setup completed for user {UserId}", user.Id);
        return CredentialSetupOutcome.Succeeded;
    }

    /// <summary>
    /// An Admin asks for a fresh setup email for an account still waiting for setup. The security stamp rotates first, so every
    /// earlier link stops working, and the reissue is audited in the same transaction; after the commit a new notification occurrence
    /// is requested. This is not the generic failed-notification resend. Authorization is the caller's job.
    /// </summary>
    public async Task<AccountResult<CredentialSetupReissue>> ReissueAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user is null)
        {
            return AccountResult<CredentialSetupReissue>.NotFound;
        }

        if (user.AccountStatus != TeacherAccountStatus.PendingCredentialSetup)
        {
            return AccountResult<CredentialSetupReissue>.Conflict(NotPendingMessage);
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var rotated = await userManager.UpdateSecurityStampAsync(user);
            if (!rotated.Succeeded)
            {
                return AccountResult<CredentialSetupReissue>.Conflict("The account changed while the setup email was being reissued. Reload and try again.");
            }

            audit.Stage(new AuditEventRequest(UserAccountAuditEventDefinitions.CredentialSetupReissued, AuditOutcome.Success)
            {
                EntityType = UserAccountAuditEventDefinitions.EntityType,
                EntityId = user.Id,
            });
            await unitOfWork.SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var request = await notifications.RequestAsync(
            new NotificationEventRequest(NotificationEventType.AccountCredentialSetup, $"{user.Id}:{Guid.NewGuid():N}", [user.Id]), cancellationToken);
        var detail = await UserDirectoryService.GetDetailAsync(accounts, actors, user.Id, cancellationToken);
        return new AccountResult<CredentialSetupReissue>(
            AccountOutcome.Ok, new CredentialSetupReissue(detail!, request.Status == NotificationRequestStatus.Accepted));
    }

    // A refused attempt records no account, so the audit trail reveals nothing the public response does not. Neither the proof
    // nor the password is ever recorded.
    private Task RecordRejectedAsync(string reason) =>
        audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.CredentialSetupRejected, AuditOutcome.Failure)
        {
            Metadata = new Dictionary<string, object?> { ["reason"] = reason },
        });
}
