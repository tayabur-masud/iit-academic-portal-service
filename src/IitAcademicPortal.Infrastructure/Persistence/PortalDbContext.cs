using IitAcademicPortal.Domain;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using IitAcademicPortal.Domain.Students;
using IitAcademicPortal.Domain.Teachers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace IitAcademicPortal.Infrastructure.Persistence;

public partial class PortalDbContext(DbContextOptions<PortalDbContext> options) : IdentityDbContext<PortalUser>(options)
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

    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();

    public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();

    public DbSet<StudentAdmissionRecord> StudentAdmissions => Set<StudentAdmissionRecord>();

    public DbSet<StudentAcademicHistory> StudentAcademicHistoryEntries => Set<StudentAcademicHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Email is the sign-in identifier, so a normalized email must resolve to exactly one account.
        builder.Entity<PortalUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();
        builder.Entity<PortalUser>().Property(u => u.DefaultRole).HasMaxLength(32);
        // Existing accounts migrate as Active; only Teacher Management creates accounts in another state.
        builder.Entity<PortalUser>().Property(u => u.AccountStatus).HasConversion<string>().HasMaxLength(32)
            .HasDefaultValue(TeacherAccountStatus.Active).IsRequired();

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
        ConfigureTeachers(builder);
        ConfigureStudents(builder);
        ConfigureNotifications(builder);
        ConfigureAuditedEntities(builder);
    }

    /// <summary>
    /// Teacher profiles: one per account (unique, restrictive link, so an account with a profile cannot be removed), a staff ID
    /// that is unique across every profile once normalized, and a restrictive optional designation.
    /// </summary>
    private static void ConfigureTeachers(ModelBuilder builder) =>
        builder.Entity<TeacherProfile>(profile =>
        {
            profile.ToTable("TeacherProfiles");
            profile.HasKey(p => p.Id);
            profile.Property(p => p.Id).ValueGeneratedNever();
            profile.Property(p => p.UserId).HasMaxLength(AuditedEntity.MaxUserIdLength).IsRequired();
            profile.Property(p => p.DisplayName).HasMaxLength(NameNormalizer.MaxNameLength).IsRequired();
            profile.Property(p => p.EmployeeStaffId).HasMaxLength(NameNormalizer.MaxCodeLength);
            profile.Property(p => p.NormalizedEmployeeStaffId).HasMaxLength(NameNormalizer.MaxCodeLength);
            profile.Property(p => p.Phone).HasMaxLength(TeacherProfile.MaxPhoneLength);
            profile.HasIndex(p => p.UserId).IsUnique();
            profile.HasIndex(p => p.NormalizedEmployeeStaffId).IsUnique().HasFilter("\"NormalizedEmployeeStaffId\" IS NOT NULL");
            profile.HasIndex(p => p.DesignationId);
            profile.HasOne<PortalUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            profile.HasOne<Designation>().WithMany().HasForeignKey(p => p.DesignationId).OnDelete(DeleteBehavior.Restrict);
        });

    /// <summary>
    /// Student Management: one profile per account (unique, restrictive), many admissions per profile with at most one per Degree and
    /// Batch, a registration number unique across all admissions once normalized, and restrictive links to the Configuration records
    /// they use. Academic history rows belong to their profile and keep every source value as text (no catalog foreign keys).
    /// </summary>
    private static void ConfigureStudents(ModelBuilder builder)
    {
        builder.Entity<StudentProfile>(profile =>
        {
            profile.ToTable("StudentProfiles");
            profile.HasKey(p => p.Id);
            profile.Property(p => p.Id).ValueGeneratedNever();
            profile.Property(p => p.UserId).HasMaxLength(AuditedEntity.MaxUserIdLength).IsRequired();
            profile.Property(p => p.ApplicantName).HasMaxLength(StudentProfile.MaxTextLength).IsRequired();
            profile.Property(p => p.FatherName).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.MotherName).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.PlaceOfBirth).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.MaritalStatus).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.Nationality).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.ApplicantMobile).HasMaxLength(StudentProfile.MaxPhoneLength).IsRequired();
            profile.Property(p => p.MailingAddressDetails).HasMaxLength(StudentProfile.MaxAddressLength);
            profile.Property(p => p.MailingCellNumber).HasMaxLength(StudentProfile.MaxPhoneLength);
            profile.Property(p => p.PermanentAddressDetails).HasMaxLength(StudentProfile.MaxAddressLength);
            profile.Property(p => p.PermanentCellNumber).HasMaxLength(StudentProfile.MaxPhoneLength);
            profile.Property(p => p.GuardianName).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.GuardianRelationship).HasMaxLength(StudentProfile.MaxTextLength);
            profile.Property(p => p.GuardianAddress).HasMaxLength(StudentProfile.MaxAddressLength);
            profile.Property(p => p.GuardianCellNumber).HasMaxLength(StudentProfile.MaxPhoneLength);
            profile.HasIndex(p => p.UserId).IsUnique();
            profile.HasIndex(p => p.MailingUpazilaId);
            profile.HasIndex(p => p.PermanentUpazilaId);
            profile.HasOne<PortalUser>().WithMany().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Restrict);
            profile.HasOne<Upazila>().WithMany().HasForeignKey(p => p.MailingUpazilaId).OnDelete(DeleteBehavior.Restrict);
            profile.HasOne<Upazila>().WithMany().HasForeignKey(p => p.PermanentUpazilaId).OnDelete(DeleteBehavior.Restrict);
            profile.HasMany(p => p.AcademicHistory).WithOne().HasForeignKey(h => h.StudentProfileId).OnDelete(DeleteBehavior.Cascade);
            profile.Navigation(p => p.AcademicHistory).UsePropertyAccessMode(PropertyAccessMode.Property);
        });

        builder.Entity<StudentAcademicHistory>(history =>
        {
            history.ToTable("StudentAcademicHistory");
            history.HasKey(h => h.Id);
            history.Property(h => h.Id).ValueGeneratedNever();
            history.Property(h => h.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            foreach (var name in new[]
            {
                nameof(StudentAcademicHistory.Examination), nameof(StudentAcademicHistory.SchoolOrCollege), nameof(StudentAcademicHistory.Board),
                nameof(StudentAcademicHistory.Group), nameof(StudentAcademicHistory.Department), nameof(StudentAcademicHistory.University),
                nameof(StudentAcademicHistory.Subject), nameof(StudentAcademicHistory.Institution), nameof(StudentAcademicHistory.Year),
                nameof(StudentAcademicHistory.MarksObtained), nameof(StudentAcademicHistory.MarksPercentage), nameof(StudentAcademicHistory.Cgpa),
                nameof(StudentAcademicHistory.Class), nameof(StudentAcademicHistory.Division),
            })
            {
                history.Property<string?>(name).HasMaxLength(StudentAcademicHistory.MaxValueLength);
            }

            history.HasIndex(h => new { h.StudentProfileId, h.SortOrder });
        });

        builder.Entity<StudentAdmissionRecord>(admission =>
        {
            admission.ToTable("StudentAdmissions");
            admission.HasKey(a => a.Id);
            admission.Property(a => a.Id).ValueGeneratedNever();
            admission.Property(a => a.AdmissionSerialNumber).HasMaxLength(StudentAdmissionRecord.MaxIdentifierLength).IsRequired();
            admission.Property(a => a.AdmissionExamRoll).HasMaxLength(StudentAdmissionRecord.MaxIdentifierLength).IsRequired();
            admission.Property(a => a.ClassRoll).HasMaxLength(StudentAdmissionRecord.MaxIdentifierLength);
            admission.Property(a => a.RegistrationNumber).HasMaxLength(StudentAdmissionRecord.MaxIdentifierLength);
            admission.Property(a => a.NormalizedRegistrationNumber).HasMaxLength(StudentAdmissionRecord.MaxIdentifierLength);
            admission.HasIndex(a => new { a.StudentProfileId, a.DegreeId, a.BatchId }).IsUnique();
            admission.HasIndex(a => a.NormalizedRegistrationNumber).IsUnique().HasFilter("\"NormalizedRegistrationNumber\" IS NOT NULL");
            admission.HasIndex(a => a.DegreeId);
            admission.HasIndex(a => a.BatchId);
            admission.HasIndex(a => a.AssignedHallId);
            admission.HasOne<StudentProfile>().WithMany().HasForeignKey(a => a.StudentProfileId).OnDelete(DeleteBehavior.Restrict);
            admission.HasOne<Degree>().WithMany().HasForeignKey(a => a.DegreeId).OnDelete(DeleteBehavior.Restrict);
            admission.HasOne<Batch>().WithMany().HasForeignKey(a => a.BatchId).OnDelete(DeleteBehavior.Restrict);
            admission.HasOne<Hall>().WithMany().HasForeignKey(a => a.AssignedHallId).OnDelete(DeleteBehavior.Restrict);
        });
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
