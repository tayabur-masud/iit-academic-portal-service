using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IitAcademicPortal.Infrastructure.Persistence;

public class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<PortalUser>(options)
{
    // Fixed keys so the supported role catalog is seeded deterministically by migrations.
    private static readonly (string Id, string Name)[] RoleCatalog =
    [
        ("6f1f2a0e-3c1b-4a43-9b2e-1a7d4c2e0001", PortalRoles.Admin),
        ("6f1f2a0e-3c1b-4a43-9b2e-1a7d4c2e0002", PortalRoles.Student),
        ("6f1f2a0e-3c1b-4a43-9b2e-1a7d4c2e0003", PortalRoles.Teacher),
        ("6f1f2a0e-3c1b-4a43-9b2e-1a7d4c2e0004", PortalRoles.Coordinator),
    ];

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();

    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    public DbSet<SecurityAuditOutboxItem> SecurityAuditOutbox => Set<SecurityAuditOutboxItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Email is the sign-in identifier, so a normalized email must resolve to exactly one account.
        builder.Entity<PortalUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        builder.Entity<PortalUser>().Property(u => u.DefaultRole).HasMaxLength(32);

        builder.Entity<IdentityRole>().HasData(RoleCatalog.Select(r => new IdentityRole
        {
            Id = r.Id,
            Name = r.Name,
            NormalizedName = r.Name.ToUpperInvariant(),
            ConcurrencyStamp = r.Id,
        }));

        builder.Entity<AuthSession>(session =>
        {
            session.ToTable("AuthSessions");
            session.HasKey(s => s.Id);
            session.Property(s => s.Id).ValueGeneratedNever();
            session.Property(s => s.HandleDigest).HasMaxLength(64).IsRequired();
            session.HasIndex(s => s.HandleDigest).IsUnique();
            session.Property(s => s.UserId).IsRequired();
            session.HasOne<PortalUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
            session.Property(s => s.ActiveRole).HasMaxLength(32);
            session.Property(s => s.CreatedAt).IsRequired();
            session.Property(s => s.LastActivityAt)
                .HasConversion(new ValueConverter<DateTimeOffset, long>(
                    value => value.UtcDateTime.Ticks,
                    ticks => new DateTimeOffset(ticks, TimeSpan.Zero)))
                .IsRequired();
            session.Property(s => s.RevocationReason).HasConversion<string>().HasMaxLength(32);
            session.Ignore(s => s.IsRevoked);
        });

        // Audit timestamps are UTC. SQLite returns an unspecified kind, so the kind is restored on read.
        var utc = new ValueConverter<DateTime, DateTime>(
            value => value.ToUniversalTime(),
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        // Formal audit events carry no foreign keys to accounts or business rows, so history survives
        // deactivation or deletion of whatever it refers to. The runtime database role may only insert and read.
        builder.Entity<AuditEvent>(auditEvent =>
        {
            auditEvent.ToTable("AuditEvents");
            auditEvent.HasKey(e => e.Id);
            auditEvent.Property(e => e.Id).ValueGeneratedNever();
            auditEvent.Property(e => e.OccurredAtUtc).HasConversion(utc).IsRequired();
            auditEvent.Property(e => e.Category).HasConversion<string>().HasMaxLength(16).IsRequired();
            auditEvent.Property(e => e.EventType).HasMaxLength(AuditEvent.MaxEventTypeLength).IsRequired();
            auditEvent.Property(e => e.Outcome).HasConversion<string>().HasMaxLength(16).IsRequired();
            auditEvent.Property(e => e.ActorUserId).HasMaxLength(AuditEvent.MaxActorUserIdLength);
            auditEvent.Property(e => e.EntityType).HasMaxLength(AuditEvent.MaxEntityTypeLength);
            auditEvent.Property(e => e.EntityId).HasMaxLength(AuditEvent.MaxEntityIdLength);
            auditEvent.Property(e => e.CorrelationId).HasMaxLength(AuditEvent.MaxCorrelationIdLength);
            auditEvent.Property(e => e.Source).HasMaxLength(AuditEvent.MaxSourceLength);

            // Newest-first keyset paging, plus the supported equality filters.
            auditEvent.HasIndex(e => new { e.OccurredAtUtc, e.Id }).IsDescending(true, true).HasDatabaseName("IX_AuditEvents_OccurredAtUtc_Id");
            auditEvent.HasIndex(e => e.ActorUserId);
            auditEvent.HasIndex(e => e.EventType);
            auditEvent.HasIndex(e => e.Category);
            auditEvent.HasIndex(e => e.Outcome);
            auditEvent.HasIndex(e => e.CorrelationId);
            auditEvent.HasIndex(e => new { e.EntityType, e.EntityId });
        });

        builder.Entity<SecurityAuditOutboxItem>(item =>
        {
            item.ToTable("SecurityAuditOutbox");
            item.HasKey(i => i.EventId);
            item.Property(i => i.EventId).ValueGeneratedNever();
            item.Property(i => i.EnvelopeJson).IsRequired();
            item.Property(i => i.EnqueuedAtUtc).HasConversion(utc).IsRequired();
            item.Property(i => i.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            item.Property(i => i.NextAttemptAtUtc).HasConversion(utc);
            item.Property(i => i.LastAttemptAtUtc).HasConversion(utc);
            item.Property(i => i.LeaseUntilUtc).HasConversion(utc);
            item.Property(i => i.DeliveredAtUtc).HasConversion(utc);
            item.Property(i => i.HandledAtUtc).HasConversion(utc);
            item.Property(i => i.LastFailureCode).HasMaxLength(64);
            item.Property(i => i.HandledBy).HasMaxLength(450);
            item.Property(i => i.HandledReason).HasMaxLength(500);
            item.HasIndex(i => new { i.State, i.NextAttemptAtUtc });
        });
    }
}
