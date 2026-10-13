using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// Only a session whose active role is Admin may use configuration. Anonymous requests are 401 and any other active
/// role is 403, for every resource and operation, including both geography import endpoints.
/// </summary>
public sealed class ConfigurationAuthorizationTests : ConfigurationTestBase
{
    private static readonly string[] Resources =
    [
        "institutions", "education-boards", "education-groups", "examination-types", "degrees", "semesters",
        "academic-sessions", "designations", "batches", "geography/divisions", "geography/districts", "geography/upazilas",
    ];

    public static TheoryData<string, string, string> Calls()
    {
        var calls = new TheoryData<string, string, string>();
        var id = Guid.NewGuid();
        foreach (var resource in Resources)
        {
            calls.Add("GET", $"{Root}/{resource}", resource);
            calls.Add("GET", $"{Root}/{resource}/{id}", resource);
            calls.Add("POST", $"{Root}/{resource}", resource);
            calls.Add("PUT", $"{Root}/{resource}/{id}", resource);
            calls.Add("DELETE", $"{Root}/{resource}/{id}", resource);
        }

        calls.Add("POST", $"{Root}/geography/import/validate", "geography import validate");
        calls.Add("POST", $"{Root}/geography/import", "geography import");
        return calls;
    }

    private static object? BodyFor(string method) => method is "POST" or "PUT" ? new { name = "Should not be stored" } : null;

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task An_anonymous_request_is_401(string method, string url, string resource)
    {
        using var anonymous = Factory.CreatePortalClient();

        var (status, _) = await SendAsync(anonymous, new HttpMethod(method), url, BodyFor(method));

        Assert.True(status == HttpStatusCode.Unauthorized, $"{method} {resource}: {status}");
    }

    [Theory]
    [MemberData(nameof(Calls))]
    public async Task An_active_role_other_than_admin_is_403(string method, string url, string resource)
    {
        foreach (var role in new[] { PortalRoles.Student, PortalRoles.Teacher, PortalRoles.Coordinator })
        {
            using var client = await SignedInAsync(role);

            var (status, _) = await SendAsync(client, new HttpMethod(method), url, BodyFor(method));

            Assert.True(status == HttpStatusCode.Forbidden, $"{role} {method} {resource}: {status}");
        }
    }

    [Fact]
    public async Task Denied_writes_change_nothing()
    {
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        using var anonymous = Factory.CreatePortalClient();

        foreach (var client in new[] { teacher, anonymous })
        {
            foreach (var resource in Resources)
            {
                await SendAsync(client, HttpMethod.Post, $"{Root}/{resource}", new { name = "Nope", type = "School" });
            }
        }

        await Factory.WithDbAsync(async db =>
        {
            Assert.Equal(0, await db.EducationalInstitutions.CountAsync() + await db.EducationBoards.CountAsync()
                + await db.EducationGroups.CountAsync() + await db.ExaminationTypes.CountAsync() + await db.Degrees.CountAsync()
                + await db.Semesters.CountAsync() + await db.AcademicSessions.CountAsync() + await db.Designations.CountAsync()
                + await db.Batches.CountAsync() + await db.Divisions.CountAsync() + await db.Districts.CountAsync() + await db.Upazilas.CountAsync());
        });
        Assert.DoesNotContain(await Factory.AuditEventsAsync(), e => e.EventType.StartsWith("configuration.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_multi_role_account_is_denied_after_switching_away_from_admin_and_allowed_after_switching_back()
    {
        var email = PortalApiFactory.UniqueEmail("both");
        await Factory.CreateUserAsync(email, PortalRoles.Admin, PortalRoles.Teacher);
        using var client = Factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);

        Assert.Equal(HttpStatusCode.Created, (await Post(client, "degrees", new { name = "As admin" })).Status);

        Assert.Equal(HttpStatusCode.OK, (await client.SetActiveRoleAsync(PortalRoles.Teacher)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Get(client, "degrees")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Post(client, "degrees", new { name = "As teacher" })).Status);

        Assert.Equal(HttpStatusCode.OK, (await client.SetActiveRoleAsync(PortalRoles.Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Get(client, "degrees")).Status);
        Assert.Equal(["As admin"], Names((await Get(client, "degrees")).Body));
    }

    [Fact]
    public async Task A_client_supplied_role_value_cannot_grant_access()
    {
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        var headers = new Dictionary<string, string> { ["X-Role"] = "Admin", ["X-Active-Role"] = "Admin" };

        using var response = await teacher.SendAsync(HttpMethod.Get, $"{Root}/degrees", headers: headers);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_write_without_the_anti_forgery_token_is_rejected_even_for_an_admin()
    {
        using var admin = await AdminAsync();

        using var response = await admin.SendAsync(HttpMethod.Post, $"{Root}/degrees", new { name = "No token" }, withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty((await Get(admin, "degrees", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }
}
