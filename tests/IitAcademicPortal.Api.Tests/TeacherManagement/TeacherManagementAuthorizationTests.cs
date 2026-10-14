using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

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

    private sealed record TeacherState(
        int ProfileCount,
        string Email,
        string UserName,
        TeacherAccountStatus AccountStatus,
        string? DefaultRole,
        bool IsActive,
        string DisplayName,
        string[] Roles,
        string[] Sessions,
        Guid[] BusinessAuditEventIds,
        Guid[] AccessDeniedEventIds);

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

    private async Task<TeacherState> CaptureStateAsync(Teacher teacher)
    {
        var profileCount = 0;
        var email = string.Empty;
        var userName = string.Empty;
        var accountStatus = TeacherAccountStatus.Active;
        string? defaultRole = null;
        var isActive = false;
        var displayName = string.Empty;
        var roles = Array.Empty<string>();
        var sessions = Array.Empty<string>();

        await Factory.WithDbAsync(async db =>
        {
            var profile = await db.TeacherProfiles.SingleAsync(p => p.Id == teacher.Id);
            var user = await db.Users.SingleAsync(u => u.Id == teacher.UserId);
            profileCount = await db.TeacherProfiles.CountAsync();
            email = user.Email!;
            userName = user.UserName!;
            accountStatus = user.AccountStatus;
            defaultRole = user.DefaultRole;
            isActive = profile.IsActive;
            displayName = profile.DisplayName;
            roles = await db.UserRoles
                .Where(ur => ur.UserId == teacher.UserId)
                .Join(db.Roles, ur => ur.RoleId, role => role.Id, (_, role) => role.Name!)
                .OrderBy(role => role)
                .ToArrayAsync();
            sessions = await db.AuthSessions
                .Where(session => session.UserId == teacher.UserId)
                .OrderBy(session => session.Id)
                .Select(session => $"{session.Id}|{session.LastActivityAt.UtcTicks}|{session.RevokedAt}|{session.RevocationReason}")
                .ToArrayAsync();
        });

        var events = await Factory.AuditEventsAsync();
        return new TeacherState(
            profileCount, email, userName, accountStatus, defaultRole, isActive, displayName, roles, sessions,
            events.Where(e => e.Category == AuditEventCategory.Business).Select(e => e.Id).Order().ToArray(),
            events.Where(e => e.EventType == "access.denied").Select(e => e.Id).Order().ToArray());
    }

    private static void AssertNoBusinessSideEffects(TeacherState before, TeacherState after, int expectedDeniedEvents)
    {
        Assert.Equal(before.ProfileCount, after.ProfileCount);
        Assert.Equal(before.Email, after.Email);
        Assert.Equal(before.UserName, after.UserName);
        Assert.Equal(before.AccountStatus, after.AccountStatus);
        Assert.Equal(before.DefaultRole, after.DefaultRole);
        Assert.Equal(before.IsActive, after.IsActive);
        Assert.Equal(before.DisplayName, after.DisplayName);
        Assert.Equal(before.Roles, after.Roles);
        Assert.Equal(before.Sessions, after.Sessions);
        Assert.Equal(before.BusinessAuditEventIds, after.BusinessAuditEventIds);
        Assert.Equal(before.AccessDeniedEventIds.Length + expectedDeniedEvents, after.AccessDeniedEventIds.Length);
    }

    [Fact]
    public async Task Anonymous_callers_get_401_on_every_route()
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        using var anonymous = Factory.CreatePortalClient();
        var before = await CaptureStateAsync(teacher);

        await AssertDeniedAsync(anonymous, teacher, HttpStatusCode.Unauthorized);

        AssertNoBusinessSideEffects(before, await CaptureStateAsync(teacher), expectedDeniedEvents: 0);
    }

    [Theory]
    [InlineData(PortalRoles.Student)]
    [InlineData(PortalRoles.Teacher)]
    [InlineData(PortalRoles.Coordinator)]
    public async Task A_session_in_a_non_Admin_role_gets_403_on_every_route(string role)
    {
        using var admin = await AdminAsync();
        var teacher = await CreateTeacherAsync(admin);
        await Factory.ActivateWithPasswordAsync(teacher.Email);
        using var teacherSession = Factory.CreatePortalClient();
        await teacherSession.SignInSuccessfullyAsync(teacher.Email);
        using var user = await SignedInAsync(role);
        var before = await CaptureStateAsync(teacher);

        await AssertDeniedAsync(user, teacher, HttpStatusCode.Forbidden);

        // 403 responses are security-audited; the denied operations must not write business state or business audit history.
        AssertNoBusinessSideEffects(before, await CaptureStateAsync(teacher), expectedDeniedEvents: Routes.Length);
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
