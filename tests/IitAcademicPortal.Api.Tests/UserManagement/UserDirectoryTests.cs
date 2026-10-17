using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>US1: an active Admin finds and inspects accounts of every role; nobody else sees any account data.</summary>
public sealed class UserDirectoryTests : UserManagementTestBase
{
    private static List<JsonElement> Items(JsonElement page) => page.GetProperty("items").EnumerateArray().ToList();

    private static List<string> Emails(JsonElement page) => Items(page).Select(i => i.GetProperty("email").GetString()!).ToList();

    [Fact]
    public async Task Anonymous_callers_get_401_and_other_active_roles_get_403_without_account_data()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);
        using var anonymous = Host.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        using var coordinator = await SignedInAsync(PortalRoles.Teacher, PortalRoles.Coordinator);

        // An Admin whose active role is not Admin is refused too: authorization follows the active role, not the assignment.
        var dualEmail = PortalApiFactory.UniqueEmail("dual");
        await Host.CreateUserAsync(dualEmail, PortalRoles.Admin, PortalRoles.Teacher);
        await Host.SetDefaultRoleAsync(dualEmail, PortalRoles.Teacher);
        using var dual = Host.CreatePortalClient();
        await dual.SignInSuccessfullyAsync(dualEmail);

        foreach (var route in new[] { Users, UserRoute(account.UserId), $"{Users}?search={account.Email}" })
        {
            using var anonymousResponse = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            Assert.DoesNotContain(account.Email, await anonymousResponse.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            foreach (var client in new[] { student, teacher, coordinator, dual })
            {
                using var response = await client.GetAsync(route);
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                Assert.DoesNotContain(account.Email, await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task The_directory_lists_accounts_of_every_role_including_teachers_and_coordinators()
    {
        using var admin = await AdminAsync();
        var student = await CreateAccountAsync(admin, PortalRoles.Student);
        var teacher = await CreateTeacherAsync(admin);
        var coordinatorEmail = PortalApiFactory.UniqueEmail("coordinator");
        await Host.CreateUserAsync(coordinatorEmail, PortalRoles.Teacher, PortalRoles.Coordinator);

        var (status, page) = await SendAsync(admin, HttpMethod.Get, $"{Users}?pageSize=100");

        Assert.Equal(HttpStatusCode.OK, status);
        var emails = Emails(page);
        Assert.Contains(student.Email, emails);
        Assert.Contains(teacher.Email, emails);
        Assert.Contains(coordinatorEmail, emails);
        var coordinator = Items(page).Single(i => i.GetProperty("email").GetString() == coordinatorEmail);
        Assert.Equal([PortalRoles.Teacher, PortalRoles.Coordinator], Roles(coordinator));
        var teacherItem = Items(page).Single(i => i.GetProperty("email").GetString() == teacher.Email);
        Assert.Equal(JsonValueKind.String, teacherItem.GetProperty("teacherId").ValueKind);
    }

    [Fact]
    public async Task Search_matches_part_of_the_email_case_insensitively_or_the_exact_user_id()
    {
        using var admin = await AdminAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var first = await CreateAccountAsync(admin, PortalRoles.Student, $"alpha.{marker}@iit.test");
        var second = await CreateAccountAsync(admin, PortalRoles.Admin, $"beta.{marker}@iit.test");
        await CreateAccountAsync(admin, PortalRoles.Student);

        var (_, byEmail) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker.ToUpperInvariant()}");
        var (_, byId) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={Uri.EscapeDataString(second.UserId)}");
        var (_, byPartialId) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={Uri.EscapeDataString(second.UserId[..8])}");

        Assert.Equal([first.Email, second.Email], Emails(byEmail));
        Assert.Equal([second.Email], Emails(byId));
        // An ID matches only exactly; part of one is just an email search.
        Assert.DoesNotContain(second.Email, Emails(byPartialId));
    }

    [Fact]
    public async Task Role_and_status_filters_narrow_the_results()
    {
        using var admin = await AdminAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var pendingStudent = await CreateAccountAsync(admin, PortalRoles.Student, $"s1.{marker}@iit.test");
        var activeStudentEmail = $"s2.{marker}@iit.test";
        await Host.CreateUserAsync(activeStudentEmail, PortalRoles.Student);
        var pendingAdmin = await CreateAccountAsync(admin, PortalRoles.Admin, $"a1.{marker}@iit.test");

        var (_, students) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}&role=student");
        var (_, pending) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}&status=PendingCredentialSetup");
        var (_, pendingStudents) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}&role=Student&status=pendingcredentialsetup");

        Assert.Equal([pendingStudent.Email, activeStudentEmail], Emails(students));
        Assert.Equal([pendingAdmin.Email, pendingStudent.Email], Emails(pending));
        Assert.Equal([pendingStudent.Email], Emails(pendingStudents));
    }

    [Theory]
    [InlineData("role=Guest", "role")]
    [InlineData("status=Suspended", "status")]
    [InlineData("status=1", "status")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task Invalid_filters_and_page_sizes_are_field_keyed_400s(string query, string field)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Get, $"{Users}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field), problem.ToString());
    }

    [Fact]
    public async Task Pages_are_bounded_default_to_25_and_are_ordered_by_normalized_email_then_id()
    {
        using var admin = await AdminAsync();
        var marker = Guid.NewGuid().ToString("N")[..10];
        var created = new List<string>();
        for (var i = 0; i < 27; i++)
        {
            // Mixed case so ordering must use the normalized email.
            var email = $"{(i % 2 == 0 ? "U" : "u")}{i:D2}.{marker}@iit.test";
            await Host.CreateUserAsync(email, PortalRoles.Student);
            created.Add(email);
        }

        var (_, first) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}");
        var (_, second) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}&page=2");
        var (_, max) = await SendAsync(admin, HttpMethod.Get, $"{Users}?search={marker}&pageSize=100");

        Assert.Equal(25, Items(first).Count);
        Assert.Equal(2, Items(second).Count);
        Assert.Equal(27, first.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, first.GetProperty("totalPages").GetInt32());
        var expected = created.OrderBy(e => e.ToUpperInvariant(), StringComparer.Ordinal).ToList();
        Assert.Equal(expected, Emails(first).Concat(Emails(second)).ToList());
        Assert.Equal(expected, Emails(max));
    }

    [Fact]
    public async Task The_detail_shows_identity_status_roles_default_and_audit_facts_without_any_secret()
    {
        using var admin = await AdminAsync();
        var account = await CreateAccountAsync(admin, PortalRoles.Student);

        using var response = await admin.GetAsync(UserRoute(account.UserId));
        var text = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(text).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(account.UserId, body.GetProperty("userId").GetString());
        Assert.Equal(account.Email, body.GetProperty("email").GetString());
        Assert.Equal("PendingCredentialSetup", body.GetProperty("accountStatus").GetString());
        Assert.Equal([PortalRoles.Student], Roles(body));
        Assert.Equal(PortalRoles.Student, body.GetProperty("defaultRole").GetString());
        Assert.Equal(AdminUserId, body.GetProperty("createdByUserId").GetString());
        Assert.True(body.TryGetProperty("createdAtUtc", out _));
        Assert.False(body.TryGetProperty("teacherId", out _));
        var user = await UserAsync(account.UserId);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("proof", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(user.SecurityStamp!, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_teacher_account_links_to_teacher_management_and_an_unknown_id_is_404()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        var (status, body) = await SendAsync(admin, HttpMethod.Get, UserRoute(teacher.UserId));
        var (missing, _) = await SendAsync(admin, HttpMethod.Get, UserRoute("no-such-account"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([PortalRoles.Teacher], Roles(body));
        var teacherId = body.GetProperty("teacherId").GetGuid();
        var (teacherStatus, _) = await SendAsync(admin, HttpMethod.Get, $"/api/admin/teachers/{teacherId}");
        Assert.Equal(HttpStatusCode.OK, teacherStatus);
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }
}
