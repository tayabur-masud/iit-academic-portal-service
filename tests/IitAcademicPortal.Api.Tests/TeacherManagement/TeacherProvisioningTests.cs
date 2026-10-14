using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>US1: an Admin adds a teacher. One linked account and profile, Teacher role and default, pending and unable to sign in.</summary>
public sealed class TeacherProvisioningTests : TeacherTestBase
{
    [Fact]
    public async Task A_valid_teacher_creates_one_pending_account_with_the_Teacher_role_and_default()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("teacher");

        using var response = await admin.SendAsync(HttpMethod.Post, Teachers, new
        {
            displayName = "  Dr. Ada   Lovelace ",
            email = $"  {email} ",
            employeeStaffId = " st-100 ",
            phone = "+880 1700 000000",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await PortalClient.ReadJsonAsync(response);
        var id = body.GetProperty("id").GetGuid();
        Assert.Equal($"/api/admin/teachers/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal("Dr. Ada   Lovelace", body.GetProperty("displayName").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
        Assert.Equal("st-100", body.GetProperty("employeeStaffId").GetString());
        Assert.Equal("PendingCredentialSetup", body.GetProperty("accountStatus").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal([PortalRoles.Teacher], Roles(body));
        Assert.Equal(PortalRoles.Teacher, body.GetProperty("defaultRole").GetString());
        Assert.Equal(AdminUserId, body.GetProperty("createdByUserId").GetString());
        Assert.Equal(AdminEmail, body.GetProperty("createdBy").GetString());

        Assert.Equal(1, await UserCountAsync(email));
        Assert.Equal(1, await ProfileCountAsync());
        Assert.Equal([PortalRoles.Teacher], await AssignedRolesAsync(body.GetProperty("userId").GetString()!));
    }

    [Fact]
    public async Task The_account_has_no_password_and_the_response_exposes_no_secrets()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("teacher");

        using var response = await admin.SendAsync(HttpMethod.Post, Teachers, new { displayName = "Teacher One", email });
        var text = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
        await Factory.WithDbAsync(async db => Assert.Null((await db.Users.SingleAsync(u => u.Email == email)).PasswordHash));
        Assert.Empty(Factory.Emails.Messages);
    }

    [Fact]
    public async Task A_pending_teacher_cannot_sign_in_even_with_a_password_set_by_another_path()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == teacher.UserId);
            user.PasswordHash = new PasswordHasher<PortalUser>().HashPassword(user, PortalApiFactory.Password);
            await db.SaveChangesAsync();
        });
        using var client = Factory.CreatePortalClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SignInAsync(teacher.Email)).StatusCode);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("DUPLICATE-UPPER")]
    public async Task A_duplicate_email_is_rejected_without_a_second_account_or_profile(string variant)
    {
        using var admin = await AdminAsync();
        var first = await CreateTeacherAsync(admin);
        var email = variant == "duplicate" ? first.Email : first.Email.ToUpperInvariant();

        var (status, problem) = await SendAsync(admin, HttpMethod.Post, Teachers, new { displayName = "Second", email });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "email"));
        Assert.Equal(1, await ProfileCountAsync());
        Assert.Equal(1, await UserCountAsync(first.Email));
    }

    [Fact]
    public async Task An_email_that_belongs_to_an_account_without_a_teacher_profile_is_rejected()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        await Factory.CreateUserAsync(email, PortalRoles.Student);

        var (status, problem) = await SendAsync(admin, HttpMethod.Post, Teachers, new { displayName = "Not a student", email });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("An account with this email already exists.", FirstError(problem, "email"));
        Assert.Equal(0, await ProfileCountAsync());
        Assert.Equal([PortalRoles.Student], await AssignedRolesAsync(await UserIdAsync(email)));
    }

    [Fact]
    public async Task Name_and_email_are_required_and_the_email_must_be_valid()
    {
        using var admin = await AdminAsync();

        var (missing, problem) = await SendAsync(admin, HttpMethod.Post, Teachers, new { displayName = "  ", email = "" });
        var (malformed, malformedProblem) = await SendAsync(admin, HttpMethod.Post, Teachers, new { displayName = "Name", email = "not-an-email" });

        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.True(HasError(problem, "displayName"));
        Assert.True(HasError(problem, "email"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed);
        Assert.Equal("Enter a valid email address.", FirstError(malformedProblem, "email"));
        Assert.Equal(0, await ProfileCountAsync());
    }

    [Fact]
    public async Task Over_long_values_and_unknown_fields_are_rejected()
    {
        using var admin = await AdminAsync();

        var (longName, longNameProblem) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = new string('a', 201), email = PortalApiFactory.UniqueEmail("t"), employeeStaffId = new string('b', 51), phone = new string('1', 51) });
        var (unknown, _) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = "Name", email = PortalApiFactory.UniqueEmail("t"), roles = new[] { "Admin" } });
        var (activeFlag, _) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = "Name", email = PortalApiFactory.UniqueEmail("t"), isActive = false });

        Assert.Equal(HttpStatusCode.BadRequest, longName);
        Assert.True(HasError(longNameProblem, "displayName"));
        Assert.True(HasError(longNameProblem, "employeeStaffId"));
        Assert.True(HasError(longNameProblem, "phone"));
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        Assert.Equal(HttpStatusCode.BadRequest, activeFlag);
        Assert.Equal(0, await ProfileCountAsync());
    }

    [Fact]
    public async Task A_supplied_staff_id_must_be_unique_ignoring_case_and_spacing_but_blank_ones_may_repeat()
    {
        using var admin = await AdminAsync();
        await CreateTeacherAsync(admin, staffId: "EMP  001");

        var (status, problem) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = "Other", email = PortalApiFactory.UniqueEmail("t"), employeeStaffId = " emp 001 " });
        await CreateTeacherAsync(admin, staffId: null);
        await CreateTeacherAsync(admin, staffId: "   ");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Another teacher already has this staff ID.", FirstError(problem, "employeeStaffId"));
        Assert.Equal(3, await ProfileCountAsync());
    }

    [Fact]
    public async Task A_designation_must_exist_and_be_active_and_its_name_is_returned()
    {
        using var admin = await AdminAsync();
        var active = await CreateAsync(admin, "designations", new { name = "Lecturer" });
        var inactive = await CreateAsync(admin, "designations", new { name = "Retired post" });
        await Put(admin, "designations", inactive, new { name = "Retired post", isActive = false });

        var (unknown, unknownProblem) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = "A", email = PortalApiFactory.UniqueEmail("t"), designationId = Guid.NewGuid() });
        var (retired, _) = await SendAsync(admin, HttpMethod.Post, Teachers,
            new { displayName = "B", email = PortalApiFactory.UniqueEmail("t"), designationId = inactive });
        var teacher = await CreateTeacherAsync(admin, designationId: active);
        var (_, body) = await GetTeacher(admin, teacher.Id);

        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        Assert.True(HasError(unknownProblem, "designationId"));
        Assert.Equal(HttpStatusCode.BadRequest, retired);
        Assert.Equal("Lecturer", body.GetProperty("designationName").GetString());
    }

    [Fact]
    public async Task A_designation_in_use_by_a_teacher_cannot_be_deleted()
    {
        using var admin = await AdminAsync();
        var designation = await CreateAsync(admin, "designations", new { name = "Professor" });
        await CreateTeacherAsync(admin, designationId: designation);

        var (status, problem) = await Delete(admin, "designations", designation);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("deactivate", problem.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Creating_a_teacher_records_one_audit_event_without_secrets_or_contact_values()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Audit Me", phone: "555-0100", staffId: "A-1");

        var events = await EventsAsync("teacher.created");

        var created = Assert.Single(events);
        Assert.Equal(AdminUserId, created.ActorUserId);
        Assert.Equal("Teacher", created.EntityType);
        Assert.Equal(teacher.Id.ToString(), created.EntityId);
        Assert.Equal(AuditOutcome.Success, created.Outcome);
        Assert.Contains(teacher.UserId, created.MetadataJson);
        Assert.Contains("Audit Me", created.ChangesJson);
        Assert.Contains("PendingCredentialSetup", created.ChangesJson);
        Assert.DoesNotContain(teacher.Email, created.ChangesJson);
        Assert.DoesNotContain("555-0100", created.ChangesJson);
        Assert.DoesNotContain("password", created.ChangesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failed_audit_event_leaves_no_account_or_profile()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("teacher");
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var service = new TeacherProvisioningService(
                scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>(),
                scope.ServiceProvider.GetRequiredService<ITeacherStore>(),
                new ThrowingRecorder(),
                scope.ServiceProvider.GetRequiredService<IAuditActorDirectory>());

            await Assert.ThrowsAsync<AuditValidationException>(() =>
                service.CreateAsync(new TeacherInput("Will Not Exist", email, "X-1", null, null), CancellationToken.None));
        }

        Assert.Equal(0, await UserCountAsync(email));
        Assert.Equal(0, await ProfileCountAsync());
    }

    [Fact]
    public async Task A_teacher_can_be_read_by_id_and_an_unknown_id_is_not_found()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin, name: "Readable");

        var (found, body) = await GetTeacher(admin, teacher.Id);
        var (missing, _) = await GetTeacher(admin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.OK, found);
        Assert.Equal("Readable", body.GetProperty("displayName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    private async Task<string> UserIdAsync(string email)
    {
        var id = string.Empty;
        await Factory.WithDbAsync(async db => id = (await db.Users.SingleAsync(u => u.Email == email)).Id);
        return id;
    }

    /// <summary>An audit recorder whose staging always fails, to prove the business change rolls back with it.</summary>
    internal sealed class ThrowingRecorder : IAuditEventRecorder
    {
        public void Stage(AuditEventRequest request) => throw new AuditValidationException("Forced audit failure.");

        public Task RecordSecurityAsync(AuditEventRequest request) => Task.CompletedTask;
    }
}
