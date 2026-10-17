using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.TeacherManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>US2: an active Admin provisions a generic Admin or Student account, pending and passwordless, with one setup email.</summary>
public sealed class UserProvisioningTests : UserManagementTestBase
{
    [Theory]
    [InlineData("Admin")]
    [InlineData("Student")]
    [InlineData("student")]
    public async Task A_generic_account_is_pending_without_a_password_and_holds_only_its_role_as_default(string accountType)
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("new");

        using var response = await admin.SendAsync(HttpMethod.Post, Users, new { email = $"  {email} ", accountType });
        var text = await response.Content.ReadAsStringAsync();
        var body = await NotificationTestHost.ReadAsync(response);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var userId = body.GetProperty("userId").GetString()!;
        var role = PortalRoles.All.Single(r => string.Equals(r, accountType, StringComparison.OrdinalIgnoreCase));
        Assert.Equal($"/api/admin/users/{userId}", response.Headers.Location!.OriginalString);
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("PendingCredentialSetup", body.GetProperty("accountStatus").GetString());
        Assert.Equal([role], Roles(body));
        Assert.Equal(role, body.GetProperty("defaultRole").GetString());
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("link", text, StringComparison.OrdinalIgnoreCase);

        var user = await UserAsync(userId);
        Assert.Null(user.PasswordHash);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
        Assert.Equal([role], await AssignedRolesAsync(userId));
        using var client = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(email)).StatusCode);
    }

    [Fact]
    public async Task Creation_is_audited_with_safe_fields_and_requests_one_setup_email_after_commit()
    {
        using var admin = await AdminAsync();

        var account = await CreateAccountAsync(admin, PortalRoles.Student);

        var created = Assert.Single(await EventsAsync("account.created"));
        Assert.Equal(AdminUserId, created.ActorUserId);
        Assert.Equal("UserAccount", created.EntityType);
        Assert.Equal(account.UserId, created.EntityId);
        Assert.DoesNotContain(account.Email, created.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PendingCredentialSetup", created.ChangesJson);

        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationEventType.AccountCredentialSetup, record.EventType);
        Assert.Equal(account.UserId, record.RecipientUserId);
        Assert.Equal(account.UserId, record.OccurrenceId);
        Assert.True(record.ContentWithheld);
        Assert.Null(record.Body);
    }

    [Fact]
    public async Task A_duplicate_normalized_email_is_a_409_with_no_second_account_role_audit_or_email()
    {
        using var admin = await AdminAsync();
        var existing = await CreateAccountAsync(admin, PortalRoles.Student);
        var records = (await Host.RecordsAsync()).Count;

        var (status, problem) = await SendAsync(admin, HttpMethod.Post, Users, new { email = existing.Email.ToUpperInvariant(), accountType = "Admin" });
        var (againstExisting, _) = await SendAsync(admin, HttpMethod.Post, Users, new { email = (await UserAsync(AdminUserId)).Email, accountType = "Student" });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(UserProvisioningService.DuplicateEmailMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, againstExisting);
        Assert.Equal(1, await UserCountAsync(existing.Email));
        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(existing.UserId));
        Assert.Single(await EventsAsync("account.created"));
        Assert.Equal(records, (await Host.RecordsAsync()).Count);
    }

    [Theory]
    [InlineData("", "Student", "email")]
    [InlineData("not-an-email", "Student", "email")]
    [InlineData("valid@iit.test", "", "accountType")]
    [InlineData("valid@iit.test", "Guest", "accountType")]
    public async Task Invalid_email_or_account_type_is_a_field_keyed_400(string email, string accountType, string field)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Post, Users, new { email, accountType });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field), problem.ToString());
        Assert.Empty(await Host.RecordsAsync());
    }

    [Theory]
    [InlineData("Teacher")]
    [InlineData("Coordinator")]
    public async Task Teacher_and_Coordinator_accounts_belong_to_their_owners_and_are_refused(string accountType)
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("owned");

        var (status, _) = await SendAsync(admin, HttpMethod.Post, Users, new { email, accountType });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(0, await UserCountAsync(email));
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task Unknown_fields_such_as_a_password_are_rejected()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("pw");

        var (status, _) = await SendAsync(admin, HttpMethod.Post, Users, new { email, accountType = "Student", password = "Chosen-pass9" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(0, await UserCountAsync(email));
    }

    [Fact]
    public async Task Anonymous_and_non_Admin_callers_cannot_create_and_nothing_is_written()
    {
        using var admin = await AdminAsync();
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        var email = PortalApiFactory.UniqueEmail("denied");

        var (anonymousStatus, _) = await SendAsync(anonymous, HttpMethod.Post, Users, new { email, accountType = "Admin" });
        var (studentStatus, _) = await SendAsync(student, HttpMethod.Post, Users, new { email, accountType = "Admin" });
        var (teacherStatus, _) = await SendAsync(teacher, HttpMethod.Post, Users, new { email, accountType = "Admin" });
        using var withoutToken = await admin.SendAsync(HttpMethod.Post, Users, new { email, accountType = "Admin" }, withAntiforgery: false);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
        Assert.Equal(HttpStatusCode.Forbidden, studentStatus);
        Assert.Equal(HttpStatusCode.Forbidden, teacherStatus);
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
        Assert.Equal(0, await UserCountAsync(email));
    }

    [Fact]
    public async Task An_audit_failure_rolls_back_the_account_and_role_and_requests_no_email()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("rollback");
        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var service = new UserProvisioningService(
                scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>(),
                scope.ServiceProvider.GetRequiredService<ITeacherStore>(),
                scope.ServiceProvider.GetRequiredService<IUserAccountStore>(),
                scope.ServiceProvider.GetRequiredService<IAuditActorDirectory>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                scope.ServiceProvider.GetRequiredService<INotificationRequestService>());

            await Assert.ThrowsAnyAsync<Exception>(() => service.CreateAsync(new CreateAccountInput(email, "Student"), CancellationToken.None));
        }

        Assert.Equal(0, await UserCountAsync(email));
        await Host.WithDbAsync(async db => Assert.Equal(1, await db.UserRoles.CountAsync()));
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task Creation_succeeds_while_the_notification_store_is_down_and_the_account_stays_pending()
    {
        using var admin = await AdminAsync();
        Host.Fault.FailAccept = true;

        var account = await CreateAccountAsync(admin, PortalRoles.Admin);

        Host.Fault.FailAccept = false;
        Assert.Empty(await Host.RecordsAsync());
        Assert.Contains(Host.Signals.PersistenceFailures, f => f.Contains(nameof(NotificationEventType.AccountCredentialSetup)));
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await UserAsync(account.UserId)).AccountStatus);
    }
}
