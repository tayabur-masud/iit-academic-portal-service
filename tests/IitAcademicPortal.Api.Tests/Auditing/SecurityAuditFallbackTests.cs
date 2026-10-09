using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>When the outbox write itself fails, the safe envelope goes to the durable fallback sink.</summary>
public sealed class SecurityAuditFallbackTests : IDisposable
{
    private static readonly AuditEventDefinition TestSecurity = AuditEventDefinition.Security("test.fallback");

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private async Task RecordAsync(Dictionary<string, object?>? metadata = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
            new AuditEventRequest(TestSecurity, AuditOutcome.Denied) { ActorUserId = "actor-1", Metadata = metadata });
    }

    [Fact]
    public async Task An_outbox_failure_never_throws_and_the_envelope_reaches_the_durable_sink()
    {
        factory.Outbox.FailEnqueue = true;

        await RecordAsync();

        var envelope = Assert.Single(factory.FallbackSink.Envelopes);
        var stored = AuditEnvelope.FromJson(envelope);
        Assert.Equal("test.fallback", stored.EventType);
        Assert.Equal("actor-1", stored.ActorUserId);
        Assert.Equal(1, factory.Metrics.Total("audit.outbox.write_failures"));
        Assert.Equal(0, factory.Metrics.Total("audit.fallback.failures"));
        Assert.Equal(0, factory.Metrics.Total("audit.event_loss_risk"));
        Assert.Empty(await factory.OutboxItemsAsync());
    }

    [Fact]
    public async Task The_envelope_sent_to_the_sink_is_already_redacted()
    {
        factory.Outbox.FailEnqueue = true;

        await RecordAsync(new Dictionary<string, object?> { ["password"] = "hunter2", ["note"] = "kept" });

        var envelope = Assert.Single(factory.FallbackSink.Envelopes);
        Assert.DoesNotContain("hunter2", envelope);
        Assert.Contains("kept", envelope);
    }

    [Fact]
    public async Task If_the_sink_also_fails_the_caller_is_unaffected_and_loss_risk_alerts_fire()
    {
        factory.Outbox.FailEnqueue = true;
        factory.FallbackSink.Fail = true;

        await RecordAsync();

        Assert.Empty(factory.FallbackSink.Envelopes);
        Assert.Equal(1, factory.Metrics.Total("audit.outbox.write_failures"));
        Assert.Equal(1, factory.Metrics.Total("audit.fallback.failures"));
        Assert.Equal(1, factory.Metrics.Total("audit.event_loss_risk"));
    }

    [Fact]
    public async Task A_working_outbox_does_not_touch_the_fallback_sink()
    {
        await RecordAsync();

        Assert.Empty(factory.FallbackSink.Envelopes);
        Assert.Single(await factory.OutboxItemsAsync());
        Assert.Equal(0, factory.Metrics.Total("audit.outbox.write_failures"));
    }

    [Fact]
    public void Outside_Development_the_service_refuses_to_start_without_a_durable_sink()
    {
        using var host = new ProductionHostFactory(sink: null, directory: null);

        var failure = Assert.ThrowsAny<Exception>(() => host.CreateClient());

        Assert.Contains("AuditFallback", failure.ToString());
    }

    [Fact]
    public void Outside_Development_a_configured_sink_lets_the_service_start()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iit-audit-tests", Guid.NewGuid().ToString("N"));
        using var host = new ProductionHostFactory(sink: "File", directory: directory);

        using var client = host.CreateClient();

        Assert.NotNull(client);
    }

    private sealed class ProductionHostFactory(string? sink, string? directory) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Portal", "Host=unused");
            builder.UseSetting("PasswordRecovery:ResetPageUrl", "https://portal.test/reset-password");
            builder.UseSetting("AuditDelivery:WorkerEnabled", "false");
            if (sink is not null)
            {
                builder.UseSetting("AuditFallback:Sink", sink);
                builder.UseSetting("AuditFallback:Directory", directory);
            }
        }
    }
}
