using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>
/// US2: profile maintenance and the role policy. Teacher stays assigned while active, Admin may be assigned or removed, Student
/// is refused, Coordinator is not editable here, the default role stays assigned, and the last active Admin is protected.
/// </summary>
public sealed class TeacherRoleTests : TeacherTestBase
{
    [Fact]
    public async Task Admin_can_be_assigned_and_the_default_role_stays_Teacher_unless_changed()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, body) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], Roles(body));
        Assert.Equal(PortalRoles.Teacher, body.GetProperty("defaultRole").GetString());
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task The_default_role_can_be_any_assigned_role()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, body) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin], PortalRoles.Admin);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(PortalRoles.Admin, body.GetProperty("defaultRole").GetString());
    }

    [Fact]
    public async Task Removing_Admin_resets_a_default_of_Admin_to_a_role_that_is_still_assigned()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin], PortalRoles.Admin);

        var (status, body) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([PortalRoles.Teacher], Roles(body));
        Assert.Equal(PortalRoles.Teacher, body.GetProperty("defaultRole").GetString());
    }

    [Fact]
    public async Task Student_is_never_assigned_and_the_roles_stay_as_they_were()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, problem) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Student]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Student cannot be assigned to a teacher.", FirstError(problem, "roles"));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task Coordinator_cannot_be_set_through_the_role_endpoint()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, problem) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Coordinator]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "roles"));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task Teacher_cannot_be_removed_while_the_profile_is_active()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, problem) = await PutRoles(admin, teacher.Id, [PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Teacher is required while the teacher profile is active.", FirstError(problem, "roles"));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Theory]
    [InlineData("Dean")]
    [InlineData("")]
    public async Task An_unsupported_role_is_rejected(string role)
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, problem) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, role]);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "roles"));
    }

    [Fact]
    public async Task Missing_roles_and_an_unassigned_default_role_are_rejected_without_changes()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (noRoles, noRolesProblem) = await PutRoles(admin, teacher.Id, null);
        var (badDefault, badDefaultProblem) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher], PortalRoles.Admin);
        var (unsupported, unsupportedProblem) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher], "Dean");

        Assert.Equal(HttpStatusCode.BadRequest, noRoles);
        Assert.True(HasError(noRolesProblem, "roles"));
        Assert.Equal(HttpStatusCode.BadRequest, badDefault);
        Assert.Equal("The default role must be one of the assigned roles.", FirstError(badDefaultProblem, "defaultRole"));
        Assert.Equal(HttpStatusCode.BadRequest, unsupported);
        Assert.True(HasError(unsupportedProblem, "defaultRole"));
        Assert.Empty(await EventsAsync("teacher.roles.changed"));
    }

    [Fact]
    public async Task A_held_Coordinator_role_is_kept_when_the_editable_roles_change()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Factory.WithDbAsync(async db =>
        {
            var users = db.Users.Where(u => u.Id == teacher.UserId);
            var roleId = await db.Roles.Where(r => r.Name == PortalRoles.Coordinator).Select(r => r.Id).SingleAsync();
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = teacher.UserId, RoleId = roleId });
            await db.SaveChangesAsync();
        });

        var (status, body) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher, PortalRoles.Coordinator], Roles(body));
    }

    [Fact]
    public async Task A_role_change_is_audited_once_and_repeating_it_changes_nothing()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        var (repeat, _) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.OK, repeat);
        var changed = Assert.Single(await EventsAsync("teacher.roles.changed"));
        Assert.Equal(AdminUserId, changed.ActorUserId);
        Assert.Equal(teacher.Id.ToString(), changed.EntityId);
        Assert.Contains("Admin, Teacher", changed.ChangesJson);
    }

    [Fact]
    public async Task A_change_to_the_roles_takes_effect_on_the_teachers_next_request()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        using var asAdmin = await SignInAsAdminAsync(teacher);
        Assert.Equal(HttpStatusCode.OK, (await asAdmin.GetAsync($"{Teachers}/{teacher.Id}")).StatusCode);

        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher]);

        // The session was in the Admin role, which is no longer assigned, so it falls back to Teacher and loses access at once.
        Assert.Equal(HttpStatusCode.Forbidden, (await asAdmin.GetAsync($"{Teachers}/{teacher.Id}")).StatusCode);
    }

    [Fact]
    public async Task The_roles_of_a_deactivated_teacher_cannot_be_changed()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Deactivate(admin, teacher.Id);

        var (status, _) = await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task The_last_active_Admin_cannot_lose_the_Admin_role_and_nothing_changes()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin], PortalRoles.Admin);
        using var asTeacherAdmin = await SignInAsAdminAsync(teacher);
        await Factory.RemoveRoleAsync(AdminEmail, PortalRoles.Admin);
        var eventsBefore = (await EventsAsync("teacher.roles.changed")).Count;

        var (status, problem) = await PutRoles(asTeacherAdmin, teacher.Id, [PortalRoles.Teacher]);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("last active Admin", problem.GetProperty("detail").GetString());
        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
        Assert.Equal(eventsBefore, (await EventsAsync("teacher.roles.changed")).Count);
    }

    [Fact]
    public async Task An_Admin_can_remove_their_own_Admin_role_while_another_active_Admin_remains()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        using var asTeacherAdmin = await SignInAsAdminAsync(teacher);

        var (status, _) = await PutRoles(asTeacherAdmin, teacher.Id, [PortalRoles.Teacher]);

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task A_pending_Admin_does_not_count_as_an_active_Admin_for_the_last_Admin_rule()
    {
        using var admin = await AdminAsync();
        var active = await CreateTeacherAsync(admin);
        var pending = await CreateTeacherAsync(admin);
        await PutRoles(admin, active.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        await PutRoles(admin, pending.Id, [PortalRoles.Teacher, PortalRoles.Admin]);
        using var asActive = await SignInAsAdminAsync(active);
        await Factory.RemoveRoleAsync(AdminEmail, PortalRoles.Admin);

        // The only other Admin is still pending credential setup, so it cannot take over: the rule protects `active`.
        var (status, _) = await PutRoles(asActive, active.Id, [PortalRoles.Teacher]);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task A_failed_audit_event_rolls_back_the_role_change()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var service = new TeacherRoleService(
                scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>(),
                scope.ServiceProvider.GetRequiredService<ITeacherStore>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                scope.ServiceProvider.GetRequiredService<IAuditActorDirectory>());

            await Assert.ThrowsAnyAsync<Exception>(() => service.UpdateAsync(
                teacher.Id, new TeacherRolesInput([PortalRoles.Teacher, PortalRoles.Admin], PortalRoles.Admin), CancellationToken.None));
        }

        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
        await Factory.WithDbAsync(async db =>
            Assert.Equal(PortalRoles.Teacher, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).DefaultRole));
    }
}
