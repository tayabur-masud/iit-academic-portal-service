using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Auditing;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Monitoring signals for the audit path: the protected health check, critical logs, and counters.</summary>
public sealed class AuditRecoveryAndAlertTests : IDisposable
{
    private static readonly AuditEventDefinition TestSecurity = AuditEventDefinition.Security("test.alerts");

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task<PortalClient> SignedInAsync(string role)
    {
        var email = PortalApiFactory.UniqueEmail(role.ToLowerInvariant());
        await factory.CreateUserAsync(email, role);
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    private async Task RecordAsync(string marker = "alert-marker")
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
            new AuditEventRequest(TestSecurity, AuditOutcome.Success) { EntityId = marker, Metadata = new Dictionary<string, object?> { ["m"] = marker } });
    }

    private async Task ExhaustOneItemAsync()
    {
        await RecordAsync("exhausted-content-marker");
        factory.Outbox.FailNextDeliveries(100);
        var processor = factory.Services.GetRequiredService<SecurityAuditOutboxProcessor>();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await processor.ProcessOnceAsync(CancellationToken.None);
            factory.Clock.Advance(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task The_health_endpoint_is_healthy_when_the_outbox_is_draining()
    {
        using var admin = await SignedInAsync(PortalRoles.Admin);
        await RecordAsync();
        await factory.FlushAuditAsync();

        var response = await admin.GetAsync("/health/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await PortalClient.ReadJsonAsync(response);
        Assert.Equal("Healthy", json.GetProperty("status").GetString());
        Assert.Equal(0, json.GetProperty("exhausted").GetInt32());
    }

    [Fact]
    public async Task Exhausted_events_make_the_endpoint_unhealthy_without_exposing_event_content()
    {
        // Exhaust first: signing in records its own event, which must not be counted among the failed ones.
        await ExhaustOneItemAsync();
        using var admin = await SignedInAsync(PortalRoles.Admin);

        var response = await admin.GetAsync("/health/audit");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, json.RootElement.GetProperty("exhausted").GetInt32());
        Assert.DoesNotContain("exhausted-content-marker", body);
    }

    [Fact]
    public async Task Events_waiting_too_long_make_the_endpoint_degraded()
    {
        using var admin = await SignedInAsync(PortalRoles.Admin);
        await RecordAsync();
        factory.Clock.Advance(TimeSpan.FromMinutes(20));

        var response = await admin.GetAsync("/health/audit");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", (await PortalClient.ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task The_health_endpoint_is_protected_like_audit_review()
    {
        using var anonymous = factory.CreatePortalClient();
        using var student = await SignedInAsync(PortalRoles.Student);

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/health/audit")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await student.GetAsync("/health/audit")).StatusCode);
    }

    [Fact]
    public async Task Exhaustion_writes_a_critical_log_with_the_event_id_and_failure_type_but_no_content()
    {
        await ExhaustOneItemAsync();

        var critical = Assert.Single(factory.Logs.Messages, m => m.StartsWith("[Critical]") && m.Contains("awaits authorized recovery"));
        Assert.Contains(nameof(InvalidOperationException), critical);
        Assert.DoesNotContain("exhausted-content-marker", critical);
        Assert.Equal(1, factory.Metrics.Total("audit.outbox.retry_exhausted"));
    }

    [Fact]
    public async Task An_outbox_write_failure_writes_a_critical_log_and_the_counter_rises()
    {
        factory.Outbox.FailEnqueue = true;

        await RecordAsync("write-failure-marker");

        Assert.Contains(factory.Logs.Messages, m => m.StartsWith("[Critical]") && m.Contains("could not be written to the outbox"));
        Assert.Equal(1, factory.Metrics.Total("audit.outbox.write_failures"));
        Assert.DoesNotContain("write-failure-marker", factory.Logs.Everything);
    }

    [Fact]
    public async Task A_fallback_sink_failure_writes_a_critical_log_and_signals_event_loss_risk()
    {
        factory.Outbox.FailEnqueue = true;
        factory.FallbackSink.Fail = true;

        await RecordAsync();

        Assert.Contains(factory.Logs.Messages, m => m.StartsWith("[Critical]") && m.Contains("durable fallback sink"));
        Assert.Equal(1, factory.Metrics.Total("audit.fallback.failures"));
        Assert.Equal(1, factory.Metrics.Total("audit.event_loss_risk"));
    }

    [Fact]
    public async Task No_alert_path_records_another_audit_event()
    {
        factory.Outbox.FailEnqueue = true;
        factory.FallbackSink.Fail = true;

        await RecordAsync();
        factory.Outbox.FailEnqueue = false;
        await factory.FlushAuditAsync();

        // The failure handling itself enqueued and stored nothing: no event about the failure to record events.
        Assert.Empty(await factory.OutboxItemsAsync());
        Assert.Empty(await factory.AuditEventsAsyncWithoutFlush());
        Assert.Empty(factory.FallbackSink.Envelopes);
    }
}
