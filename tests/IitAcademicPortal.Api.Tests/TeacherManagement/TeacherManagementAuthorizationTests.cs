using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>
/// Every teacher route denies anonymous callers (401) and every session whose active role is not Admin (403), whatever roles the
/// account holds, and leaks no teacher or account data in the response.
/// </summary>
public sealed class TeacherManagementAuthorizationTests : TeacherTestBase
{
    private static readonly (HttpMethod Method, string Route, bool HasBody)[] Routes =
    [
        (HttpMethod.Get, "", false),
        (HttpMethod.Post, "", true),
        (HttpMethod.Get, "/{id}", false),
        (HttpMethod.Put, "/{id}", true),
        (HttpMethod.Put, "/{id}/roles", true),
        (HttpMethod.Post, "/{id}/deactivation", false),
    ];

    private static object Body(Teacher teacher) =>
        new { displayName = "Intruder", email = PortalApiFactory.UniqueEmail("x"), roles = new[] { "Teacher", "Admin" }, isActive = false, teacher = teacher.Email };

    private static async Task AssertDeniedAsync(PortalClient client, Teacher teacher, HttpStatusCode expected)
    {
        foreach (var (method, route, hasBody) in Routes)
        {
            using var response = await client.SendAsync(method, $"{Teachers}{route.Replace("{id}", teacher.Id.ToString())}", hasBody ? Body(teacher) : null);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.StatusCode == expected, $"{method} {route} returned {response.StatusCode}, expected {expected}");
            Assert.DoesNotContain(teacher.Email, text);
            Assert.DoesNotContain("Test Teacher", text);
        }
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_route()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        using var anonymous = Factory.CreatePortalClient();

        await AssertDeniedAsync(anonymous, teacher, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(PortalRoles.Student)]
    [InlineData(PortalRoles.Teacher)]
    [InlineData(PortalRoles.Coordinator)]
    public async Task A_session_in_a_non_Admin_role_gets_403_on_every_route(string role)
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        using var user = await SignedInAsync(role);

        await AssertDeniedAsync(user, teacher, HttpStatusCode.Forbidden);

        // Nothing was changed by the denied attempts.
        var (_, body) = await GetTeacher(admin, teacher.Id);
        Assert.Equal("Test Teacher", body.GetProperty("displayName").GetString());
        Assert.Equal([PortalRoles.Teacher], Roles(body));
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await ProfileCountAsync());
    }

    [Fact]
    public async Task An_Admin_account_acting_in_another_role_is_denied_until_it_switches_to_Admin()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        var email = PortalApiFactory.UniqueEmail("both");
        await Factory.CreateUserAsync(email, PortalRoles.Admin, PortalRoles.Teacher);
        await Factory.SetDefaultRoleAsync(email, PortalRoles.Teacher);
        using var both = Factory.CreatePortalClient();
        await both.SignInSuccessfullyAsync(email);

        await AssertDeniedAsync(both, teacher, HttpStatusCode.Forbidden);
        await both.SetActiveRoleAsync(PortalRoles.Admin);

        Assert.Equal(HttpStatusCode.OK, (await both.GetAsync($"{Teachers}/{teacher.Id}")).StatusCode);
    }

    [Fact]
    public async Task A_state_changing_request_without_the_anti_forgery_token_is_rejected()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);

        using var create = await admin.SendAsync(HttpMethod.Post, Teachers, new { displayName = "No token", email = PortalApiFactory.UniqueEmail("t") }, withAntiforgery: false);
        using var roles = await admin.SendAsync(HttpMethod.Put, $"{Teachers}/{teacher.Id}/roles", new { roles = new[] { "Teacher", "Admin" } }, withAntiforgery: false);
        using var deactivate = await admin.SendAsync(HttpMethod.Post, $"{Teachers}/{teacher.Id}/deactivation", withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, roles.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deactivate.StatusCode);
        Assert.Equal(1, await ProfileCountAsync());
        var (_, body) = await GetTeacher(admin, teacher.Id);
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.Equal([PortalRoles.Teacher], Roles(body));
    }
}
