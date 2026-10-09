using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Controllers;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// Audit history is read-only for every role. An attempt to change it gets 405 with Allow: GET and is recorded;
/// anonymous callers get 401 and nothing is recorded; no stored event ever changes.
/// </summary>
public sealed class AuditMutationAttemptTests : IDisposable
{
    private static readonly string[] MutatingMethods = ["POST", "PUT", "PATCH", "DELETE"];

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

    private async Task<AuditEvent> SeedEventAsync()
    {
        var seeded = new AuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, AuditEventCategory.Security, "test.readonly", AuditOutcome.Success,
            "actor", "Test", "unchanged-marker", null, null, "{\"k\":\"v\"}", null);
        await factory.WithDbAsync(async db =>
        {
            db.AuditEvents.Add(seeded);
            await db.SaveChangesAsync();
        });
        return seeded;
    }

    [Theory]
    [InlineData(PortalRoles.Admin)]
    [InlineData(PortalRoles.Student)]
    [InlineData(PortalRoles.Teacher)]
    [InlineData(PortalRoles.Coordinator)]
    public async Task Every_authenticated_role_gets_405_for_every_mutating_method_with_or_without_a_csrf_token(string role)
    {
        var seeded = await SeedEventAsync();
        var (client, userId) = await SignedInAsync(role);
        using var _ = client;
        var urls = new[] { "/api/audit-events", $"/api/audit-events/{seeded.Id}", "/api/audit-events/not-an-id" };
        var attempts = 0;

        foreach (var url in urls)
        {
            foreach (var method in MutatingMethods)
            {
                foreach (var withToken in new[] { true, false })
                {
                    var response = await client.SendAsync(new HttpMethod(method), url, new { eventType = "forged.event" }, withAntiforgery: withToken);

                    Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
                    Assert.Equal("GET", Assert.Single(response.Content.Headers.Allow));
                    Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                    attempts++;
                }
            }
        }

        var recorded = await factory.AuditEventsAsync("audit.mutation.attempted");
        Assert.Equal(attempts, recorded.Count);
        Assert.All(recorded, e =>
        {
            Assert.Equal(AuditOutcome.Denied, e.Outcome);
            Assert.Equal(AuditEventCategory.Security, e.Category);
            Assert.Equal(userId, e.ActorUserId);
        });
        foreach (var method in MutatingMethods)
        {
            Assert.Equal(urls.Length * 2, recorded.Count(e => JsonDocument.Parse(e.MetadataJson!).RootElement.GetProperty("method").GetString() == method));
        }

        // Exactly one event per attempt: the refusal is not also an access-denied event.
        Assert.Empty(await factory.AuditEventsAsync("access.denied"));
    }

    [Fact]
    public async Task The_event_names_the_kind_of_target_never_the_client_supplied_identifier()
    {
        var (client, _) = await SignedInAsync(PortalRoles.Admin);
        using var _1 = client;

        await client.SendAsync(HttpMethod.Delete, "/api/audit-events/attacker-chosen-identifier");
        await client.SendAsync(HttpMethod.Post, "/api/audit-events");

        var recorded = await factory.AuditEventsAsync("audit.mutation.attempted");
        Assert.Contains(recorded, e => e.MetadataJson!.Contains("\"target\":\"event\""));
        Assert.Contains(recorded, e => e.MetadataJson!.Contains("\"target\":\"collection\""));
        Assert.DoesNotContain(recorded, e => e.MetadataJson!.Contains("attacker-chosen-identifier"));
    }

    [Fact]
    public async Task An_anonymous_attempt_gets_401_and_records_nothing()
    {
        using var client = factory.CreatePortalClient();

        foreach (var method in MutatingMethods)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(new HttpMethod(method), "/api/audit-events", new { }, withAntiforgery: false)).StatusCode);
        }

        Assert.Empty(await factory.AuditEventsAsync());
    }

    [Fact]
    public async Task No_attempt_changes_any_stored_event()
    {
        var seeded = await SeedEventAsync();
        var (client, _) = await SignedInAsync(PortalRoles.Admin);
        using var _1 = client;

        foreach (var method in MutatingMethods)
        {
            await client.SendAsync(new HttpMethod(method), $"/api/audit-events/{seeded.Id}", new { eventType = "tampered.event", outcome = "failure" });
        }

        var stored = (await factory.AuditEventsAsync("test.readonly")).Single();
        Assert.Equal(seeded.EventType, stored.EventType);
        Assert.Equal(seeded.Outcome, stored.Outcome);
        Assert.Equal(seeded.MetadataJson, stored.MetadataJson);
        Assert.Equal(seeded.OccurredAtUtc, stored.OccurredAtUtc);
    }

    [Fact]
    public async Task A_malformed_event_id_on_a_read_is_404_for_an_admin_and_never_advertises_mutation_methods()
    {
        var (admin, _) = await SignedInAsync(PortalRoles.Admin);
        using var _1 = admin;

        var response = await admin.GetAsync("/api/audit-events/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(response.Content.Headers.Allow);
    }

    [Fact]
    public void The_only_non_read_routes_under_the_audit_path_belong_to_the_mutation_guard()
    {
        var endpoints = factory.Services.GetServices<EndpointDataSource>().SelectMany(s => s.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("api/audit-events", StringComparison.OrdinalIgnoreCase) == true)
            .ToList();

        Assert.NotEmpty(endpoints);
        foreach (var endpoint in endpoints)
        {
            var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? [];
            var controller = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType();
            var mutating = methods.Where(m => MutatingMethods.Contains(m, StringComparer.OrdinalIgnoreCase)).ToList();
            if (mutating.Count > 0)
            {
                Assert.Equal(typeof(AuditMutationGuardController), controller);
            }
            else
            {
                Assert.Equal(["GET"], methods);
            }
        }
    }
}
