using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.TeacherManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// The teacher-provisioning producer: a committed account requests an invitation, a rolled-back one requests nothing, no password
/// is set or returned, and the pending account stays unable to sign in. Delivery of the invitation needs the account owner's secure
/// setup-link contract, which does not exist yet, so these tests prove the request and the visible gate, not an end-to-end send.
/// </summary>
public sealed class TeacherInvitationProducerTests : NotificationTestBase
{
    private async Task<(string UserId, string Email)> CreateTeacherAsync(PortalClient admin)
    {
        var email = PortalApiFactory.UniqueEmail("teacher");
        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Invited Teacher", email });
        Assert.Equal(HttpStatusCode.Created, status);
        return (body.GetProperty("userId").GetString()!, email);
    }

    [Fact]
    public async Task A_committed_account_requests_one_invitation_for_that_account_only()
    {
        using var admin = await AdminAsync();

        var (userId, email) = await CreateTeacherAsync(admin);

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationEventType.TeacherInvitation, record.EventType);
        Assert.Equal(userId, record.RecipientUserId);
        Assert.Equal(userId, record.OccurrenceId);
        Assert.Equal(email, record.Destination);
        Assert.True(record.ContentWithheld);
        Assert.Null(record.Subject);
        Assert.Null(record.Body);
    }

    [Fact]
    public async Task No_password_is_set_or_returned_and_the_pending_account_still_cannot_sign_in()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("teacher");

        using var response = await admin.SendAsync(HttpMethod.Post, "/api/admin/teachers", new { displayName = "Quiet Teacher", email });
        var text = await response.Content.ReadAsStringAsync();
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("link", text, StringComparison.OrdinalIgnoreCase);
        await Host.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            Assert.Null(user.PasswordHash);
            Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        });
        using var client = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Without_the_owners_setup_link_nothing_is_sent_and_the_invitation_is_visibly_failed_and_resendable_later()
    {
        using var admin = await AdminAsync();
        var (userId, email) = await CreateTeacherAsync(admin);

        await Host.FlushNotificationsAsync();

        // The gate is visible: the notification is Failed with a reason, not silently dropped and not faked as sent. No reset proof
        // is reused and the account is not activated.
        Assert.Empty(Host.Emails.Messages);
        Assert.Equal(0, Host.Emails.Attempts);
        var record = Assert.Single(await Host.RecordsAsync());
        var state = await Host.StateAsync(record.Id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("SetupLinkProviderMissing", state.FailureCode);
        await Host.WithDbAsync(async db =>
            Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await db.Users.SingleAsync(u => u.Id == userId)).AccountStatus));
        _ = email;
    }

    [Fact]
    public async Task A_rolled_back_account_requests_no_invitation()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("teacher");
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var service = new TeacherProvisioningService(
                scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>(),
                scope.ServiceProvider.GetRequiredService<ITeacherStore>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                scope.ServiceProvider.GetRequiredService<IAuditActorDirectory>(),
                scope.ServiceProvider.GetRequiredService<INotificationRequestService>());

            await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(new TeacherInput("Rolled Back", email, null, null, null), CancellationToken.None));
        }

        await Host.FlushNotificationsAsync();

        Assert.Empty(await Host.RecordsAsync());
        Assert.Empty(Host.Emails.Messages);
    }

    [Fact]
    public async Task A_rejected_creation_requests_no_invitation()
    {
        using var admin = await AdminAsync();
        var (_, email) = await CreateTeacherAsync(admin);
        var before = (await Host.RecordsAsync()).Count;

        var (status, _) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Duplicate", email });
        var (invalid, _) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "", email = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.Equal(before, (await Host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task Account_creation_succeeds_during_a_provider_outage_and_a_replay_creates_no_duplicate()
    {
        using var admin = await AdminAsync();
        Host.Outage();

        var (userId, _) = await CreateTeacherAsync(admin);
        await Host.FlushNotificationsAsync();
        var replay = await Host.RequestAsync(new NotificationEventRequest(NotificationEventType.TeacherInvitation, userId, [userId]));
        await Host.FlushNotificationsAsync();

        Assert.Equal(1, replay.Duplicates);
        Assert.Single(await Host.RecordsAsync());
        Assert.Empty(Host.Emails.Messages);
    }
}
