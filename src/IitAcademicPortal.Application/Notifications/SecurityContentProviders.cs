using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// The password-recovery workflow's contract: a message is authorized only while the account still exists and still has the
/// address the request was accepted for, and each delivery attempt gets a fresh Identity reset proof generated in memory. The proof
/// and the link are never stored or logged.
/// </summary>
public sealed class PasswordResetContentProvider(UserManager<PortalUser> userManager, IOptions<PasswordRecoveryOptions> options)
    : ISecurityContentProvider
{
    public NotificationEventType EventType => NotificationEventType.PasswordReset;

    public async Task<SecurityContentResult> CheckAsync(string recipientUserId, string destination, CancellationToken cancellationToken) =>
        (await FindEligibleAsync(recipientUserId, destination)).Result ?? SecurityContentResult.Eligible;

    public async Task<SecurityContentResult> PrepareAsync(string recipientUserId, string destination, CancellationToken cancellationToken)
    {
        var (user, refused) = await FindEligibleAsync(recipientUserId, destination);
        if (refused is not null)
        {
            return refused;
        }

        var proof = await userManager.GeneratePasswordResetTokenAsync(user!);
        var link = $"{options.Value.ResetPageUrl}?email={Uri.EscapeDataString(user!.Email!)}&proof={Uri.EscapeDataString(proof)}";
        return new SecurityContentResult(
            SecurityContentStatus.Ready,
            Values: new Dictionary<string, string>
            {
                [NotificationTemplateCatalog.ActionLink] = link,
                [NotificationTemplateCatalog.ExpiresIn] = FormatLifespan(options.Value.ProofLifespan),
            });
    }

    private async Task<(PortalUser? User, SecurityContentResult? Result)> FindEligibleAsync(string userId, string destination)
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user?.Email is null)
        {
            return (null, SecurityContentResult.Refused("RecipientRemoved"));
        }

        return string.Equals(user.Email, destination, StringComparison.OrdinalIgnoreCase)
            ? (user, null)
            : (null, SecurityContentResult.Refused("DestinationChanged"));
    }

    private static string FormatLifespan(TimeSpan lifespan) =>
        lifespan.TotalMinutes < 120 ? $"{lifespan.TotalMinutes:0} minutes" : $"{lifespan.TotalHours:0} hours";
}

/// <summary>
/// Stands in for a security event whose owning account workflow has not supplied its secure link contract yet (teacher and student
/// invitation, email confirmation). Nothing is sent: the notification fails with a visible, resendable reason. It never reuses a
/// password-reset proof and never activates an account.
/// </summary>
public sealed class UnavailableSecurityContentProvider(NotificationEventType eventType, string code) : ISecurityContentProvider
{
    public NotificationEventType EventType => eventType;

    public Task<SecurityContentResult> CheckAsync(string recipientUserId, string destination, CancellationToken cancellationToken) =>
        Task.FromResult(SecurityContentResult.NoProvider(code));

    public Task<SecurityContentResult> PrepareAsync(string recipientUserId, string destination, CancellationToken cancellationToken) =>
        Task.FromResult(SecurityContentResult.NoProvider(code));
}
