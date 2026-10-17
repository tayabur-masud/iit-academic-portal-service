using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.TeacherManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>
/// US2 owner integration: every account owner requests the same <see cref="NotificationEventType.AccountCredentialSetup"/> event only
/// after its own transaction commits. Teacher Management is proven end to end. The batch-coordinator assignment owner does not exist
/// yet, so its producer test is a skipped release gate, not a pass.
/// </summary>
public sealed class AccountCredentialSetupOwnerTests : UserManagementTestBase
{
    [Fact]
    public async Task Teacher_provisioning_requests_the_common_setup_event_once_for_the_committed_account()
    {
        using var admin = await AdminAsync();

        var teacher = await CreateTeacherAsync(admin);

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationEventType.AccountCredentialSetup, record.EventType);
        Assert.Equal(teacher.UserId, record.RecipientUserId);
        Assert.Equal(teacher.UserId, record.OccurrenceId);
        Assert.Equal(teacher.Email, record.Destination);
        Assert.True(record.ContentWithheld);
        Assert.Null(record.Subject);
        Assert.Null(record.Body);
        Assert.DoesNotContain(await Host.RecordsAsync(), r => r.EventType == NotificationEventType.TeacherInvitation);
    }

    [Fact]
    public async Task A_new_teacher_completes_setup_through_the_common_flow_and_keeps_one_account_and_profile()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        var link = await LatestSetupLinkAsync(teacher.Email);
        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await holder.SignInAsync(teacher.Email)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);

        Assert.Equal(HttpStatusCode.Created, (await holder.SignInAsync(teacher.Email, NewPassword)).StatusCode);
        Assert.Equal(1, await UserCountAsync(teacher.Email));
        await Host.WithDbAsync(async db =>
        {
            var profile = await db.TeacherProfiles.SingleAsync(p => p.UserId == teacher.UserId);
            Assert.True(profile.IsActive);
        });
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task A_pending_teacher_can_be_reissued_through_user_management()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        var first = await LatestSetupLinkAsync(teacher.Email);

        Assert.Equal(HttpStatusCode.OK, (await Reissue(admin, teacher.UserId)).Status);
        var second = await LatestSetupLinkAsync(teacher.Email);

        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, first.UserId, first.Proof)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, second.UserId, second.Proof)).Status);
    }

    [Fact]
    public async Task A_rolled_back_teacher_requests_no_setup_email()
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
        Assert.Equal(0, await UserCountAsync(email));
    }

    [Fact]
    public async Task A_rejected_teacher_creation_requests_no_setup_email()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        var before = (await Host.RecordsAsync()).Count;

        var (duplicate, _) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Duplicate", email = teacher.Email });
        var (invalid, _) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "", email = "nope" });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate);
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.Equal(before, (await Host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task Teacher_creation_succeeds_during_a_provider_outage_and_a_replay_creates_no_duplicate()
    {
        using var admin = await AdminAsync();
        Host.Outage();

        var teacher = await CreateTeacherAsync(admin);
        await Host.FlushNotificationsAsync();
        var replay = await Host.RequestAsync(AccountCredentialSetupService.InitialRequest(teacher.UserId));

        Assert.Equal(1, replay.Duplicates);
        Assert.Single(await Host.RecordsAsync());
        Assert.Empty(Host.Emails.Messages);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(teacher.UserId)).AccountStatus);
    }

    [Fact]
    public async Task User_management_never_creates_or_assigns_Coordinator()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("coordinator");

        var (created, _) = await SendAsync(admin, HttpMethod.Post, Users, new { email, accountType = "Coordinator" });
        var account = await CreateAccountAsync(admin, PortalRoles.Admin);
        var (assigned, _) = await SendAsync(admin, HttpMethod.Put, $"{UserRoute(account.UserId)}/roles", new { roles = new[] { "Admin", "Coordinator" } });

        Assert.Equal(HttpStatusCode.Conflict, created);
        Assert.Equal(HttpStatusCode.BadRequest, assigned);
        Assert.Equal(0, await UserCountAsync(email));
        Assert.Equal([PortalRoles.Admin], await AssignedRolesAsync(account.UserId));
    }

    [Fact(Skip = "Release gate: the batch-coordinator assignment owner does not exist yet. When it does, prove it commits the new " +
        "Coordinator account and authoritative assignment, then requests AccountCredentialSetup.InitialRequest(userId), and that the " +
        "account stays PendingCredentialSetup until the setup link is used.")]
    public Task The_batch_assignment_owner_requests_the_common_setup_event_after_committing_a_new_Coordinator() => Task.CompletedTask;
}
