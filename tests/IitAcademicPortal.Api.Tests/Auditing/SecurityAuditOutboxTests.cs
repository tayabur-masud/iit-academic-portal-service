using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Infrastructure.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Durable delivery of security events: retry, restart, idempotency, leases, and exhaustion.</summary>
public sealed class SecurityAuditOutboxTests : IDisposable
{
    private static readonly AuditEventDefinition TestSecurity = AuditEventDefinition.Security("test.outbox");

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private SecurityAuditOutboxProcessor Processor => factory.Services.GetRequiredService<SecurityAuditOutboxProcessor>();

    private async Task<Guid> RecordAsync(string? marker = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().RecordSecurityAsync(
            new AuditEventRequest(TestSecurity, AuditOutcome.Success) { EntityId = marker ?? Guid.NewGuid().ToString() });
        return (await factory.OutboxItemsAsync()).Last().EventId;
    }

    private async Task<SecurityAuditOutboxItem> ItemAsync(Guid id) => (await factory.OutboxItemsAsync()).Single(i => i.EventId == id);

    [Fact]
    public async Task A_failed_delivery_is_retried_with_backoff_then_succeeds_exactly_once()
    {
        var id = await RecordAsync();
        factory.Outbox.FailNextDeliveries(1);

        Assert.Equal(1, await Processor.ProcessOnceAsync(CancellationToken.None));
        var afterFailure = await ItemAsync(id);
        Assert.Equal(OutboxDeliveryState.RetryScheduled, afterFailure.State);
        Assert.Equal(1, afterFailure.AttemptCount);
        Assert.Equal(nameof(InvalidOperationException), afterFailure.LastFailureCode);
        Assert.NotNull(afterFailure.NextAttemptAtUtc);
        Assert.Empty(await factory.AuditEventsAsyncWithoutFlush());

        // Not due yet: a second pass claims nothing.
        Assert.Equal(0, await Processor.ProcessOnceAsync(CancellationToken.None));

        factory.Clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, await Processor.ProcessOnceAsync(CancellationToken.None));

        var delivered = await ItemAsync(id);
        Assert.Equal(OutboxDeliveryState.Delivered, delivered.State);
        Assert.Equal(2, delivered.AttemptCount);
        Assert.Single(await factory.AuditEventsAsync("test.outbox"));
    }

    [Fact]
    public async Task Pending_events_survive_a_restart_because_state_lives_in_the_database()
    {
        var first = await RecordAsync();
        var second = await RecordAsync();

        // A brand-new processor with no memory of the earlier one, as after a process restart.
        var restarted = new SecurityAuditOutboxProcessor(
            factory.Services.GetRequiredService<ISecurityAuditOutboxStore>(),
            Options.Create(new AuditDeliveryOptions()),
            factory.Clock,
            new AuditMetrics(),
            NullLogger<SecurityAuditOutboxProcessor>.Instance);

        Assert.Equal(2, await restarted.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal(OutboxDeliveryState.Delivered, (await ItemAsync(first)).State);
        Assert.Equal(OutboxDeliveryState.Delivered, (await ItemAsync(second)).State);
    }

    [Fact]
    public async Task Replaying_a_delivered_event_does_not_create_a_second_formal_event()
    {
        var id = await RecordAsync();
        await factory.FlushAuditAsync();
        var item = await ItemAsync(id);

        // A crash after the insert but before the acknowledgement would replay the item.
        await factory.Services.GetRequiredService<ISecurityAuditOutboxStore>().DeliverAsync(item, factory.Clock.GetUtcNow().UtcDateTime, CancellationToken.None);

        var stored = await factory.AuditEventsAsync("test.outbox");
        Assert.Equal(1, stored.Count(e => e.Id == id));
        Assert.Equal(OutboxDeliveryState.Delivered, (await ItemAsync(id)).State);
    }

    [Fact]
    public async Task An_item_claimed_by_one_worker_cannot_be_claimed_by_another_until_its_lease_expires()
    {
        await RecordAsync();
        var store = factory.Services.GetRequiredService<ISecurityAuditOutboxStore>();
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        var lease = TimeSpan.FromMinutes(1);

        Assert.Single(await store.ClaimDueAsync(now, lease, 10, CancellationToken.None));
        Assert.Empty(await store.ClaimDueAsync(now, lease, 10, CancellationToken.None));

        // The first worker crashed; once its lease expires the item is claimed again.
        Assert.Single(await store.ClaimDueAsync(now + TimeSpan.FromMinutes(2), lease, 10, CancellationToken.None));
    }

    [Fact]
    public async Task After_the_automatic_limit_the_event_is_retained_exhausted_and_an_alert_counter_rises()
    {
        var id = await RecordAsync();
        factory.Outbox.FailNextDeliveries(100);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            Assert.Equal(1, await Processor.ProcessOnceAsync(CancellationToken.None));
            factory.Clock.Advance(TimeSpan.FromSeconds(30));
        }

        var item = await ItemAsync(id);
        Assert.Equal(OutboxDeliveryState.Exhausted, item.State);
        Assert.Equal(3, item.AttemptCount);
        Assert.False(string.IsNullOrEmpty(item.EnvelopeJson));
        Assert.Null(item.NextAttemptAtUtc);
        Assert.Equal(1, factory.Metrics.Total("audit.outbox.retry_exhausted"));

        // Exhausted work is never retried automatically and never discarded.
        factory.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(0, await Processor.ProcessOnceAsync(CancellationToken.None));
        Assert.Equal(OutboxDeliveryState.Exhausted, (await ItemAsync(id)).State);
        Assert.Empty(await factory.AuditEventsAsyncWithoutFlush());
    }

    [Fact]
    public async Task The_status_reports_pending_and_exhausted_counts_for_monitoring()
    {
        await RecordAsync();
        var store = factory.Services.GetRequiredService<ISecurityAuditOutboxStore>();

        var before = await store.GetStatusAsync(CancellationToken.None);
        Assert.Equal(1, before.Pending);
        Assert.NotNull(before.OldestUndeliveredEnqueuedAtUtc);

        factory.Outbox.FailNextDeliveries(100);
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await Processor.ProcessOnceAsync(CancellationToken.None);
            factory.Clock.Advance(TimeSpan.FromSeconds(30));
        }

        var after = await store.GetStatusAsync(CancellationToken.None);
        Assert.Equal(0, after.Pending);
        Assert.Equal(1, after.Exhausted);
    }
}
