using IitAcademicPortal.Domain;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;
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

    public DbSet<EducationalInstitution> EducationalInstitutions => Set<EducationalInstitution>();

    public DbSet<EducationBoard> EducationBoards => Set<EducationBoard>();

    public DbSet<EducationGroup> EducationGroups => Set<EducationGroup>();

    public DbSet<ExaminationType> ExaminationTypes => Set<ExaminationType>();

    public DbSet<Degree> Degrees => Set<Degree>();

    public DbSet<Semester> Semesters => Set<Semester>();

    public DbSet<AcademicSession> AcademicSessions => Set<AcademicSession>();

    public DbSet<Designation> Designations => Set<Designation>();

    public DbSet<Hall> Halls => Set<Hall>();

    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<Division> Divisions => Set<Division>();

    public DbSet<District> Districts => Set<District>();

    public DbSet<Upazila> Upazilas => Set<Upazila>();

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

        ConfigureConfiguration(builder);
        ConfigureAuditedEntities(builder);
    }

    /// <summary>
    /// Maps created and last-modified (who and when) for every entity that derives from <see cref="AuditedEntity"/>, so a new
    /// entity gets the columns without any mapping of its own. The times default to the database clock for rows added by SQL.
    /// </summary>
    private void ConfigureAuditedEntities(ModelBuilder builder)
    {
        var now = Database.IsNpgsql() ? "now()" : "CURRENT_TIMESTAMP";
        var audited = builder.Model.GetEntityTypes()
            .Where(t => !t.IsOwned() && typeof(AuditedEntity).IsAssignableFrom(t.ClrType))
            .Select(t => t.ClrType)
            .ToList();
        foreach (var type in audited)
        {
            builder.Entity(type, e =>
            {
                e.Property(nameof(AuditedEntity.CreatedAtUtc)).HasConversion(UtcTimes).HasDefaultValueSql(now).IsRequired();
                e.Property(nameof(AuditedEntity.CreatedByUserId)).HasMaxLength(AuditedEntity.MaxUserIdLength);
                e.Property(nameof(AuditedEntity.LastModifiedAtUtc)).HasConversion(UtcTimes).HasDefaultValueSql(now).IsRequired();
                e.Property(nameof(AuditedEntity.LastModifiedByUserId)).HasMaxLength(AuditedEntity.MaxUserIdLength);
            });
        }
    }

    /// <summary>
    /// Reference data for the Admin Configuration feature. Uniqueness is on the normalized comparison values and
    /// every relationship is restrictive, so the database is the last guard against duplicates and against
    /// deleting a record that something still refers to.
    /// </summary>
    private static void ConfigureConfiguration(ModelBuilder builder)
    {
        // Independent catalogs: one globally unique normalized name each.
        MapRecord<EducationalInstitution>(builder, "EducationalInstitutions", unique: true).Entity<EducationalInstitution>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(16).IsRequired();
            e.Property(x => x.Address).HasMaxLength(NameNormalizer.MaxAddressLength);
        });
        MapCode<EducationalInstitution>(builder);
        MapRecord<EducationBoard>(builder, "EducationBoards", unique: true);
        MapCode<EducationBoard>(builder);
        MapRecord<EducationGroup>(builder, "EducationGroups", unique: true);
        MapRecord<ExaminationType>(builder, "ExaminationTypes", unique: true);
        MapRecord<Degree>(builder, "Degrees", unique: true).Entity<Degree>(e =>
        {
            e.Property(x => x.CreditsToBeCompleted).HasPrecision(8, 2).IsRequired();
            e.ToTable(t => t.HasCheckConstraint("CK_Degrees_CreditsToBeCompleted_Positive", "\"CreditsToBeCompleted\" > 0"));
        });
        MapCode<Degree>(builder);
        MapRecord<Semester>(builder, "Semesters", unique: true);
        MapRecord<AcademicSession>(builder, "AcademicSessions", unique: true);
        MapRecord<Designation>(builder, "Designations", unique: true);
        MapCode<Designation>(builder);
        MapRecord<Hall>(builder, "Halls", unique: true).Entity<Hall>(e => e.Property(x => x.Address).HasMaxLength(NameNormalizer.MaxAddressLength));
        MapCode<Hall>(builder);

        // A Batch name is unique within its Degree only; Session does not change that scope.
        MapRecord<Batch>(builder, "Batches", unique: false).Entity<Batch>(e =>
        {
            e.HasIndex(x => new { x.DegreeId, x.NormalizedName }).IsUnique().HasFilter(LiveOnly);
            e.HasOne<Degree>().WithMany().HasForeignKey(x => x.DegreeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Semester>().WithMany().HasForeignKey(x => x.StartingSemesterId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AcademicSession>().WithMany().HasForeignKey(x => x.AcademicSessionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.StartingSemesterId);
            e.HasIndex(x => x.AcademicSessionId);
        });

        // Division names are global; District and Upazila names are unique within their parent.
        MapRecord<Division>(builder, "Divisions", unique: true);
        MapRecord<District>(builder, "Districts", unique: false).Entity<District>(e =>
        {
            e.HasIndex(x => new { x.DivisionId, x.NormalizedName }).IsUnique().HasFilter(LiveOnly);
            e.HasOne<Division>().WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        });
        MapRecord<Upazila>(builder, "Upazilas", unique: false).Entity<Upazila>(e =>
        {
            e.HasIndex(x => new { x.DistrictId, x.NormalizedName }).IsUnique().HasFilter(LiveOnly);
            e.HasOne<District>().WithMany().HasForeignKey(x => x.DistrictId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    /// <summary>
    /// Deleted records stay in their table but drop out of the unique indexes (they apply only to live rows), so a deleted
    /// record's name and code can be used again. A global query filter hides deleted rows from every query.
    /// </summary>
    private const string LiveOnly = "\"IsDeleted\" = false";

    private static readonly ValueConverter<DateTime, DateTime> UtcTimes = new(
        value => value.ToUniversalTime(),
        value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static ModelBuilder MapRecord<T>(ModelBuilder builder, string table, bool unique) where T : ConfigurationRecord
    {
        builder.Entity<T>(e =>
        {
            e.ToTable(table);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(NameNormalizer.MaxNameLength).IsRequired();
            e.Property(x => x.NormalizedName).HasMaxLength(NameNormalizer.MaxNameLength).IsRequired();
            e.Property(x => x.IsActive).IsRequired();
            e.Property(x => x.IsDeleted).IsRequired().HasDefaultValue(false);
            e.Property(x => x.DeletedAtUtc).HasConversion(UtcTimes);
            e.Property(x => x.DeletedByUserId).HasMaxLength(450);
            e.HasQueryFilter(x => !x.IsDeleted);
            var names = e.HasIndex(x => x.NormalizedName);
            if (unique)
            {
                names.IsUnique().HasFilter(LiveOnly);
            }
        });
        return builder;
    }

    /// <summary>An optional code, unique within its own catalog when present (NULLs never collide).</summary>
    private static void MapCode<T>(ModelBuilder builder) where T : ConfigurationRecord, ICodedRecord
    {
        builder.Entity<T>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(NameNormalizer.MaxCodeLength);
            e.Property(x => x.NormalizedCode).HasMaxLength(NameNormalizer.MaxCodeLength);
            e.HasIndex(x => x.NormalizedCode).IsUnique().HasFilter(LiveOnly);
        });
    }
}
