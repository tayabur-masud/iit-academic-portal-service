using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
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
/// </summary>
public sealed class PasswordRecoveryService(
    UserManager<PortalUser> userManager,
    IPasswordRecoveryQueue queue,
    IEmailSender emailSender,
    PortalAuthenticationService authentication,
    IAuditEventRecorder audit,
    IOptions<PasswordRecoveryOptions> options,
    ILogger<PasswordRecoveryService> logger)
{
    public const string PublicRequestMessage =
        "If an eligible account exists, recovery instructions will be sent to its registered email address.";

    /// <summary>Accepts a request without doing any account-dependent work on the request path.</summary>
    public void Request(string email) => queue.Enqueue(email);

    /// <summary>Background step: sends a recovery proof to the email recorded on a matching account.</summary>
    public async Task ProcessRequestAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user?.Email is null)
        {
            logger.LogInformation("Password recovery requested for an address with no eligible account");
            return;
        }

        var proof = await userManager.GeneratePasswordResetTokenAsync(user);
        var link = $"{options.Value.ResetPageUrl}?email={Uri.EscapeDataString(user.Email)}&proof={Uri.EscapeDataString(proof)}";
        var body =
            "A password reset was requested for your IIT Academic Portal account.\n\n" +
            $"Use this link to choose a new password. It works once and expires in {FormatLifespan(options.Value.ProofLifespan)}:\n\n" +
            $"{link}\n\n" +
            "If you did not request this, you can ignore this email. Your password will not change.";

        await emailSender.SendAsync(new EmailMessage(user.Email, "Reset your IIT Academic Portal password", body), cancellationToken);
        logger.LogInformation("Password recovery instructions sent for user {UserId}", user.Id);
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

    private static string FormatLifespan(TimeSpan lifespan) =>
        lifespan.TotalMinutes < 120 ? $"{lifespan.TotalMinutes:0} minutes" : $"{lifespan.TotalHours:0} hours";
}
