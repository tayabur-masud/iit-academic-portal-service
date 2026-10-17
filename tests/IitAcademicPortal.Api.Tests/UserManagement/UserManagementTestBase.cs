using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.Notifications;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>A notification-capable API host, an active-Admin client, and helpers for the User Management and setup routes.</summary>
public abstract partial class UserManagementTestBase : NotificationTestBase
{
    protected const string Users = "/api/admin/users";
    protected const string Setup = "/api/auth/account-credential-setups";
    protected const string NewPassword = "Chosen-pass9";

    protected record Account(string UserId, string Email);

    protected record SetupLink(string UserId, string Proof);

    protected static string UserRoute(string userId) => $"{Users}/{Uri.EscapeDataString(userId)}";

    /// <summary>Creates a generic account through the API, failing the test if it was not created.</summary>
    protected async Task<Account> CreateAccountAsync(PortalClient admin, string accountType, string? email = null)
    {
        email ??= PortalApiFactory.UniqueEmail(accountType.ToLowerInvariant());
        var (status, body) = await SendAsync(admin, HttpMethod.Post, Users, new { email, accountType });
        Assert.True(status == HttpStatusCode.Created, $"Creating an account returned {status}: {body}");
        return new Account(body.GetProperty("userId").GetString()!, email);
    }

    protected async Task<Account> CreateTeacherAsync(PortalClient admin)
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Setup Teacher", email });
        Assert.True(status == HttpStatusCode.Created, $"Creating a teacher returned {status}: {body}");
        return new Account(body.GetProperty("userId").GetString()!, email);
    }

    /// <summary>Delivers due notifications and returns the link in the newest setup email sent to the address.</summary>
    protected async Task<SetupLink> LatestSetupLinkAsync(string email)
    {
        await Host.FlushNotificationsAsync();
        var message = Host.Emails.SentTo(email).Last();
        return ParseLink(message.Body);
    }

    protected static SetupLink ParseLink(string body)
    {
        var match = LinkPattern().Match(body);
        Assert.True(match.Success, "The setup email has no setup link.");
        return new SetupLink(Uri.UnescapeDataString(match.Groups[1].Value), Uri.UnescapeDataString(match.Groups[2].Value));
    }

    protected static Task<(HttpStatusCode Status, JsonElement Body)> CompleteAsync(PortalClient client, string userId, string proof, string newPassword = NewPassword) =>
        SendAsync(client, HttpMethod.Post, Setup, new { userId, proof, newPassword });

    protected static Task<(HttpStatusCode Status, JsonElement Body)> Reissue(PortalClient admin, string userId) =>
        SendAsync(admin, HttpMethod.Post, $"{UserRoute(userId)}/credential-setup-reissues");

    protected static string[] Roles(JsonElement account) =>
        account.GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();

    protected async Task<PortalUser> UserAsync(string userId)
    {
        PortalUser? user = null;
        await Host.WithDbAsync(async db => user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        return user!;
    }

    protected async Task<string[]> AssignedRolesAsync(string userId)
    {
        var roles = Array.Empty<string>();
        await Host.WithDbAsync(async db => roles = await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToArrayAsync());
        return PortalRoles.Normalize(roles).ToArray();
    }

    protected async Task<int> UserCountAsync(string email)
    {
        var count = 0;
        var normalized = email.Trim().ToUpperInvariant();
        await Host.WithDbAsync(async db => count = await db.Users.CountAsync(u => u.NormalizedEmail == normalized));
        return count;
    }

    protected Task<List<AuditEvent>> EventsAsync(string eventType) => Host.AuditEventsAsync(eventType);

    /// <summary>Signs in as a fresh active account with the given roles.</summary>
    protected async Task<PortalClient> SignedInAsync(params string[] roles)
    {
        var email = PortalApiFactory.UniqueEmail("user");
        await Host.CreateUserAsync(email, roles);
        var client = Host.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    [GeneratedRegex(@"[?&]userId=([^&\s]+)&proof=([^&\s]+)")]
    private static partial Regex LinkPattern();
}
