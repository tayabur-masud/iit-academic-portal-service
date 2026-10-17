using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>
/// Cross-story privacy regression: after provisioning, delivery, reissue, completion, maintenance, and deactivation, no account
/// response, audit event, log line, or permanent notification record contains a password, hash, security stamp, setup proof, setup
/// link, or rendered security body.
/// </summary>
public sealed class AccountCredentialPrivacyTests : UserManagementTestBase
{
    [Fact]
    public async Task No_credential_proof_link_or_rendered_security_body_is_returned_audited_logged_or_kept()
    {
        using var admin = await AdminAsync();
        var responses = new List<string>();

        async Task<JsonElement> Capture(HttpMethod method, string url, object? body = null)
        {
            using var response = await admin.SendAsync(method, url, body);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(NotificationTestHost.IsSuccess(response.StatusCode), $"{method} {url} returned {response.StatusCode}: {text}");
            responses.Add(text);
            return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
        }

        var created = await Capture(HttpMethod.Post, Users, new { email = PortalApiFactory.UniqueEmail("private"), accountType = "Student" });
        var userId = created.GetProperty("userId").GetString()!;
        var email = created.GetProperty("email").GetString()!;
        var first = await LatestSetupLinkAsync(email);
        await Capture(HttpMethod.Post, $"{UserRoute(userId)}/credential-setup-reissues");
        var second = await LatestSetupLinkAsync(email);
        var renderedBodies = Host.Emails.SentTo(email).Select(m => m.Body).ToList();

        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, second.UserId, second.Proof)).Status);
        var detail = await Capture(HttpMethod.Get, UserRoute(userId));
        await Capture(HttpMethod.Put, $"{UserRoute(userId)}/email",
            new { email = PortalApiFactory.UniqueEmail("private-renamed"), expectedConcurrencyStamp = detail.GetProperty("concurrencyStamp").GetString() });
        await Capture(HttpMethod.Put, $"{UserRoute(userId)}/roles", new { roles = new[] { PortalRoles.Student } });
        await Capture(HttpMethod.Post, $"{UserRoute(userId)}/deactivation");
        await Capture(HttpMethod.Get, $"{Users}?search={Uri.EscapeDataString(userId)}");
        await Capture(HttpMethod.Get, History);

        var user = await UserAsync(userId);
        var links = renderedBodies.Select(b => b[b.IndexOf("https://portal.test/account-setup", StringComparison.Ordinal)..].Split('\n')[0].Trim()).ToList();
        string[] secrets = [first.Proof, second.Proof, .. links, NewPassword, user.PasswordHash!, user.SecurityStamp!];

        var audit = (await Host.AuditEventsAsync()).SelectMany(e => new[] { e.MetadataJson ?? "", e.ChangesJson ?? "" }).ToList();
        var records = await Host.RecordsAsync();
        var kept = records.SelectMany(r => new[] { r.Subject ?? "", r.Body ?? "", r.Destination ?? "" }).ToList();
        await Host.WithDbAsync(async db =>
            kept.AddRange((await db.NotificationAttempts.AsNoTracking().ToListAsync()).Select(a => a.FailureCode ?? "")));

        NotificationTestHost.AssertNoSecret(responses, secrets);
        NotificationTestHost.AssertNoSecret(audit, secrets);
        NotificationTestHost.AssertNoSecret([Host.Logs.Everything], secrets);
        NotificationTestHost.AssertNoSecret(kept, secrets);
        Assert.All(records, r => Assert.True(r.ContentWithheld));
        Assert.All(renderedBodies, body => NotificationTestHost.AssertNoSecret(kept.Concat(audit).Concat(responses), body));
        Assert.All(responses, text =>
        {
            Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("proof", text, StringComparison.OrdinalIgnoreCase);
        });
    }
}
