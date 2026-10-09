using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Only a session whose active role is Admin may review audit history, checked on every request.</summary>
public sealed class AuditReviewAuthorizationTests : IDisposable
{
    private const string Marker = "protected-audit-marker-4821";

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<Guid> SeedEventAsync()
    {
        var seeded = new AuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, AuditEventCategory.Security, "test.review", AuditOutcome.Success,
            null, "Test", Marker, null, null, null, null);
        await factory.WithDbAsync(async db =>
        {
            db.AuditEvents.Add(seeded);
            await db.SaveChangesAsync();
        });
        return seeded.Id;
    }

    private async Task<PortalClient> SignedInAsync(params string[] roles)
    {
        var email = PortalApiFactory.UniqueEmail("reviewer");
        await factory.CreateUserAsync(email, roles);
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    [Fact]
    public async Task An_admin_can_search_and_open_an_event()
    {
        var id = await SeedEventAsync();
        using var client = await SignedInAsync(PortalRoles.Admin);

        var search = await client.GetAsync("/api/audit-events?eventType=test.review");
        var detail = await client.GetAsync($"/api/audit-events/{id}");

        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        Assert.Contains(Marker, await search.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(id.ToString(), (await PortalClient.ReadJsonAsync(detail)).GetProperty("eventId").GetString());
    }

    [Theory]
    [InlineData(PortalRoles.Student)]
    [InlineData(PortalRoles.Teacher)]
    [InlineData(PortalRoles.Coordinator)]
    public async Task Other_roles_are_denied_without_any_audit_content(string role)
    {
        var id = await SeedEventAsync();
        using var client = await SignedInAsync(role);

        var search = await client.GetAsync("/api/audit-events");
        var detail = await client.GetAsync($"/api/audit-events/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, search.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
        Assert.DoesNotContain(Marker, await search.Content.ReadAsStringAsync());
        Assert.DoesNotContain(Marker, await detail.Content.ReadAsStringAsync());
        Assert.DoesNotContain(id.ToString(), await detail.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task An_unauthenticated_request_gets_401_without_content()
    {
        var id = await SeedEventAsync();
        using var client = factory.CreatePortalClient();

        var search = await client.GetAsync("/api/audit-events");
        var detail = await client.GetAsync($"/api/audit-events/{id}");

        Assert.Equal(HttpStatusCode.Unauthorized, search.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, detail.StatusCode);
        Assert.DoesNotContain(Marker, await search.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Access_follows_the_active_role_and_is_rechecked_after_each_switch()
    {
        await SeedEventAsync();
        using var client = await SignedInAsync(PortalRoles.Admin, PortalRoles.Teacher);

        // An Admin assignment alone is not enough: the session's active role decides.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/audit-events")).StatusCode);

        await client.SetActiveRoleAsync(PortalRoles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit-events")).StatusCode);

        await client.SetActiveRoleAsync(PortalRoles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/audit-events")).StatusCode);
    }

    [Fact]
    public async Task A_teacher_and_coordinator_account_gets_no_audit_access_in_either_role()
    {
        using var client = await SignedInAsync(PortalRoles.Teacher, PortalRoles.Coordinator);

        await client.SetActiveRoleAsync(PortalRoles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit-events")).StatusCode);

        await client.SetActiveRoleAsync(PortalRoles.Coordinator);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit-events")).StatusCode);
    }

    [Fact]
    public async Task A_client_cannot_claim_the_admin_role_through_headers_or_query_values()
    {
        using var client = await SignedInAsync(PortalRoles.Student);

        var response = await client.SendAsync(
            HttpMethod.Get, "/api/audit-events?role=Admin&activeRole=Admin", headers: new Dictionary<string, string> { ["X-Role"] = "Admin", ["X-Active-Role"] = "Admin" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_refused_review_request_is_itself_recorded_as_an_access_denied_event()
    {
        using var client = await SignedInAsync(PortalRoles.Coordinator);

        await client.GetAsync("/api/audit-events");

        var denied = Assert.Single(await factory.AuditEventsAsync("access.denied"));
        Assert.Equal("api/audit-events", System.Text.Json.JsonDocument.Parse(denied.MetadataJson!).RootElement.GetProperty("route").GetString());
    }
}
