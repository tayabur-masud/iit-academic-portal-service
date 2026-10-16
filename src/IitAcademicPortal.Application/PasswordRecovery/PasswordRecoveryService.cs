using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.PasswordRecovery;

public enum PasswordResetStatus
{
    Succeeded,
    InvalidProof,
    PasswordRejected,
}

public sealed record PasswordResetOutcome(PasswordResetStatus Status, IReadOnlyList<string> PasswordErrors)
{
    public static PasswordResetOutcome Succeeded { get; } = new(PasswordResetStatus.Succeeded, []);

    public static PasswordResetOutcome InvalidProof { get; } = new(PasswordResetStatus.InvalidProof, []);
}

/// <summary>
/// Identity-backed password recovery. Requests never reveal whether an account exists; proofs are
/// short-lived and single-use (Identity rotates the security stamp on reset, invalidating the proof).
/// Delivery is not done here: an accepted request is kept durably and, when a worker resolves an eligible account, handed to the
/// notification capability once. The proof itself is generated later, in memory, for one authorized delivery attempt.
/// </summary>
public sealed class PasswordRecoveryService(
    UserManager<PortalUser> userManager,
    INotificationStore notificationStore,
    ITransientSecretProtector protector,
    INotificationRequestService notifications,
    INotificationFailureSignal failureSignal,
    PortalAuthenticationService authentication,
    IAuditEventRecorder audit,
    IOptions<PasswordRecoveryOptions> options,
    TimeProvider timeProvider,
    ILogger<PasswordRecoveryService> logger)
{
    public const string PublicRequestMessage =
        "If an eligible account exists, recovery instructions will be sent to its registered email address.";

    /// <summary>
    /// Accepts a request and keeps it durably (the submitted address only in protected form) without any account-dependent work, so
    /// the public response cannot depend on whether an account exists. A failure to retain it never changes the response: it raises
    /// the operational failure signal instead.
    /// </summary>
    public async Task RequestAsync(string email, CancellationToken cancellationToken)
    {
        try
        {
            var request = PasswordRecoveryRequest.Create(
                protector.Protect(email.Trim()), timeProvider.GetUtcNow().UtcDateTime, options.Value.RequestLifetime);
            await notificationStore.AddRecoveryRequestAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The exception type only: messages from the database driver can echo values.
            logger.LogError("A password recovery request could not be retained ({ExceptionType})", ex.GetType().Name);
            failureSignal.PersistenceFailed(NotificationEventType.PasswordReset, "password-recovery-request", 1, ex.GetType().Name);
        }
    }

    /// <summary>
    /// Background step: resolves the account for a retained request. An eligible account gets one password-reset notification
    /// (idempotent on the request); an unknown address creates no permanent record. Returns false when the work must be retried.
    /// </summary>
    public async Task<bool> ProcessRequestAsync(Guid requestId, string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user?.Email is null)
        {
            logger.LogInformation("Password recovery requested for an address with no eligible account");
            return true;
        }

        var result = await notifications.RequestAsync(
            new NotificationEventRequest(NotificationEventType.PasswordReset, requestId.ToString("N"), [user.Id]), cancellationToken);
        return result.Status != NotificationRequestStatus.PersistenceFailed;
    }

    /// <summary>
    /// Replaces the password using a valid proof. On success only <paramref name="currentSessionId"/>
    /// (the session used for the reset, if any) is revoked; other sessions remain active by design.
    /// </summary>
    public async Task<PasswordResetOutcome> ResetAsync(
        string email, string proof, string newPassword, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        // Checked before any account lookup so the response cannot reveal whether the account exists.
        var policyErrors = PasswordPolicy.Validate(newPassword);
        if (policyErrors.Count > 0)
        {
            await RecordResetAsync(AuditOutcome.Failure, null, "password_rejected");
            return new PasswordResetOutcome(PasswordResetStatus.PasswordRejected, policyErrors);
        }

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            logger.LogWarning("Password reset rejected: invalid recovery proof");
            await RecordResetAsync(AuditOutcome.Failure, null, "invalid_proof");
            return PasswordResetOutcome.InvalidProof;
        }

        var result = await userManager.ResetPasswordAsync(user, proof, newPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                logger.LogWarning("Password reset rejected for user {UserId}: invalid recovery proof", user.Id);
                await RecordResetAsync(AuditOutcome.Failure, null, "invalid_proof");
                return PasswordResetOutcome.InvalidProof;
            }

            await RecordResetAsync(AuditOutcome.Failure, null, "password_rejected");
            return new PasswordResetOutcome(PasswordResetStatus.PasswordRejected, result.Errors.Select(e => e.Description).ToArray());
        }

        if (currentSessionId is { } sessionId)
        {
            await authentication.RevokeAsync(sessionId, SessionRevocationReason.PasswordReset, cancellationToken);
        }

        logger.LogInformation("Password reset completed for user {UserId}", user.Id);
        await RecordResetAsync(AuditOutcome.Success, user.Id, null);
        return PasswordResetOutcome.Succeeded;
    }

    // A failed reset records no account, so the audit trail reveals nothing the client response does not. Neither
    // the recovery proof nor the new password is ever recorded.
    private Task RecordResetAsync(AuditOutcome outcome, string? actorUserId, string? reason) =>
        audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.PasswordResetCompleted, outcome)
        {
            ActorUserId = actorUserId,
            Metadata = reason is null ? null : new Dictionary<string, object?> { ["reason"] = reason },
        });
}
