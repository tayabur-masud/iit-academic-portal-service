using IitAcademicPortal.Application.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace IitAcademicPortal.Api.Tests.Auditing.Fixtures;

/// <summary>
/// A test-only stand-in for a business record, so the atomic business-event behavior can be proven before any
/// business feature exists. It is mapped only in the test host and never appears in the production model.
/// </summary>
public sealed class StandInBusinessEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "draft";

    public string InternalNote { get; set; } = string.Empty;
}

/// <summary>The definition a future owning feature would declare for one of its mandatory events.</summary>
public static class StandInAuditDefinitions
{
    public static AuditEventDefinition Updated { get; } =
        AuditEventDefinition.Business("standin.updated", "name", "status").WithChangeIndicators("internalNote");

    /// <summary>An event type that the test database is rigged to reject.</summary>
    public static AuditEventDefinition Forced { get; } = AuditEventDefinition.Business("standin.fail", "name");
}

/// <summary>Adds the stand-in entity to the model in the test host only.</summary>
public sealed class StandInModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        modelBuilder.Entity<StandInBusinessEntity>(entity =>
        {
            entity.ToTable("StandInBusinessEntities");
            entity.HasKey(e => e.Id);
        });
    }
}
