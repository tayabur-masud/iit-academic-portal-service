using System.Net;
using System.Text.RegularExpressions;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>
/// US2: the common setup flow. The emailed proof is purpose-specific and single-use, activates only a pending account after a valid
/// password, and a pending-only Admin reissue invalidates every earlier link. Failures change nothing and disclose nothing.
/// </summary>
public sealed partial class AccountCredentialSetupTests : UserManagementTestBase
{
    [Fact]
    public async Task The_setup_email_links_to_the_setup_page_and_completing_it_activates_the_account_with_a_safe_audit()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);

        await Host.FlushNotificationsAsync();
        var message = Assert.Single(Host.Emails.SentTo(account.Email));
        Assert.Equal("Set up your IIT Academic Portal account", message.Subject);
        Assert.Contains($"https://portal.test/account-setup?userId={Uri.EscapeDataString(account.UserId)}&proof=", message.Body);
        Assert.Contains("expires in 3 days", message.Body);
        var link = ParseLink(message.Body);
        using var holder = Host.CreatePortalClient();

        var (status, _) = await CompleteAsync(holder, link.UserId, link.Proof);

        Assert.Equal(HttpStatusCode.NoContent, status);
        var user = await UserAsync(account.UserId);
        Assert.Equal(TeacherAccountStatus.Active, user.AccountStatus);
        Assert.NotNull(user.PasswordHash);
        Assert.Equal(HttpStatusCode.Created, (await holder.SignInAsync(account.Email, NewPassword)).StatusCode);

        var completed = Assert.Single(await EventsAsync("account.credential-setup.completed"));
        Assert.Equal(account.UserId, completed.ActorUserId);
        Assert.Equal(account.UserId, completed.EntityId);
        Assert.Contains("Active", completed.ChangesJson);
        NotificationTestHost.AssertNoSecret([completed.ChangesJson ?? "", completed.MetadataJson ?? ""], link.Proof, NewPassword, user.PasswordHash!);
    }

    [Fact]
    public async Task A_used_link_cannot_be_replayed_and_the_password_stays_as_set()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Admin);
        var link = await LatestSetupLinkAsync(account.Email);
        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
        var hash = (await UserAsync(account.UserId)).PasswordHash;

        var (status, problem) = await CompleteAsync(holder, link.UserId, link.Proof, "Another-pass7");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Setup link not valid", problem.GetProperty("title").GetString());
        Assert.Equal(hash, (await UserAsync(account.UserId)).PasswordHash);
        Assert.Single(await EventsAsync("account.credential-setup.completed"));
    }

    [Fact]
    public async Task A_password_reset_proof_is_not_a_setup_proof_and_a_setup_proof_cannot_reset_a_password()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var link = await LatestSetupLinkAsync(account.Email);
        string resetProof;
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
            resetProof = await users.GeneratePasswordResetTokenAsync((await users.FindByIdAsync(account.UserId))!);
        }

        using var holder = Host.CreatePortalClient();
        var (withResetProof, _) = await CompleteAsync(holder, account.UserId, resetProof);
        using var asReset = await holder.SendAsync(HttpMethod.Post, "/api/auth/password-resets",
            new { email = account.Email, proof = link.Proof, newPassword = NewPassword });

        Assert.Equal(HttpStatusCode.BadRequest, withResetProof);
        Assert.Equal(HttpStatusCode.BadRequest, asReset.StatusCode);
        var user = await UserAsync(account.UserId);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        Assert.Null(user.PasswordHash);

        // The genuine setup link still works afterwards.
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
    }

    [Fact]
    public async Task A_password_that_breaks_the_policy_is_refused_without_activating_and_the_link_still_works()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var link = await LatestSetupLinkAsync(account.Email);
        using var holder = Host.CreatePortalClient();

        var (status, problem) = await CompleteAsync(holder, link.UserId, link.Proof, "short");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "NewPassword") || HasError(problem, "newPassword"), problem.ToString());
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(account.UserId)).AccountStatus);
        Assert.Empty(await EventsAsync("account.credential-setup.completed"));
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
    }

    [Fact]
    public async Task Wrong_unknown_and_tampered_links_all_get_the_same_generic_response()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var other = await CreateAccountAsync(admin, PortalRoles.Student);
        var link = await LatestSetupLinkAsync(account.Email);
        using var holder = Host.CreatePortalClient();

        var bodies = new List<string>();
        foreach (var (userId, proof) in new[] { (other.UserId, link.Proof), ("no-such-account", link.Proof), (link.UserId, link.Proof + "x") })
        {
            using var response = await holder.SendAsync(HttpMethod.Post, Setup, new { userId, proof, newPassword = NewPassword });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            bodies.Add(TraceId().Replace(await response.Content.ReadAsStringAsync(), string.Empty));
        }

        Assert.Single(bodies.Distinct());
        Assert.DoesNotContain(account.Email, bodies[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(other.UserId)).AccountStatus);
        var rejected = await EventsAsync("auth.credential-setup.rejected");
        Assert.Equal(3, rejected.Count);
        Assert.All(rejected, e => Assert.Null(e.ActorUserId));
        NotificationTestHost.AssertNoSecret(rejected.Select(e => e.MetadataJson ?? ""), link.Proof, NewPassword, account.UserId);
    }

    [Fact]
    public async Task An_active_or_deactivated_account_cannot_use_a_setup_link()
    {
        using var admin = await AdminAsync();
        var active = await CreateAccountAsync(admin, PortalRoles.Student);
        var deactivated = await CreateAccountAsync(admin, PortalRoles.Student);
        var activeLink = await LatestSetupLinkAsync(active.Email);
        var deactivatedLink = await LatestSetupLinkAsync(deactivated.Email);
        await Host.SetAccountStatusAsync(active.Email, TeacherAccountStatus.Active);
        await Host.SetAccountStatusAsync(deactivated.Email, TeacherAccountStatus.Deactivated);
        using var holder = Host.CreatePortalClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, activeLink.UserId, activeLink.Proof)).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, deactivatedLink.UserId, deactivatedLink.Proof)).Status);
        Assert.Equal(TeacherAccountStatus.Deactivated, (await UserAsync(deactivated.UserId)).AccountStatus);
    }

    [Fact]
    public async Task An_Admin_reissue_sends_a_fresh_link_invalidates_the_old_one_and_is_audited_without_proof_material()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var first = await LatestSetupLinkAsync(account.Email);

        using var response = await admin.SendAsync(HttpMethod.Post, $"{UserRoute(account.UserId)}/credential-setup-reissues");
        var text = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"setupEmailRequested\":true", text);
        Assert.DoesNotContain("proof", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        var second = await LatestSetupLinkAsync(account.Email);
        Assert.NotEqual(first.Proof, second.Proof);
        Assert.Equal(2, Host.Emails.SentTo(account.Email).Count());
        var records = (await Host.RecordsAsync()).Where(r => r.EventType == NotificationEventType.AccountCredentialSetup).ToList();
        Assert.Equal(2, records.Select(r => r.OccurrenceId).Distinct().Count());

        var reissued = Assert.Single(await EventsAsync("account.credential-setup.reissued"));
        Assert.Equal(AdminUserId, reissued.ActorUserId);
        Assert.Equal(account.UserId, reissued.EntityId);
        NotificationTestHost.AssertNoSecret([reissued.ChangesJson ?? "", reissued.MetadataJson ?? ""], first.Proof, second.Proof, account.Email);

        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, first.UserId, first.Proof)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, second.UserId, second.Proof)).Status);
    }

    [Fact]
    public async Task Reissue_is_refused_for_active_deactivated_and_unknown_accounts()
    {
        using var admin = await AdminAsync();
        var activeEmail = PortalApiFactory.UniqueEmail("active");
        var activeId = await Host.CreateUserAsync(activeEmail, PortalRoles.Student);
        var deactivated = await CreateAccountAsync(admin, PortalRoles.Student);
        await Host.SetAccountStatusAsync(deactivated.Email, TeacherAccountStatus.Deactivated);
        var before = (await Host.RecordsAsync()).Count;

        var (activeStatus, activeProblem) = await Reissue(admin, activeId);
        var (deactivatedStatus, _) = await Reissue(admin, deactivated.UserId);
        var (unknownStatus, _) = await Reissue(admin, "no-such-account");

        Assert.Equal(HttpStatusCode.Conflict, activeStatus);
        Assert.Equal(HttpStatusCode.Conflict, deactivatedStatus);
        Assert.Equal(HttpStatusCode.NotFound, unknownStatus);
        Assert.Contains("waiting for credential setup", activeProblem.GetProperty("detail").GetString());
        Assert.Equal(before, (await Host.RecordsAsync()).Count);
        Assert.Empty(await EventsAsync("account.credential-setup.reissued"));
    }

    [Fact]
    public async Task Only_an_active_Admin_may_reissue()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var link = await LatestSetupLinkAsync(account.Email);
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);
        using var teacher = await SignedInAsync(PortalRoles.Teacher);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Reissue(anonymous, account.UserId)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reissue(student, account.UserId)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Reissue(teacher, account.UserId)).Status);

        // Nothing was rotated, so the original link still works.
        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
    }

    [Fact]
    public async Task A_failed_delivery_leaves_the_account_pending_and_a_reissue_recovers()
    {
        using var admin = await AdminAsync();
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent();
        var account = await CreateAccountAsync(admin, PortalRoles.Admin);

        await Host.FlushNotificationsAsync();

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(record.Id)).Status);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(account.UserId)).AccountStatus);
        using var client = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(account.Email)).StatusCode);

        Host.Emails.Behavior = null;
        Assert.Equal(HttpStatusCode.OK, (await Reissue(admin, account.UserId)).Status);
        var link = await LatestSetupLinkAsync(account.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(client, link.UserId, link.Proof)).Status);
    }

    [Fact]
    public async Task Delivery_is_refused_when_the_account_is_no_longer_pending_or_its_address_changed()
    {
        using var admin = await AdminAsync();
        var activated = await CreateAccountAsync(admin, PortalRoles.Student);
        var moved = await CreateAccountAsync(admin, PortalRoles.Student);
        await Host.SetAccountStatusAsync(activated.Email, TeacherAccountStatus.Active);
        await Host.WithDbAsync(async db =>
        {
            var user = db.Users.Single(u => u.Id == moved.UserId);
            user.Email = PortalApiFactory.UniqueEmail("moved");
            user.NormalizedEmail = user.Email.ToUpperInvariant();
            await db.SaveChangesAsync();
        });

        await Host.FlushNotificationsAsync();

        Assert.Empty(Host.Emails.Messages);
        var records = await Host.RecordsAsync();
        var codes = new List<string?>();
        foreach (var record in records)
        {
            codes.Add((await Host.StateAsync(record.Id)).FailureCode);
        }

        Assert.Contains("AccountNotPending", codes);
        Assert.Contains("DestinationChanged", codes);
    }

    [Fact]
    public async Task A_pending_account_gets_no_password_reset_email_so_recovery_never_bypasses_setup()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        await Host.FlushNotificationsAsync();
        using var client = Host.CreatePortalClient();

        using var response = await client.SendAsync(HttpMethod.Post, "/api/auth/password-reset-requests", new { email = account.Email });
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var reset = Assert.Single(await Host.RecordsAsync(), r => r.EventType == NotificationEventType.PasswordReset);
        Assert.Equal("AccountPendingSetup", (await Host.StateAsync(reset.Id)).FailureCode);
        Assert.Single(Host.Emails.SentTo(account.Email));
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(account.UserId)).AccountStatus);
    }

    [GeneratedRegex("\"traceId\":\"[^\"]*\"")]
    private static partial Regex TraceId();
}

/// <summary>A host whose setup proofs are already expired when generated.</summary>
public sealed class ExpiredSetupProofHost : NotificationTestHost
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting("AccountCredentialSetup:ProofLifespan", "-00:00:01");
    }
}

public sealed class ExpiredSetupProofTests : IDisposable
{
    private readonly ExpiredSetupProofHost host = new();

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task An_expired_setup_link_is_refused_and_the_account_stays_pending()
    {
        var adminEmail = PortalApiFactory.UniqueEmail("admin");
        await host.CreateUserAsync(adminEmail, PortalRoles.Admin);
        using var admin = host.CreatePortalClient();
        await admin.SignInSuccessfullyAsync(adminEmail);
        var email = PortalApiFactory.UniqueEmail("late");
        using var created = await admin.SendAsync(HttpMethod.Post, "/api/admin/users", new { email, accountType = "Student" });
        var userId = (await NotificationTestHost.ReadAsync(created)).GetProperty("userId").GetString()!;
        await host.FlushNotificationsAsync();
        var body = Assert.Single(host.Emails.SentTo(email)).Body;
        var proof = Uri.UnescapeDataString(Regex.Match(body, @"[?&]proof=([^&\s]+)").Groups[1].Value);

        using var response = await admin.SendAsync(HttpMethod.Post, "/api/auth/account-credential-setups", new { userId, proof, newPassword = "Chosen-pass9" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await host.WithDbAsync(async db =>
            Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await db.Users.FindAsync(userId))!.AccountStatus));
    }
}
