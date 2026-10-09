using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Infrastructure.Auditing;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// The operator recovery function against real PostgreSQL: only the schema owner can run it, it needs an operator
/// and a reason, it changes only exhausted items, and it never touches formal audit events.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class PostgresAuditRecoveryTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private async Task EnsureFunctionAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "postgresql", "audit-outbox-recovery.sql"));
        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(ddl, script);
    }

    private async Task<Guid> SeedItemAsync(OutboxDeliveryState state)
    {
        var auditEvent = new AuditEvent(
            Guid.NewGuid(), DateTime.UtcNow, AuditEventCategory.Security, "test.recovery", AuditOutcome.Success,
            null, "Test", Guid.NewGuid().ToString(), null, null, null, null);
        await using var db = Db.CreateRuntimeContext();
        db.SecurityAuditOutbox.Add(new SecurityAuditOutboxItem(auditEvent.Id, AuditEnvelope.From(auditEvent).ToJson(), DateTime.UtcNow));
        await db.SaveChangesAsync();
        await using var connection = await Db.OpenAsync(Db.RuntimeConnectionString);
        await PostgresTestDatabase.ExecuteAsync(
            connection, $"UPDATE \"SecurityAuditOutbox\" SET \"State\" = '{state}', \"AttemptCount\" = 3 WHERE \"EventId\" = '{auditEvent.Id}'");
        return auditEvent.Id;
    }

    private async Task<SecurityAuditOutboxItem> ItemAsync(Guid id)
    {
        await using var db = Db.CreateRuntimeContext();
        return await db.SecurityAuditOutbox.AsNoTracking().SingleAsync(i => i.EventId == id);
    }

    private async Task<int> RecoverAsync(string action, string? operatorId, string? reason, Guid? eventId = null)
    {
        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        await using var command = new NpgsqlCommand("SELECT audit_outbox_recover(@action, @operator, @reason, @eventId)", ddl);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("operator", (object?)operatorId ?? DBNull.Value);
        command.Parameters.AddWithValue("reason", (object?)reason ?? DBNull.Value);
        command.Parameters.Add(new NpgsqlParameter("eventId", NpgsqlTypes.NpgsqlDbType.Uuid) { Value = (object?)eventId ?? DBNull.Value });
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task An_operator_and_a_reason_are_required_and_the_action_must_be_known()
    {
        await EnsureFunctionAsync();
        var id = await SeedItemAsync(OutboxDeliveryState.Exhausted);

        foreach (var (action, op, reason) in new (string, string?, string?)[]
        {
            ("retry", null, "why"), ("retry", "  ", "why"), ("retry", "op-1", null), ("handled", "op-1", " "), ("delete", "op-1", "why"),
        })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => RecoverAsync(action, op, reason, id));
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
        }

        Assert.Equal(OutboxDeliveryState.Exhausted, (await ItemAsync(id)).State);
    }

    [PostgresFact]
    public async Task Retry_returns_only_the_chosen_exhausted_item_to_the_queue_and_records_who_and_why()
    {
        await EnsureFunctionAsync();
        var chosen = await SeedItemAsync(OutboxDeliveryState.Exhausted);
        var untouched = await SeedItemAsync(OutboxDeliveryState.Exhausted);
        var before = await ItemAsync(chosen);

        Assert.Equal(1, await RecoverAsync("retry", "operator-7", "The audit store is back.", chosen));

        var retried = await ItemAsync(chosen);
        Assert.Equal(OutboxDeliveryState.Pending, retried.State);
        Assert.Equal(0, retried.AttemptCount);
        Assert.Equal(before.EnvelopeJson, retried.EnvelopeJson);
        Assert.Equal("operator-7", retried.HandledBy);
        Assert.Equal("The audit store is back.", retried.HandledReason);
        Assert.NotNull(retried.HandledAtUtc);
        Assert.Equal(OutboxDeliveryState.Exhausted, (await ItemAsync(untouched)).State);
    }

    [PostgresFact]
    public async Task Handled_marks_the_chosen_item_keeps_the_envelope_and_deletes_nothing()
    {
        await EnsureFunctionAsync();
        var chosen = await SeedItemAsync(OutboxDeliveryState.Exhausted);
        var before = await ItemAsync(chosen);
        int Rows() { using var db = Db.CreateRuntimeContext(); return db.SecurityAuditOutbox.Count(); }
        var rowsBefore = Rows();

        Assert.Equal(1, await RecoverAsync("handled", "operator-8", "Restored from the fallback sink.", chosen));

        var handled = await ItemAsync(chosen);
        Assert.Equal(OutboxDeliveryState.Handled, handled.State);
        Assert.Equal("operator-8", handled.HandledBy);
        Assert.Equal("Restored from the fallback sink.", handled.HandledReason);
        Assert.NotNull(handled.HandledAtUtc);
        Assert.Equal(before.EnvelopeJson, handled.EnvelopeJson);
        Assert.Equal(rowsBefore, Rows());
    }

    [PostgresFact]
    public async Task Only_exhausted_items_are_ever_changed()
    {
        await EnsureFunctionAsync();
        var pending = await SeedItemAsync(OutboxDeliveryState.Pending);
        var scheduled = await SeedItemAsync(OutboxDeliveryState.RetryScheduled);
        var delivered = await SeedItemAsync(OutboxDeliveryState.Delivered);

        foreach (var id in new[] { pending, scheduled, delivered })
        {
            Assert.Equal(0, await RecoverAsync("retry", "operator-9", "should not apply", id));
            Assert.Equal(0, await RecoverAsync("handled", "operator-9", "should not apply", id));
        }

        Assert.Equal(OutboxDeliveryState.Pending, (await ItemAsync(pending)).State);
        Assert.Equal(OutboxDeliveryState.RetryScheduled, (await ItemAsync(scheduled)).State);
        Assert.Equal(OutboxDeliveryState.Delivered, (await ItemAsync(delivered)).State);
        Assert.Null((await ItemAsync(pending)).HandledBy);
    }

    [PostgresFact]
    public async Task Recovery_never_touches_formal_audit_events()
    {
        await EnsureFunctionAsync();
        await SeedItemAsync(OutboxDeliveryState.Exhausted);
        async Task<string> SnapshotAsync()
        {
            await using var db = Db.CreateRuntimeContext();
            var events = await db.AuditEvents.AsNoTracking().OrderBy(e => e.Id).ToListAsync();
            return events.Count + ":" + string.Join(",", events.Select(e => $"{e.Id}/{e.EventType}/{e.Outcome}"));
        }

        await using (var db = Db.CreateRuntimeContext())
        {
            db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), DateTime.UtcNow, AuditEventCategory.Security, "test.keep", AuditOutcome.Success, null, null, null, null, null, null, null));
            await db.SaveChangesAsync();
        }

        var before = await SnapshotAsync();
        await RecoverAsync("retry", "operator-1", "check");
        await RecoverAsync("handled", "operator-1", "check");

        Assert.Equal(before, await SnapshotAsync());
    }

    [PostgresFact]
    public async Task The_runtime_role_cannot_run_the_recovery_function()
    {
        await EnsureFunctionAsync();
        await using var runtime = await Db.OpenAsync(Db.RuntimeConnectionString);

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(runtime, "SELECT audit_outbox_recover('retry', 'attacker', 'trying')"));

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }

    [PostgresFact]
    public async Task A_retried_item_is_delivered_by_the_normal_worker_under_the_restricted_role()
    {
        await EnsureFunctionAsync();
        var id = await SeedItemAsync(OutboxDeliveryState.Exhausted);
        await RecoverAsync("retry", "operator-2", "retry after the incident", id);

        var processor = new SecurityAuditOutboxProcessor(
            new SecurityAuditOutboxStore(Db.CreateRuntimeFactory()), Options.Create(new AuditDeliveryOptions()),
            TimeProvider.System, new AuditMetrics(), NullLogger<SecurityAuditOutboxProcessor>.Instance);
        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None) >= 1);

        Assert.Equal(OutboxDeliveryState.Delivered, (await ItemAsync(id)).State);
        await using var read = Db.CreateRuntimeContext();
        Assert.Equal(1, await read.AuditEvents.CountAsync(e => e.Id == id));
    }
}
