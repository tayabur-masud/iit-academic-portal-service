using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// The account owners' contract for the common setup email. A message is authorized only while the account still exists, still has
/// the address the request was accepted for, and is still waiting for credential setup. Each delivery attempt
/// gets a fresh setup proof from the dedicated token provider, generated in memory; the proof and the link are never stored or logged.
/// </summary>
public sealed class AccountCredentialSetupSecurityContentProvider(
    UserManager<PortalUser> userManager,
    IOptions<AccountCredentialSetupOptions> options,
    IOptions<NotificationOptions> notificationOptions) : ISecurityContentProvider
{
    public NotificationEventType EventType => NotificationEventType.AccountCredentialSetup;

    public async Task<SecurityContentResult> CheckAsync(string recipientUserId, string destination, CancellationToken cancellationToken) =>
        (await FindEligibleAsync(recipientUserId, destination)).Result ?? SecurityContentResult.Eligible;

    public async Task<SecurityContentResult> PrepareAsync(string recipientUserId, string destination, CancellationToken cancellationToken)
    {
        var (user, refused) = await FindEligibleAsync(recipientUserId, destination);
        if (refused is not null)
        {
            return refused;
        }

        var proof = await AccountCredentialSetupService.GenerateProofAsync(userManager, user!);
        var page = options.Value.EffectiveSetupPageUrl(notificationOptions.Value.PortalUrl);
        var link = $"{page}?userId={Uri.EscapeDataString(user!.Id)}&proof={Uri.EscapeDataString(proof)}";
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

        if (!string.Equals(user.Email, destination, StringComparison.OrdinalIgnoreCase))
        {
            return (null, SecurityContentResult.Refused("DestinationChanged"));
        }

        return user.AccountStatus == TeacherAccountStatus.PendingCredentialSetup
            ? (user, null)
            : (null, SecurityContentResult.Refused("AccountNotPending"));
    }

    private static string FormatLifespan(TimeSpan lifespan) =>
        lifespan.TotalMinutes < 120 ? $"{lifespan.TotalMinutes:0} minutes"
        : lifespan.TotalHours < 48 ? $"{lifespan.TotalHours:0} hours"
        : $"{lifespan.TotalDays:0} days";
}
