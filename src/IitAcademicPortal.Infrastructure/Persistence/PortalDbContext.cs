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

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Email is the sign-in identifier, so a normalized email must resolve to exactly one account.
        builder.Entity<PortalUser>().HasIndex(u => u.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique();

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
    }
}
