using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>US3: find teachers (search, status filter, numbered pages) and deactivate them without deleting anything.</summary>
public sealed class TeacherDirectoryAndDeactivationTests : TeacherTestBase
{
    private static string[] Names(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("displayName").GetString()!).ToArray();

    private static Task<(HttpStatusCode Status, JsonElement Body)> List(PortalClient admin, string query = "") =>
        SendAsync(admin, HttpMethod.Get, $"{Teachers}{query}");

    [Fact]
    public async Task The_list_is_ordered_by_name_and_numbered_with_counts()
    {
        using var admin = await AdminAsync();
        foreach (var name in new[] { "Charlie", "alice", "Bob", "Dora", "Eve" })
        {
            await CreateTeacherAsync(admin, name);
        }

        var (status, first) = await List(admin, "?pageSize=2&page=1");
        var (_, last) = await List(admin, "?pageSize=2&page=3");
        var (_, beyond) = await List(admin, "?pageSize=2&page=9");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["alice", "Bob"], Names(first));
        Assert.Equal(5, first.GetProperty("totalCount").GetInt64());
        Assert.Equal(3, first.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.Equal(["Eve"], Names(last));
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_matches_name_email_and_staff_id_ignoring_case()
    {
        using var admin = await AdminAsync();
        await CreateTeacherAsync(admin, "Grace Hopper", "grace.hopper@iit.test", "EMP-77");
        await CreateTeacherAsync(admin, "Alan Turing", "alan@iit.test", "EMP-88");

        Assert.Equal(["Grace Hopper"], Names((await List(admin, "?search=HOPPER")).Body));
        Assert.Equal(["Alan Turing"], Names((await List(admin, "?search=ALAN@IIT")).Body));
        Assert.Equal(["Alan Turing"], Names((await List(admin, "?search=emp-88")).Body));
        Assert.Equal(["Alan Turing", "Grace Hopper"], Names((await List(admin, "?search=%20%20")).Body));
        Assert.Empty((await List(admin, "?search=nobody")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task The_status_filter_separates_pending_active_and_deactivated_teachers()
    {
        using var admin = await AdminAsync();
        var pending = await CreateTeacherAsync(admin, "Pending One");
        var active = await CreateTeacherAsync(admin, "Active One");
        var gone = await CreateTeacherAsync(admin, "Gone One");
        await Factory.ActivateWithPasswordAsync(active.Email);
        await Deactivate(admin, gone.Id);

        Assert.Equal(["Pending One"], Names((await List(admin, "?status=PendingCredentialSetup")).Body));
        Assert.Equal(["Active One"], Names((await List(admin, "?status=active")).Body));
        Assert.Equal(["Gone One"], Names((await List(admin, "?status=Deactivated")).Body));
        Assert.Equal(3, (await List(admin)).Body.GetProperty("totalCount").GetInt64());
        Assert.NotEqual(pending.Id, active.Id);
    }

    [Fact]
    public async Task List_items_show_status_roles_and_designation_and_no_secrets()
    {
        using var admin = await AdminAsync();
        var designation = await CreateAsync(admin, "designations", new { name = "Lecturer" });
        await CreateTeacherAsync(admin, "Listed", designationId: designation);

        var (_, body) = await SendAsync(admin, HttpMethod.Get, Teachers);

        var item = body.GetProperty("items")[0];
        Assert.Equal("PendingCredentialSetup", item.GetProperty("accountStatus").GetString());
        Assert.Equal("Lecturer", item.GetProperty("designationName").GetString());
        Assert.Equal([PortalRoles.Teacher], Roles(item));
        var text = body.GetRawText();
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("?status=Retired", "status")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?page=0", "page")]
    public async Task Invalid_query_values_are_rejected_with_a_field_error(string query, string field)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await List(admin, query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field));
    }

    [Fact]
    public async Task Deactivation_disables_the_account_keeps_everything_and_revokes_every_session()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, "Leaving");
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        await Factory.ActivateWithPasswordAsync(teacher.Email);
        using var phone = Factory.CreatePortalClient();
        using var laptop = Factory.CreatePortalClient();
        await phone.SignInSuccessfullyAsync(teacher.Email);
        await laptop.SignInSuccessfullyAsync(teacher.Email);

        var (status, body) = await Deactivate(admin, teacher.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("isActive").GetBoolean());
        Assert.Equal("Deactivated", body.GetProperty("accountStatus").GetString());
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], Roles(body));
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.GetCurrentSessionAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await laptop.GetCurrentSessionAsync()).StatusCode);
        using var again = Factory.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await again.SignInAsync(teacher.Email)).StatusCode);
        await Factory.WithDbAsync(async db =>
        {
            Assert.Equal(1, await db.TeacherProfiles.CountAsync(p => p.Id == teacher.Id));
            Assert.Equal(1, await db.Users.CountAsync(u => u.Id == teacher.UserId));
            Assert.Equal(0, await db.AuthSessions.CountAsync(s => s.UserId == teacher.UserId && s.RevokedAt == null));
        });
    }

    [Fact]
    public async Task A_deactivated_teacher_is_still_listed_and_readable()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, "Still Here");
        await Deactivate(admin, teacher.Id);

        var (_, listed) = await List(admin, "?status=Deactivated");
        var (status, one) = await GetTeacher(admin, teacher.Id);

        Assert.Equal(["Still Here"], Names(listed));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(one.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Deactivating_is_audited_once_with_the_session_count_and_repeating_it_changes_nothing()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Factory.ActivateWithPasswordAsync(teacher.Email);
        using var session = Factory.CreatePortalClient();
        await session.SignInSuccessfullyAsync(teacher.Email);

        await Deactivate(admin, teacher.Id);
        var (repeat, _) = await Deactivate(admin, teacher.Id);

        Assert.Equal(HttpStatusCode.OK, repeat);
        var deactivated = Assert.Single(await EventsAsync("teacher.deactivated"));
        Assert.Equal(AdminUserId, deactivated.ActorUserId);
        Assert.Equal(teacher.Id.ToString(), deactivated.EntityId);
        Assert.Contains("\"sessionsRevoked\":1", deactivated.MetadataJson);
        Assert.Contains("Deactivated", deactivated.ChangesJson);
    }

    [Fact]
    public async Task Deactivating_through_an_update_does_the_same_in_one_request()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, "Via Update");

        var (status, body) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}",
            new { displayName = "Via Update Renamed", email = teacher.Email, isActive = false });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Via Update Renamed", body.GetProperty("displayName").GetString());
        Assert.Equal("Deactivated", body.GetProperty("accountStatus").GetString());
        Assert.Single(await EventsAsync("teacher.updated"));
        Assert.Single(await EventsAsync("teacher.deactivated"));
    }

    [Fact]
    public async Task The_last_active_Admin_cannot_be_deactivated_and_stays_signed_in()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        using var asTeacherAdmin = await SignInAsAdminAsync(teacher);
        await Factory.RemoveRoleAsync(AdminEmail, PortalRoles.Admin);

        var (status, problem) = await Deactivate(asTeacherAdmin, teacher.Id);
        var (viaUpdate, _) = await SendAsync(asTeacherAdmin, HttpMethod.Put, $"{Teachers}/{teacher.Id}",
            new { displayName = "Test Teacher", email = teacher.Email, isActive = false });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("last active Admin", problem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, viaUpdate);
        Assert.Equal(HttpStatusCode.OK, (await asTeacherAdmin.GetCurrentSessionAsync()).StatusCode);
        await Factory.WithDbAsync(async db =>
        {
            Assert.True((await db.TeacherProfiles.SingleAsync()).IsActive);
            Assert.Equal(TeacherAccountStatus.Active, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).AccountStatus);
        });
        Assert.Empty(await EventsAsync("teacher.deactivated"));
    }

    [Fact]
    public async Task A_teacher_who_is_Admin_can_be_deactivated_while_another_active_Admin_remains()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        await Factory.ActivateWithPasswordAsync(teacher.Email);

        var (status, _) = await Deactivate(admin, teacher.Id);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task There_is_no_delete_route_and_deactivating_an_unknown_teacher_is_not_found()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        using var delete = await admin.SendAsync(HttpMethod.Delete, $"{Teachers}/{teacher.Id}");
        var (missing, _) = await Deactivate(admin, Guid.NewGuid());

        Assert.True(delete.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"DELETE returned {delete.StatusCode}");
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal(1, await ProfileCountAsync());
    }

    [Fact]
    public async Task A_failed_audit_event_leaves_the_teacher_active_with_sessions_intact()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Factory.ActivateWithPasswordAsync(teacher.Email);
        using var session = Factory.CreatePortalClient();
        await session.SignInSuccessfullyAsync(teacher.Email);

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var lifecycle = new IitAcademicPortal.Application.Teachers.TeacherLifecycleService(
                provider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<PortalUser>>(),
                provider.GetRequiredService<IitAcademicPortal.Application.Abstractions.ITeacherStore>(),
                provider.GetRequiredService<IitAcademicPortal.Application.Abstractions.IAuthSessionRepository>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                provider.GetRequiredService<IitAcademicPortal.Application.Abstractions.IAuditActorDirectory>(),
                TimeProvider.System);

            await Assert.ThrowsAnyAsync<Exception>(() => lifecycle.DeactivateAsync(teacher.Id, CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.OK, (await session.GetCurrentSessionAsync()).StatusCode);
        await Factory.WithDbAsync(async db =>
        {
            Assert.True((await db.TeacherProfiles.SingleAsync()).IsActive);
            Assert.Equal(TeacherAccountStatus.Active, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).AccountStatus);
        });
    }
}
