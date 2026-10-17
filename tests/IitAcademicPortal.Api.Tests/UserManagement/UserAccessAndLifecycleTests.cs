using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Api.Tests.TeacherManagement;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>
/// US3: email changes, policy-limited role changes, and deactivation. Teacher accounts are delegated to Teacher Management, the last
/// active Admin is protected, an Admin cannot deactivate themselves, and every change commits with its audit event or not at all.
/// </summary>
public sealed class UserAccessAndLifecycleTests : UserManagementTestBase
{
    private static Task<(HttpStatusCode Status, JsonElement Body)> PutEmail(PortalClient admin, string userId, string email, string? stamp) =>
        SendAsync(admin, HttpMethod.Put, $"{UserRoute(userId)}/email", new { email, expectedConcurrencyStamp = stamp });

    private static Task<(HttpStatusCode Status, JsonElement Body)> PutRoles(PortalClient admin, string userId, string[]? roles, string? defaultRole = null) =>
        SendAsync(admin, HttpMethod.Put, $"{UserRoute(userId)}/roles", new { roles, defaultRole });

    private static Task<(HttpStatusCode Status, JsonElement Body)> Deactivate(PortalClient admin, string userId) =>
        SendAsync(admin, HttpMethod.Post, $"{UserRoute(userId)}/deactivation");

    private async Task<string> StampAsync(PortalClient admin, string userId) =>
        (await SendAsync(admin, HttpMethod.Get, UserRoute(userId))).Body.GetProperty("concurrencyStamp").GetString()!;

    [Fact]
    public async Task Anonymous_and_non_Admin_callers_cannot_change_email_roles_or_status()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var stamp = await StampAsync(admin, account.UserId);
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        var newEmail = PortalApiFactory.UniqueEmail("taken");

        foreach (var (client, expected) in new[] { (anonymous, HttpStatusCode.Unauthorized), (student, HttpStatusCode.Forbidden), (teacher, HttpStatusCode.Forbidden) })
        {
            Assert.Equal(expected, (await PutEmail(client, account.UserId, newEmail, stamp)).Status);
            Assert.Equal(expected, (await PutRoles(client, account.UserId, [PortalRoles.Student])).Status);
            Assert.Equal(expected, (await Deactivate(client, account.UserId)).Status);
        }

        var user = await UserAsync(account.UserId);
        Assert.Equal(account.Email, user.Email);
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
    }

    [Fact]
    public async Task An_email_change_keeps_the_user_id_is_audited_as_changed_only_and_invalidates_earlier_setup_links()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var oldLink = await LatestSetupLinkAsync(account.Email);
        var newEmail = PortalApiFactory.UniqueEmail("renamed");

        var (status, body) = await PutEmail(admin, account.UserId, $" {newEmail} ", await StampAsync(admin, account.UserId));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(account.UserId, body.GetProperty("userId").GetString());
        Assert.Equal(newEmail, body.GetProperty("email").GetString());
        var user = await UserAsync(account.UserId);
        Assert.Equal(newEmail, user.UserName);
        Assert.Equal(newEmail.ToUpperInvariant(), user.NormalizedEmail);
        var changed = Assert.Single(await EventsAsync("account.email.changed"));
        Assert.Equal(AdminUserId, changed.ActorUserId);
        NotificationTestHost.AssertNoSecret([changed.ChangesJson ?? "", changed.MetadataJson ?? ""], account.Email, newEmail);

        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, oldLink.UserId, oldLink.Proof)).Status);
        Assert.Equal(HttpStatusCode.OK, (await Reissue(admin, account.UserId)).Status);
        var fresh = await LatestSetupLinkAsync(newEmail);
        Assert.Equal(HttpStatusCode.NoContent, (await CompleteAsync(holder, fresh.UserId, fresh.Proof)).Status);
    }

    [Fact]
    public async Task A_duplicate_email_or_a_stale_stamp_is_a_409_and_nothing_changes()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var other = await CreateAccountAsync(admin, PortalRoles.Student);
        var stale = await StampAsync(admin, account.UserId);
        Assert.Equal(HttpStatusCode.OK, (await PutEmail(admin, account.UserId, PortalApiFactory.UniqueEmail("first"), stale)).Status);
        var current = await StampAsync(admin, account.UserId);
        var before = await UserAsync(account.UserId);

        var (duplicate, _) = await PutEmail(admin, account.UserId, other.Email.ToUpperInvariant(), current);
        var (staleStatus, problem) = await PutEmail(admin, account.UserId, PortalApiFactory.UniqueEmail("second"), stale);
        var (missing, missingProblem) = await PutEmail(admin, account.UserId, PortalApiFactory.UniqueEmail("third"), null);
        var (invalid, invalidProblem) = await PutEmail(admin, account.UserId, "not-an-email", current);
        var (unknown, _) = await PutEmail(admin, "no-such-account", PortalApiFactory.UniqueEmail("x"), current);

        Assert.Equal(HttpStatusCode.Conflict, duplicate);
        Assert.Equal(HttpStatusCode.Conflict, staleStatus);
        Assert.Equal(UserAccessService.StaleMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.True(HasError(missingProblem, "expectedConcurrencyStamp"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.True(HasError(invalidProblem, "email"));
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal(before.Email, (await UserAsync(account.UserId)).Email);
        Assert.Single(await EventsAsync("account.email.changed"));
    }

    [Fact]
    public async Task A_teacher_email_change_goes_through_teacher_management_and_keeps_the_profile()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        var newEmail = PortalApiFactory.UniqueEmail("teacher-renamed");

        var (status, body) = await PutEmail(admin, teacher.UserId, newEmail, await StampAsync(admin, teacher.UserId));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(newEmail, body.GetProperty("email").GetString());
        Assert.Single(await EventsAsync("teacher.updated"));
        Assert.Empty(await EventsAsync("account.email.changed"));
        await Host.WithDbAsync(async db => Assert.Equal("Setup Teacher", (await db.TeacherProfiles.SingleAsync(p => p.UserId == teacher.UserId)).DisplayName));
    }

    [Fact]
    public async Task A_Student_account_holds_only_Student()
    {
        using var admin = await AdminAsync();
        var student = await CreateAccountAsync(admin, PortalRoles.Student);

        var (withAdmin, problem) = await PutRoles(admin, student.UserId, [PortalRoles.Student, PortalRoles.Admin]);
        var (asAdmin, _) = await PutRoles(admin, student.UserId, [PortalRoles.Admin]);
        var (same, body) = await PutRoles(admin, student.UserId, [PortalRoles.Student], PortalRoles.Student);

        Assert.Equal(HttpStatusCode.BadRequest, withAdmin);
        Assert.True(HasError(problem, "roles"));
        Assert.Equal(HttpStatusCode.BadRequest, asAdmin);
        Assert.Equal(HttpStatusCode.OK, same);
        Assert.Equal([PortalRoles.Student], Roles(body));
        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(student.UserId));
        Assert.Empty(await EventsAsync("account.roles.changed"));
    }

    [Fact]
    public async Task A_generic_Admin_cannot_gain_Teacher_Student_or_Coordinator_here()
    {
        using var admin = await AdminAsync();
        var other = await CreateAccountAsync(admin, PortalRoles.Admin);

        var (teacher, _) = await PutRoles(admin, other.UserId, [PortalRoles.Admin, PortalRoles.Teacher]);
        var (student, _) = await PutRoles(admin, other.UserId, [PortalRoles.Admin, PortalRoles.Student]);
        var (coordinator, _) = await PutRoles(admin, other.UserId, [PortalRoles.Admin, PortalRoles.Coordinator]);
        var (unsupported, _) = await PutRoles(admin, other.UserId, [PortalRoles.Admin, "Guest"]);
        var (empty, _) = await PutRoles(admin, other.UserId, []);
        var (badDefault, _) = await PutRoles(admin, other.UserId, [PortalRoles.Admin], PortalRoles.Teacher);

        Assert.Equal(HttpStatusCode.Conflict, teacher);
        Assert.Equal(HttpStatusCode.BadRequest, student);
        Assert.Equal(HttpStatusCode.BadRequest, coordinator);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported);
        Assert.Equal(HttpStatusCode.BadRequest, empty);
        Assert.Equal(HttpStatusCode.BadRequest, badDefault);
        Assert.Equal([PortalRoles.Admin], await AssignedRolesAsync(other.UserId));
    }

    [Fact]
    public async Task Teacher_roles_are_delegated_Admin_can_be_added_and_removed_and_Teacher_is_kept()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (added, addedBody) = await PutRoles(admin, teacher.UserId, [PortalRoles.Teacher, PortalRoles.Admin], PortalRoles.Admin);
        var (withoutTeacher, _) = await PutRoles(admin, teacher.UserId, [PortalRoles.Admin]);
        var (removed, removedBody) = await PutRoles(admin, teacher.UserId, [PortalRoles.Teacher]);

        Assert.Equal(HttpStatusCode.OK, added);
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], Roles(addedBody));
        Assert.Equal(PortalRoles.Admin, addedBody.GetProperty("defaultRole").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, withoutTeacher);
        Assert.Equal(HttpStatusCode.OK, removed);
        Assert.Equal([PortalRoles.Teacher], Roles(removedBody));
        Assert.Equal(PortalRoles.Teacher, removedBody.GetProperty("defaultRole").GetString());
        Assert.Equal(2, (await EventsAsync("teacher.roles.changed")).Count);
        Assert.Empty(await EventsAsync("account.roles.changed"));
    }

    [Fact]
    public async Task A_derived_Coordinator_role_is_preserved_through_a_teacher_role_change()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Host.WithDbAsync(async db =>
        {
            var coordinatorRole = await db.Roles.SingleAsync(r => r.Name == PortalRoles.Coordinator);
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = teacher.UserId, RoleId = coordinatorRole.Id });
            await db.SaveChangesAsync();
        });

        var (status, body) = await PutRoles(admin, teacher.UserId, [PortalRoles.Teacher, PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher, PortalRoles.Coordinator], Roles(body));
    }

    [Fact]
    public async Task Deactivation_keeps_the_account_roles_and_history_blocks_sign_in_and_revokes_every_session()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await Host.CreateUserAsync(email, PortalRoles.Student);
        using var first = Host.CreatePortalClient();
        using var second = Host.CreatePortalClient();
        await first.SignInSuccessfullyAsync(email);
        await second.SignInSuccessfullyAsync(email);

        var (status, body) = await Deactivate(admin, userId);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Deactivated", body.GetProperty("accountStatus").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await first.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.GetCurrentSessionAsync()).StatusCode);
        using var again = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.SignInAsync(email)).StatusCode);
        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(userId));
        var deactivated = Assert.Single(await EventsAsync("account.deactivated"));
        Assert.Equal(AdminUserId, deactivated.ActorUserId);
        Assert.Contains("\"sessionsRevoked\":2", deactivated.MetadataJson);
        await Host.WithDbAsync(async db => Assert.All(
            await db.AuthSessions.Where(s => s.UserId == userId).ToListAsync(), s => Assert.Equal(IitAcademicPortal.Domain.Sessions.SessionRevocationReason.AccountDeactivated, s.RevocationReason)));

        // Repeating changes nothing and records nothing.
        Assert.Equal(HttpStatusCode.OK, (await Deactivate(admin, userId)).Status);
        Assert.Single(await EventsAsync("account.deactivated"));
    }

    [Fact]
    public async Task An_Admin_cannot_deactivate_their_own_account_even_with_another_active_Admin()
    {
        using var admin = await AdminAsync();
        await Host.CreateUserAsync(PortalApiFactory.UniqueEmail("other-admin"), PortalRoles.Admin);

        var (status, problem) = await Deactivate(admin, AdminUserId);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(UserAccessService.SelfDeactivationMessage, problem.GetProperty("detail").GetString());
        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(AdminUserId)).AccountStatus);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetCurrentSessionAsync()).StatusCode);
    }

    [Fact]
    public async Task The_last_active_Admin_cannot_be_deactivated()
    {
        // Through the API the caller is always another active Admin, so the rule is exercised directly: the only active Admin is the
        // target, and the service runs outside a request (no caller), as a background owner would.
        var lastEmail = PortalApiFactory.UniqueEmail("last");
        var last = await Host.CreateUserAsync(lastEmail, PortalRoles.Admin);
        var inactiveEmail = PortalApiFactory.UniqueEmail("inactive-admin");
        await Host.CreateUserAsync(inactiveEmail, PortalRoles.Admin);
        await Host.SetAccountStatusAsync(inactiveEmail, TeacherAccountStatus.Deactivated);

        await using var scope = Host.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<UserAccessService>().DeactivateAsync(last, CancellationToken.None);

        Assert.Equal(AccountOutcome.Conflict, result.Outcome);
        Assert.Equal(TeacherLifecycleService.LastAdminMessage, result.Message);
        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(last)).AccountStatus);
        Assert.Empty(await EventsAsync("account.deactivated"));
    }

    [Fact]
    public async Task Another_Admin_can_be_deactivated_while_the_caller_stays_active()
    {
        using var admin = await AdminAsync();
        var other = await Host.CreateUserAsync(PortalApiFactory.UniqueEmail("other-admin"), PortalRoles.Admin);

        var (status, _) = await Deactivate(admin, other);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(TeacherAccountStatus.Deactivated, (await UserAsync(other)).AccountStatus);
        Assert.Equal([PortalRoles.Admin], await AssignedRolesAsync(other));
    }

    [Fact]
    public async Task A_teacher_is_deactivated_through_teacher_management_so_the_profile_follows()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, body) = await Deactivate(admin, teacher.UserId);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Deactivated", body.GetProperty("accountStatus").GetString());
        Assert.Single(await EventsAsync("teacher.deactivated"));
        Assert.Empty(await EventsAsync("account.deactivated"));
        await Host.WithDbAsync(async db => Assert.False((await db.TeacherProfiles.SingleAsync(p => p.UserId == teacher.UserId)).IsActive));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task A_pending_account_can_be_deactivated_and_its_setup_link_then_fails()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        var link = await LatestSetupLinkAsync(account.Email);

        Assert.Equal(HttpStatusCode.OK, (await Deactivate(admin, account.UserId)).Status);

        using var holder = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await CompleteAsync(holder, link.UserId, link.Proof)).Status);
        Assert.Equal(TeacherAccountStatus.Deactivated, (await UserAsync(account.UserId)).AccountStatus);
    }

    [Fact]
    public async Task An_audit_failure_rolls_back_a_deactivation_and_its_session_revocation()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await Host.CreateUserAsync(email, PortalRoles.Student);
        using var session = Host.CreatePortalClient();
        await session.SignInSuccessfullyAsync(email);

        await using (var scope = Host.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var service = new UserAccessService(
                provider.GetRequiredService<UserManager<PortalUser>>(),
                provider.GetRequiredService<ITeacherStore>(),
                provider.GetRequiredService<IUserAccountStore>(),
                provider.GetRequiredService<IAuditActorDirectory>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                provider.GetRequiredService<IAuditRequestContext>(),
                provider.GetRequiredService<IAuthSessionRepository>(),
                provider.GetRequiredService<TeacherProfileService>(),
                provider.GetRequiredService<TeacherRoleService>(),
                provider.GetRequiredService<TeacherLifecycleService>(),
                provider.GetRequiredService<TimeProvider>());

            await Assert.ThrowsAnyAsync<Exception>(() => service.DeactivateAsync(userId, CancellationToken.None));
        }

        Assert.Equal(TeacherAccountStatus.Active, (await UserAsync(userId)).AccountStatus);
        Assert.Equal(HttpStatusCode.OK, (await session.GetCurrentSessionAsync()).StatusCode);
        Assert.Empty(await EventsAsync("account.deactivated"));
    }
}
