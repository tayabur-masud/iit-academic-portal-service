using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.TeacherManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.Students;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// US1 integrity: the first account, profile, admission, and their audit events commit together or not at all; the setup email is
/// requested only after a new account commits; a returning Student's account is never touched; and activation follows the existing
/// credential-setup flow.
/// </summary>
public sealed class StudentProvisioningTransactionTests : StudentTestBase
{
    private StudentAdmissionService ServiceWith(IServiceProvider provider, IAuditEventRecorder recorder) => new(
        provider.GetRequiredService<UserManager<PortalUser>>(),
        provider.GetRequiredService<IStudentStore>(),
        recorder,
        provider.GetRequiredService<IAuditActorDirectory>(),
        provider.GetRequiredService<INotificationRequestService>(),
        provider.GetRequiredService<TimeProvider>());

    private static StudentAdmissionInput Input(string email, Guid degreeId, Guid batchId) => new(
        email, "Rolled Back", null, null, null, null, null, null, "01700000000", null, null, null, null, null, null,
        degreeId, batchId, "S-1", "R-1", null, null, null, [new AcademicHistoryInput(null, "Master", null, null, null, null, null, "DU", null, null, "2020", null, null, null, null, null)]);

    [Fact]
    public async Task A_failed_audit_write_leaves_no_account_profile_admission_history_or_setup_request()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var before = await CountsAsync();
        var email = Unique("rollback");

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var service = ServiceWith(scope.ServiceProvider, new TeacherProvisioningTests.ThrowingRecorder());
            await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(Input(email, refs.Pgdit, refs.PgditBatch), CancellationToken.None));
        }

        Assert.Equal(before, await CountsAsync());
        Assert.Equal(0, await UserCountAsync(email));
        await Host.WithDbAsync(async db => Assert.Equal(0, await db.StudentAcademicHistoryEntries.CountAsync()));
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task A_failed_returning_admission_changes_neither_the_profile_nor_the_account()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var email = Unique("returning");
        var first = await AdmitAsync(admin, Form(email, refs.Pgdit, refs.PgditBatch));
        var stamp = (await UserAsync(first.UserId)).ConcurrencyStamp;

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var service = ServiceWith(scope.ServiceProvider, new TeacherProvisioningTests.ThrowingRecorder());
            await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(Input(email, refs.Emit, refs.EmitBatch), CancellationToken.None));
        }

        Assert.Equal((1, 1), ((await CountsAsync()).Profiles, (await CountsAsync()).Admissions));
        await Host.WithDbAsync(async db => Assert.Equal("Ayesha Rahman", (await db.StudentProfiles.SingleAsync()).ApplicantName));
        Assert.Equal(stamp, (await UserAsync(first.UserId)).ConcurrencyStamp);
    }

    [Fact]
    public async Task Creation_is_audited_for_the_account_profile_and_admission_without_personal_values()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("audited"), refs.Pgdit, refs.PgditBatch);
        form["mailingAddress"] = new { details = "House 77, Secret Lane", districtId = refs.District, upazilaId = refs.Upazila };

        var admission = await AdmitAsync(admin, form);

        var account = Assert.Single(await EventsAsync("account.created"));
        var profile = Assert.Single(await EventsAsync("student.profile.created"));
        var created = Assert.Single(await EventsAsync("student.admission.created"));
        Assert.Equal(admission.UserId, account.EntityId);
        Assert.Equal(admission.ProfileId.ToString(), profile.EntityId);
        Assert.Equal(admission.Id.ToString(), created.EntityId);
        Assert.All([account, profile, created], e => Assert.Equal(AdminUserId, e.ActorUserId));
        Assert.Contains(refs.Pgdit.ToString(), created.ChangesJson);
        Assert.Contains("\"accountCreated\":true", created.MetadataJson);
        NotificationTestHost.AssertNoSecret(
            [profile.ChangesJson ?? "", created.ChangesJson ?? "", profile.MetadataJson ?? "", created.MetadataJson ?? "", account.ChangesJson ?? ""],
            "Ayesha", "Abdur", "Salma", "+8801711000000", "Secret Lane", "1998-04-12", admission.Email);
    }

    [Fact]
    public async Task The_new_Student_activates_only_through_the_setup_link_and_a_later_admission_keeps_the_password_and_status()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        var first = await AdmitAsync(admin, Form(Unique("activate"), refs.Pgdit, refs.PgditBatch));
        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await holder.SignInAsync(first.Email)).StatusCode);

        var link = await LatestSetupLinkAsync(first.Email);
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, link.UserId, link.Proof + "x")).Status);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(first.UserId)).AccountStatus);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
        Assert.Equal(HttpStatusCode.Created, (await holder.SignInAsync(first.Email, NewPassword)).StatusCode);
        var active = await UserAsync(first.UserId);

        var (status, body) = await CreateAdmission(admin, Form(first.Email, refs.Emit, refs.EmitBatch));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("setupEmailRequested").GetBoolean());
        var after = await UserAsync(first.UserId);
        Assert.Equal(TeacherAccountStatus.Active, after.AccountStatus);
        Assert.Equal(active.PasswordHash, after.PasswordHash);
        Assert.Equal(active.SecurityStamp, after.SecurityStamp);
        Assert.Single(await Host.RecordsAsync(), r => r.EventType == NotificationEventType.AccountCredentialSetup);
    }

    [Fact]
    public async Task Creation_succeeds_when_the_setup_email_cannot_be_queued_and_the_account_stays_pending()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        Host.Fault.FailAccept = true;

        var (status, body) = await CreateAdmission(admin, Form(Unique("outage"), refs.Pgdit, refs.PgditBatch));

        Host.Fault.FailAccept = false;
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("accountCreated").GetBoolean());
        Assert.False(body.GetProperty("setupEmailRequested").GetBoolean());
        var userId = body.GetProperty("admission").GetProperty("profile").GetProperty("userId").GetString()!;
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(userId)).AccountStatus);
        Assert.Contains(Host.Signals.PersistenceFailures, f => f.StartsWith($"AccountCredentialSetup|{userId}|", StringComparison.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await Reissue(admin, userId)).Status);
    }

    [Fact]
    public async Task A_failed_delivery_leaves_the_new_Student_pending()
    {
        var refs = await RefsAsync();
        using var admin = await AdminAsync();
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent();

        var admission = await AdmitAsync(admin, Form(Unique("bounce"), refs.Pgdit, refs.PgditBatch));
        await Host.FlushNotificationsAsync();

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(record.Id)).Status);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(admission.UserId)).AccountStatus);
    }
}
