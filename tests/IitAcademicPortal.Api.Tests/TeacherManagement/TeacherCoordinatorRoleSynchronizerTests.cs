using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>
/// The internal Coordinator-role contract, tested directly. This does not prove any assignment workflow calls it (none exists
/// yet); that stays a gate for the future owner of batch-coordinator assignments.
/// </summary>
public sealed class TeacherCoordinatorRoleSynchronizerTests : TeacherTestBase
{
    /// <summary>Runs the synchronizer the way a caller does: in its own unit of work, then saves, as the assignment workflow would.</summary>
    private async Task<CoordinatorSyncOutcome> SyncAsync(string userId, bool hasAssignments, bool save = true)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var synchronizer = scope.ServiceProvider.GetRequiredService<ITeacherCoordinatorRoleSynchronizer>();
        var outcome = await synchronizer.SynchronizeCoordinatorRoleAsync(userId, hasAssignments, CancellationToken.None);
        if (save)
        {
            await scope.ServiceProvider.GetRequiredService<ITeacherStore>().SaveAsync(CancellationToken.None);
        }

        return outcome;
    }

    private async Task<Teacher> TeacherAsync()
    {
        using var admin = await AdminAsync();
        return await CreateTeacherAsync(admin);
    }

    [Fact]
    public async Task An_active_assignment_adds_Coordinator_and_none_removes_it()
    {
        var teacher = await TeacherAsync();

        Assert.Equal(CoordinatorSyncOutcome.Added, await SyncAsync(teacher.UserId, true));
        Assert.Equal([PortalRoles.Teacher, PortalRoles.Coordinator], await AssignedRolesAsync(teacher.UserId));

        Assert.Equal(CoordinatorSyncOutcome.Removed, await SyncAsync(teacher.UserId, false));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task Coordinator_stays_while_any_assignment_remains()
    {
        var teacher = await TeacherAsync();
        await SyncAsync(teacher.UserId, true);

        // A second assignment, then one of the two ends: the caller reports that one still remains.
        Assert.Equal(CoordinatorSyncOutcome.Unchanged, await SyncAsync(teacher.UserId, true));
        Assert.Equal(CoordinatorSyncOutcome.Unchanged, await SyncAsync(teacher.UserId, true));
        Assert.Contains(PortalRoles.Coordinator, await AssignedRolesAsync(teacher.UserId));
        Assert.Equal(CoordinatorSyncOutcome.Removed, await SyncAsync(teacher.UserId, false));
    }

    [Fact]
    public async Task Repeating_a_call_changes_and_audits_nothing()
    {
        var teacher = await TeacherAsync();

        await SyncAsync(teacher.UserId, true);
        await SyncAsync(teacher.UserId, true);
        await SyncAsync(teacher.UserId, false);
        await SyncAsync(teacher.UserId, false);

        var events = await EventsAsync("teacher.coordinator-role.synchronized");
        Assert.Equal(2, events.Count);
        Assert.Contains("Teacher, Coordinator", events[0].ChangesJson);
    }

    [Fact]
    public async Task Teacher_and_Admin_are_never_touched_and_Student_is_never_assigned()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await PutRoles(admin, teacher.Id, [PortalRoles.Teacher, PortalRoles.Admin]);

        await SyncAsync(teacher.UserId, true);
        await SyncAsync(teacher.UserId, false);

        Assert.Equal([PortalRoles.Admin, PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task Removing_Coordinator_corrects_a_default_role_that_is_no_longer_assigned()
    {
        var teacher = await TeacherAsync();
        await SyncAsync(teacher.UserId, true);
        await Factory.WithDbAsync(async db =>
        {
            (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).DefaultRole = PortalRoles.Coordinator;
            await db.SaveChangesAsync();
        });

        await SyncAsync(teacher.UserId, false);

        await Factory.WithDbAsync(async db =>
            Assert.Equal(PortalRoles.Teacher, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).DefaultRole));
        var removed = (await EventsAsync("teacher.coordinator-role.synchronized")).Last();
        Assert.Contains("defaultRole", removed.ChangesJson);
    }

    [Fact]
    public async Task Adding_Coordinator_leaves_the_default_role_alone()
    {
        var teacher = await TeacherAsync();

        await SyncAsync(teacher.UserId, true);

        await Factory.WithDbAsync(async db =>
            Assert.Equal(PortalRoles.Teacher, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).DefaultRole));
    }

    [Fact]
    public async Task An_unknown_user_or_an_account_without_a_teacher_profile_is_rejected_and_nothing_changes()
    {
        var student = PortalApiFactory.UniqueEmail("student");
        var studentId = await Factory.CreateUserAsync(student, PortalRoles.Student);

        await Assert.ThrowsAsync<CoordinatorSyncException>(() => SyncAsync("no-such-user", true));
        await Assert.ThrowsAsync<CoordinatorSyncException>(() => SyncAsync(studentId, true));

        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(studentId));
        Assert.Empty(await EventsAsync("teacher.coordinator-role.synchronized"));
    }

    [Fact]
    public async Task A_deactivated_teacher_cannot_become_a_Coordinator_but_can_lose_the_role()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await SyncAsync(teacher.UserId, true);
        await Deactivate(admin, teacher.Id);

        await Assert.ThrowsAsync<CoordinatorSyncException>(() => SyncAsync(teacher.UserId, true));
        Assert.Equal(CoordinatorSyncOutcome.Removed, await SyncAsync(teacher.UserId, false));
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
    }

    [Fact]
    public async Task The_role_and_its_audit_event_commit_with_the_callers_save_or_not_at_all()
    {
        var teacher = await TeacherAsync();

        // The caller's assignment change failed and it never saved: neither the role nor the event may remain.
        Assert.Equal(CoordinatorSyncOutcome.Added, await SyncAsync(teacher.UserId, true, save: false));

        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(teacher.UserId));
        Assert.Empty(await EventsAsync("teacher.coordinator-role.synchronized"));
    }

    [Fact]
    public async Task The_event_records_only_approved_identifiers_and_no_batch_or_student_data()
    {
        var teacher = await TeacherAsync();

        await SyncAsync(teacher.UserId, true);

        var added = Assert.Single(await EventsAsync("teacher.coordinator-role.synchronized"));
        Assert.Equal(teacher.Id.ToString(), added.EntityId);
        Assert.Contains(teacher.UserId, added.MetadataJson);
        Assert.DoesNotContain(teacher.Email, added.ChangesJson);
        Assert.Null(added.ActorUserId);
    }
}
