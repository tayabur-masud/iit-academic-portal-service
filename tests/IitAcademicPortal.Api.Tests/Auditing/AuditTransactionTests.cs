using System.Text.Json;
using IitAcademicPortal.Api.Tests.Auditing.Fixtures;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// A mandatory business event and its business change commit together or not at all, proven with a
/// test-only stand-in entity because no business feature exists yet.
/// </summary>
public sealed class AuditTransactionTests : IDisposable
{
    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static AuditEventRequest Update(StandInBusinessEntity entity, AuditEventDefinition? definition = null, AuditOutcome outcome = AuditOutcome.Success) =>
        new(definition ?? StandInAuditDefinitions.Updated, outcome)
        {
            ActorUserId = "admin-1",
            EntityType = "StandIn",
            EntityId = entity.Id.ToString(),
            Changes =
            [
                new AuditFieldChange("name", "old name", entity.Name),
                new AuditFieldChange("status", "draft", entity.Status),
                new AuditFieldChange("internalNote", "before", "after"),
                new AuditFieldChange("unlistedField", "x", "y"),
            ],
        };

    [Fact]
    public async Task The_business_change_and_its_event_commit_together_with_only_approved_values()
    {
        var entity = new StandInBusinessEntity { Name = "new name", Status = "approved" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Add(entity);
            scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().Stage(Update(entity));
            await db.SaveChangesAsync();
        }

        await factory.WithDbAsync(async db =>
        {
            Assert.NotNull(await db.Set<StandInBusinessEntity>().FindAsync(entity.Id));
            var stored = Assert.Single(await db.AuditEvents.Where(e => e.EntityId == entity.Id.ToString()).ToListAsync());
            Assert.Equal(AuditEventCategory.Business, stored.Category);
            Assert.Equal("standin.updated", stored.EventType);
            Assert.Equal("admin-1", stored.ActorUserId);

            using var changes = JsonDocument.Parse(stored.ChangesJson!);
            var byField = changes.RootElement.EnumerateArray().ToDictionary(c => c.GetProperty("fieldName").GetString()!);
            Assert.Equal("old name", byField["name"].GetProperty("oldValue").GetString());
            Assert.Equal("new name", byField["name"].GetProperty("newValue").GetString());
            Assert.Equal("approved", byField["status"].GetProperty("newValue").GetString());
            Assert.True(byField["internalNote"].GetProperty("changed").GetBoolean());
            Assert.False(byField["internalNote"].TryGetProperty("oldValue", out _));
            Assert.DoesNotContain("unlistedField", stored.ChangesJson);
            Assert.DoesNotContain("before", stored.ChangesJson);
        });
    }

    [Fact]
    public async Task When_the_audit_write_fails_neither_the_business_change_nor_the_event_commits()
    {
        await factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON \"AuditEvents\" WHEN NEW.\"EventType\" = 'standin.fail' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;"));

        var entity = new StandInBusinessEntity { Name = "must not persist" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Add(entity);
            scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().Stage(Update(entity, StandInAuditDefinitions.Forced));

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        await factory.WithDbAsync(async db =>
        {
            Assert.Null(await db.Set<StandInBusinessEntity>().FindAsync(entity.Id));
            Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "standin.fail").ToListAsync());
        });
    }

    [Fact]
    public async Task An_invalid_event_stages_nothing_and_records_nothing()
    {
        var entity = new StandInBusinessEntity { Name = "x" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var recorder = scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>();
            var invalid = new AuditEventRequest(StandInAuditDefinitions.Updated, AuditOutcome.Success) { EntityId = new string('x', 500) };

            Assert.Throws<AuditValidationException>(() => recorder.Stage(invalid));
            await scope.ServiceProvider.GetRequiredService<PortalDbContext>().SaveChangesAsync();
        }

        await factory.WithDbAsync(async db => Assert.Empty(await db.AuditEvents.Where(e => e.EventType == "standin.updated").ToListAsync()));
        Assert.Empty(await factory.OutboxItemsAsync());
        Assert.NotNull(entity);
    }

    [Fact]
    public async Task A_policy_based_business_denial_is_recorded_alone_without_a_business_change()
    {
        var entity = new StandInBusinessEntity { Name = "denied" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().Stage(Update(entity, outcome: AuditOutcome.Denied));
            await scope.ServiceProvider.GetRequiredService<PortalDbContext>().SaveChangesAsync();
        }

        await factory.WithDbAsync(async db =>
        {
            Assert.Null(await db.Set<StandInBusinessEntity>().FindAsync(entity.Id));
            var stored = Assert.Single(await db.AuditEvents.Where(e => e.EntityId == entity.Id.ToString()).ToListAsync());
            Assert.Equal(AuditOutcome.Denied, stored.Outcome);
        });
    }

    [Fact]
    public async Task Business_events_do_not_use_the_outbox()
    {
        var entity = new StandInBusinessEntity { Name = "direct" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PortalDbContext>();
            db.Add(entity);
            scope.ServiceProvider.GetRequiredService<IAuditEventRecorder>().Stage(Update(entity));
            await db.SaveChangesAsync();
        }

        Assert.Empty(await factory.OutboxItemsAsync());
    }
}
