using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// Authenticated 403 refusals are audit events. Anonymous 401 responses, including the portal's own signed-in
/// check on every page load, are counted but never recorded.
/// </summary>
public sealed class AccessDeniedEventTests : IDisposable
{
    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<(PortalClient Client, string UserId)> SignedInAsync(string role)
    {
        var email = PortalApiFactory.UniqueEmail(role.ToLowerInvariant());
        var userId = await factory.CreateUserAsync(email, role);
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return (client, userId);
    }

    private static string? Route(AuditEvent e) =>
        JsonDocument.Parse(e.MetadataJson!).RootElement.TryGetProperty("route", out var route) ? route.GetString() : null;

    [Fact]
    public async Task A_policy_refusal_records_the_actor_the_method_and_the_matched_route_template()
    {
        var (client, userId) = await SignedInAsync(PortalRoles.Student);
        using var _ = client;

        var response = await client.GetAsync("/test-probe/modules/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await PortalClient.ReadJsonAsync(response);
        Assert.Equal("Access denied", problem.GetProperty("title").GetString());

        var denied = Assert.Single(await factory.AuditEventsAsync("access.denied"));
        Assert.Equal(AuditOutcome.Denied, denied.Outcome);
        Assert.Equal(AuditEventCategory.Security, denied.Category);
        Assert.Equal(userId, denied.ActorUserId);
        Assert.Equal("GET", JsonDocument.Parse(denied.MetadataJson!).RootElement.GetProperty("method").GetString());
        Assert.Equal("test-probe/modules/admin", Route(denied));
    }

    [Fact]
    public async Task A_record_level_refusal_keeps_the_route_template_not_the_identifier_in_the_path()
    {
        var (client, _) = await SignedInAsync(PortalRoles.Student);
        using var _1 = client;
        var otherStudent = "someone-elses-id-12345";

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/test-probe/student-records/{otherStudent}")).StatusCode);

        var denied = Assert.Single(await factory.AuditEventsAsync("access.denied"));
        Assert.Equal("test-probe/student-records/{studentUserId}", Route(denied));
        Assert.DoesNotContain(otherStudent, denied.MetadataJson);
    }

    [Fact]
    public async Task The_query_string_is_never_recorded()
    {
        var (client, _) = await SignedInAsync(PortalRoles.Teacher);
        using var _1 = client;

        await client.GetAsync("/test-probe/modules/admin?secret=abc123&token=xyz");

        var denied = Assert.Single(await factory.AuditEventsAsync("access.denied"));
        Assert.DoesNotContain("abc123", denied.MetadataJson);
        Assert.DoesNotContain("xyz", denied.MetadataJson);
        Assert.DoesNotContain("?", denied.MetadataJson);
    }

    [Fact]
    public async Task Anonymous_401_responses_record_nothing_but_are_counted()
    {
        using var anonymous = factory.CreatePortalClient();

        // This is exactly what the portal does for every signed-out visitor on every page load.
        for (var i = 0; i < 3; i++)
        {
            var response = await anonymous.GetCurrentSessionAsync();
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Sign-in required", (await PortalClient.ReadJsonAsync(response)).GetProperty("title").GetString());
        }

        Assert.Empty(await factory.AuditEventsAsync());
        Assert.Empty(await factory.OutboxItemsAsync());
        Assert.Equal(3, factory.Metrics.Total("audit.anonymous_unauthorized"));
    }

    [Fact]
    public async Task A_stale_or_unknown_session_cookie_is_also_just_counted()
    {
        using var client = factory.CreatePortalClient();
        client.UseSessionCookie("a-cookie-for-a-session-that-does-not-exist");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/test-probe/modules/admin")).StatusCode);

        Assert.Empty(await factory.AuditEventsAsync());
        Assert.Equal(1, factory.Metrics.Total("audit.anonymous_unauthorized"));
    }

    [Fact]
    public async Task Allowed_requests_and_the_original_status_codes_are_untouched()
    {
        var (client, _) = await SignedInAsync(PortalRoles.Admin);
        using var _1 = client;

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test-probe/modules/admin")).StatusCode);

        Assert.Empty(await factory.AuditEventsAsync("access.denied"));
    }
}
