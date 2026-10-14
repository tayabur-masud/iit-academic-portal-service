using System.Net;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>US2: Admins update teacher profile fields and the account email; the account identity stays the same.</summary>
public sealed class TeacherProfileTests : TeacherTestBase
{
    private static object Edit(string name = "New Name", string? email = null, string? staffId = null, string? phone = null, Guid? designationId = null, bool? isActive = null) =>
        new { displayName = name, email, employeeStaffId = staffId, phone, designationId, isActive };

    [Fact]
    public async Task Updating_changes_the_profile_and_keeps_the_same_account()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Old Name", staffId: "S-1");
        var designation = await CreateAsync(admin, "designations", new { name = "Reader" });

        var (status, body) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}",
            Edit("  New   Name ", teacher.Email, " S-2 ", "0123", designation));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("New   Name", body.GetProperty("displayName").GetString());
        Assert.Equal("S-2", body.GetProperty("employeeStaffId").GetString());
        Assert.Equal("0123", body.GetProperty("phone").GetString());
        Assert.Equal("Reader", body.GetProperty("designationName").GetString());
        Assert.Equal(teacher.UserId, body.GetProperty("userId").GetString());
        Assert.Equal(AdminUserId, body.GetProperty("lastModifiedByUserId").GetString());
        Assert.Equal(1, await ProfileCountAsync());
    }

    [Fact]
    public async Task Clearing_the_optional_fields_removes_them()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, staffId: "S-9", phone: "999");

        var (_, body) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}", Edit("Same", teacher.Email, "  ", null));

        Assert.False(body.TryGetProperty("employeeStaffId", out _));
        Assert.False(body.TryGetProperty("phone", out _));
    }

    [Fact]
    public async Task Changing_the_email_changes_the_sign_in_name_and_it_must_stay_unique()
    {
        using var admin = await AdminAsync();
        var first = await CreateTeacherAsync(admin);
        var second = await CreateTeacherAsync(admin);
        var newEmail = PortalApiFactory.UniqueEmail("renamed");

        var (clash, clashProblem) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{first.Id}", Edit(email: second.Email.ToUpperInvariant()));
        var (renamed, body) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{first.Id}", Edit(email: newEmail));

        Assert.Equal(HttpStatusCode.BadRequest, clash);
        Assert.Equal("An account with this email already exists.", FirstError(clashProblem, "email"));
        Assert.Equal(HttpStatusCode.OK, renamed);
        Assert.Equal(newEmail, body.GetProperty("email").GetString());
        Assert.Equal(first.UserId, body.GetProperty("userId").GetString());
        await Factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == first.UserId);
            Assert.Equal(newEmail, user.UserName);
            Assert.Equal(newEmail.ToUpperInvariant(), user.NormalizedEmail);
        });
    }

    [Fact]
    public async Task Resubmitting_the_same_values_is_not_a_duplicate_of_itself()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Same", staffId: "S-5");

        var (status, _) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}", Edit("Same", teacher.Email.ToUpperInvariant(), "S-5"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(await EventsAsync("teacher.updated"));
    }

    [Fact]
    public async Task A_staff_id_used_by_another_teacher_is_rejected()
    {
        using var admin = await AdminAsync();
        await CreateTeacherAsync(admin, staffId: "TAKEN");
        var other = await CreateTeacherAsync(admin);

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{other.Id}", Edit(email: other.Email, staffId: " taken "));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "employeeStaffId"));
    }

    [Fact]
    public async Task Required_fields_still_apply_to_an_update()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Keep Me");

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}", Edit(" ", ""));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "displayName"));
        Assert.True(HasError(problem, "email"));
        var (_, body) = await GetTeacher(admin, teacher.Id);
        Assert.Equal("Keep Me", body.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task Updating_an_unknown_teacher_is_not_found()
    {
        using var admin = await AdminAsync();

        var (status, _) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{Guid.NewGuid()}", Edit(email: "x@iit.test"));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task An_update_is_audited_with_only_the_changed_fields_and_contact_values_are_not_recorded()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Before", phone: "111");
        var newEmail = PortalApiFactory.UniqueEmail("after");

        await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}", Edit("After", newEmail, null, "222"));

        var updated = Assert.Single(await EventsAsync("teacher.updated"));
        Assert.Equal(AdminUserId, updated.ActorUserId);
        Assert.Contains("Before", updated.ChangesJson);
        Assert.Contains("After", updated.ChangesJson);
        Assert.DoesNotContain(newEmail, updated.ChangesJson);
        Assert.DoesNotContain("222", updated.ChangesJson);
        Assert.Contains("email", updated.ChangesJson);
        Assert.Contains("phone", updated.ChangesJson);
    }

    [Fact]
    public async Task A_failed_audit_event_rolls_back_profile_and_identity_email_changes()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Original Name");
        var replacementEmail = PortalApiFactory.UniqueEmail("rollback");
        var updatesBefore = (await EventsAsync("teacher.updated")).Count;

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var provider = scope.ServiceProvider;
            var service = new TeacherProfileService(
                provider.GetRequiredService<UserManager<PortalUser>>(),
                provider.GetRequiredService<ITeacherStore>(),
                provider.GetRequiredService<TeacherLifecycleService>(),
                new TeacherProvisioningTests.ThrowingRecorder(),
                provider.GetRequiredService<IAuditActorDirectory>());

            await Assert.ThrowsAsync<AuditValidationException>(() => service.UpdateAsync(
                teacher.Id,
                new TeacherInput("Changed Name", replacementEmail, "NEW-1", "555", null),
                CancellationToken.None));
        }

        await Factory.WithDbAsync(async db =>
        {
            var profile = await db.TeacherProfiles.SingleAsync(p => p.Id == teacher.Id);
            var user = await db.Users.SingleAsync(u => u.Id == teacher.UserId);
            Assert.Equal("Original Name", profile.DisplayName);
            Assert.Equal(teacher.Email, user.Email);
            Assert.Equal(teacher.Email, user.UserName);
            Assert.Equal(teacher.Email.ToUpperInvariant(), user.NormalizedEmail);
            Assert.Null(profile.EmployeeStaffId);
            Assert.Null(profile.Phone);
        });
        Assert.Equal(0, await UserCountAsync(replacementEmail));
        Assert.Equal(updatesBefore, (await EventsAsync("teacher.updated")).Count);
    }

    [Fact]
    public async Task A_deactivated_teachers_profile_cannot_be_reactivated_here()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Deactivate(admin, teacher.Id);

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, $"{Teachers}/{teacher.Id}", Edit(email: teacher.Email, isActive: true));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "isActive"));
        await Factory.WithDbAsync(async db =>
        {
            Assert.False((await db.TeacherProfiles.SingleAsync()).IsActive);
            Assert.Equal(TeacherAccountStatus.Deactivated, (await db.Users.SingleAsync(u => u.Id == teacher.UserId)).AccountStatus);
        });
    }
}
