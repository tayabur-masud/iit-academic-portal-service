using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using IitAcademicPortal.Infrastructure.Auditing;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// The append-only boundary, whole-schema grants, concurrency, and delivery, verified against a real PostgreSQL
/// database with the same two-account setup a deployment uses. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class PostgresAuditPermissionsTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private static AuditEvent NewEvent(DateTime? at = null, string? actor = null, string? entityId = null, string type = "test.pg") =>
        new(Guid.NewGuid(), at ?? DateTime.UtcNow, AuditEventCategory.Security, type, AuditOutcome.Success,
            actor, "Test", entityId, null, null, null, null);

    private static void AssertDenied(Exception? exception)
    {
        var postgres = Assert.IsType<PostgresException>(exception);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgres.SqlState);
    }

    [PostgresFact]
    public async Task The_runtime_role_can_append_and_read_audit_events()
    {
        var added = NewEvent(actor: "pg-actor-1");
        await using (var db = Db.CreateRuntimeContext())
        {
            db.AuditEvents.Add(added);
            await db.SaveChangesAsync();
        }

        await using var read = Db.CreateRuntimeContext();
        var stored = await read.AuditEvents.AsNoTracking().SingleAsync(e => e.Id == added.Id);
        Assert.Equal("pg-actor-1", stored.ActorUserId);
        Assert.Equal(DateTimeKind.Utc, stored.OccurredAtUtc.Kind);
    }

    [PostgresFact]
    public async Task The_runtime_role_cannot_update_delete_or_truncate_audit_events()
    {
        var added = NewEvent();
        await using (var db = Db.CreateRuntimeContext())
        {
            db.AuditEvents.Add(added);
            await db.SaveChangesAsync();
        }

        await using var connection = await Db.OpenAsync(Db.RuntimeConnectionString);
        foreach (var statement in new[]
        {
            $"UPDATE \"AuditEvents\" SET \"EventType\" = 'tampered.type' WHERE \"Id\" = '{added.Id}'",
            $"DELETE FROM \"AuditEvents\" WHERE \"Id\" = '{added.Id}'",
            "TRUNCATE \"AuditEvents\"",
        })
        {
            var error = await Record.ExceptionAsync(() => PostgresTestDatabase.ExecuteAsync(connection, statement));
            AssertDenied(error);
        }

        await using var read = Db.CreateRuntimeContext();
        Assert.Equal("test.pg", (await read.AuditEvents.AsNoTracking().SingleAsync(e => e.Id == added.Id)).EventType);
    }

    [PostgresFact]
    public async Task The_runtime_role_can_change_outbox_state_but_never_delete_a_row()
    {
        var id = Guid.NewGuid();
        await using (var db = Db.CreateRuntimeContext())
        {
            db.SecurityAuditOutbox.Add(new SecurityAuditOutboxItem(id, "{}", DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using var connection = await Db.OpenAsync(Db.RuntimeConnectionString);
        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(connection, $"UPDATE \"SecurityAuditOutbox\" SET \"AttemptCount\" = 1 WHERE \"EventId\" = '{id}'"));
        AssertDenied(await Record.ExceptionAsync(() => PostgresTestDatabase.ExecuteAsync(connection, $"DELETE FROM \"SecurityAuditOutbox\" WHERE \"EventId\" = '{id}'")));
        AssertDenied(await Record.ExceptionAsync(() => PostgresTestDatabase.ExecuteAsync(connection, "TRUNCATE \"SecurityAuditOutbox\"")));
    }

    [PostgresFact]
    public async Task The_runtime_role_owns_nothing_and_holds_no_elevated_attributes()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);

        await using var owner = new NpgsqlCommand("SELECT tableowner FROM pg_tables WHERE schemaname = 'public' AND tablename = 'AuditEvents'", connection);
        Assert.Equal(Db.DdlRole, (string?)await owner.ExecuteScalarAsync());

        await using var ownedByRuntime = new NpgsqlCommand("SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tableowner = @role", connection);
        ownedByRuntime.Parameters.AddWithValue("role", Db.RuntimeRole);
        Assert.Equal(0L, (long?)await ownedByRuntime.ExecuteScalarAsync());

        await using var attributes = new NpgsqlCommand("SELECT rolsuper OR rolcreatedb OR rolcreaterole OR rolbypassrls FROM pg_roles WHERE rolname = @role", connection);
        attributes.Parameters.AddWithValue("role", Db.RuntimeRole);
        Assert.Equal(false, (bool?)await attributes.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task The_application_works_on_the_existing_identity_and_session_tables_under_the_runtime_role()
    {
        var user = new PortalUser { UserName = "pg.user@iit.test", Email = "pg.user@iit.test", NormalizedEmail = "PG.USER@IIT.TEST", NormalizedUserName = "PG.USER@IIT.TEST" };
        await using var db = Db.CreateRuntimeContext();
        db.Users.Add(user);
        db.AuthSessions.Add(new AuthSession(user.Id, Guid.NewGuid().ToString("N"), null, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        user.DefaultRole = "Teacher";
        await db.SaveChangesAsync();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Id == user.Id && u.DefaultRole == "Teacher"));

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        Assert.Equal(0, await db.Users.CountAsync(u => u.Id == user.Id));
    }

    [PostgresFact]
    public async Task A_table_created_by_a_later_migration_is_usable_without_a_new_grant()
    {
        await using (var ddl = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(ddl, "CREATE TABLE \"LaterFeatureTable\" (\"Id\" serial PRIMARY KEY, \"Name\" text)");
        }

        await using var runtime = await Db.OpenAsync(Db.RuntimeConnectionString);
        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(runtime, "INSERT INTO \"LaterFeatureTable\" (\"Name\") VALUES ('a')"));
        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(runtime, "UPDATE \"LaterFeatureTable\" SET \"Name\" = 'b'"));
        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(runtime, "DELETE FROM \"LaterFeatureTable\""));
    }

    [PostgresFact]
    public async Task Event_identifiers_are_unique()
    {
        var first = NewEvent();
        await using (var db = Db.CreateRuntimeContext())
        {
            db.AuditEvents.Add(first);
            await db.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateRuntimeContext();
        duplicate.AuditEvents.Add(new AuditEvent(first.Id, DateTime.UtcNow, AuditEventCategory.Security, "test.pg", AuditOutcome.Success, null, null, null, null, null, null, null));
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }

    [PostgresFact]
    public async Task Concurrent_inserts_with_identical_timestamps_lose_nothing_and_page_without_duplicates()
    {
        var marker = $"concurrent-{Guid.NewGuid():N}";
        var instant = DateTime.UtcNow;
        var ids = Enumerable.Range(0, 50).Select(_ => Guid.NewGuid()).ToList();

        // Each unit runs on the thread pool so the test runner's limited synchronization context cannot serialize them.
        await Task.WhenAll(ids.Select(id => Task.Run(async () =>
        {
            await using var db = Db.CreateRuntimeContext();
            db.AuditEvents.Add(new AuditEvent(id, instant, AuditEventCategory.Security, "test.pg", AuditOutcome.Success, null, "Test", marker, null, null, null, null));
            await db.SaveChangesAsync();
        })));

        // Keyset paging with seven per page, all at the same instant: ordering falls back to the event ID.
        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            await using var db = Db.CreateRuntimeContext();
            var page = await new AuditEventStore(db).SearchAsync(
                new AuditSearchCriteria(null, null, null, null, null, "Test", marker, null, null, 7, cursor), CancellationToken.None);
            seen.AddRange(page.Items.Select(e => e.Id));
            cursor = page.NextCursor;
        }
        while (cursor is not null);

        Assert.Equal(50, seen.Count);
        Assert.Equal(50, seen.Distinct().Count());
        Assert.True(ids.ToHashSet().SetEquals(seen));
    }

    [PostgresFact]
    public async Task Historical_identifiers_survive_deletion_of_the_account_they_refer_to()
    {
        var user = new PortalUser { UserName = "gone@iit.test", Email = "gone@iit.test", NormalizedEmail = "GONE@IIT.TEST", NormalizedUserName = "GONE@IIT.TEST" };
        var historic = NewEvent(actor: user.Id, entityId: user.Id);
        await using (var db = Db.CreateRuntimeContext())
        {
            db.Users.Add(user);
            db.AuditEvents.Add(historic);
            await db.SaveChangesAsync();
            db.Users.Remove(user);
            await db.SaveChangesAsync();
        }

        await using var read = Db.CreateRuntimeContext();
        Assert.Equal(0, await read.Users.CountAsync(u => u.Id == user.Id));
        var stored = await read.AuditEvents.AsNoTracking().SingleAsync(e => e.Id == historic.Id);
        Assert.Equal(user.Id, stored.ActorUserId);
        Assert.Equal(user.Id, stored.EntityId);
    }

    [PostgresFact]
    public async Task Delivery_works_under_the_restricted_role_and_a_replay_creates_no_second_event()
    {
        var store = new SecurityAuditOutboxStore(Db.CreateRuntimeFactory());
        var processor = new SecurityAuditOutboxProcessor(
            store, Options.Create(new AuditDeliveryOptions()), TimeProvider.System, new AuditMetrics(), NullLogger<SecurityAuditOutboxProcessor>.Instance);
        var auditEvent = NewEvent(entityId: $"delivery-{Guid.NewGuid():N}");
        await store.EnqueueAsync(auditEvent.Id, AuditEnvelope.From(auditEvent).ToJson(), auditEvent.OccurredAtUtc, CancellationToken.None);

        Assert.True(await processor.ProcessOnceAsync(CancellationToken.None) >= 1);

        await using var read = Db.CreateRuntimeContext();
        Assert.Equal(1, await read.AuditEvents.CountAsync(e => e.Id == auditEvent.Id));
        var delivered = await read.SecurityAuditOutbox.AsNoTracking().SingleAsync(i => i.EventId == auditEvent.Id);
        Assert.Equal(OutboxDeliveryState.Delivered, delivered.State);

        await store.DeliverAsync(delivered, DateTime.UtcNow, CancellationToken.None);
        Assert.Equal(1, await read.AuditEvents.CountAsync(e => e.Id == auditEvent.Id));
    }

    [PostgresFact]
    public async Task Two_workers_claiming_at_once_never_claim_the_same_item()
    {
        var store = new SecurityAuditOutboxStore(Db.CreateRuntimeFactory());
        var item = NewEvent();
        await store.EnqueueAsync(item.Id, AuditEnvelope.From(item).ToJson(), item.OccurredAtUtc, CancellationToken.None);
        var now = DateTime.UtcNow;

        var claims = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            Task.Run(() => store.ClaimDueAsync(now, TimeSpan.FromMinutes(1), 1000, CancellationToken.None))));

        Assert.Equal(1, claims.SelectMany(c => c).Count(i => i.EventId == item.Id));
    }
}
