using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Teachers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// Teacher profiles over the request's <see cref="PortalDbContext"/>. Identity, the profile, session revocation, and the staged
/// audit event all share this context, so one transaction around them makes a change atomic.
/// </summary>
public sealed class TeacherStore(PortalDbContext db) : ITeacherStore
{
    private const string UniqueViolation = "23505";

    // Arbitrary, fixed key for the PostgreSQL advisory lock that serializes the last-Admin check.
    private const long AdminInvariantLockKey = 4_004_001;

    public async Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task LockAdminInvariantAsync(CancellationToken cancellationToken)
    {
        // SQLite (the test host) has a single writer, so only PostgreSQL needs the lock. It is held until the transaction ends.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({AdminInvariantLockKey})", cancellationToken);
        }
    }

    public Task<TeacherProfile?> FindProfileAsync(Guid teacherId, CancellationToken cancellationToken) =>
        db.TeacherProfiles.FirstOrDefaultAsync(p => p.Id == teacherId, cancellationToken);

    public Task<TeacherProfile?> FindProfileByUserAsync(string userId, CancellationToken cancellationToken) =>
        db.TeacherProfiles.FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public Task<bool> StaffIdExistsAsync(string normalizedStaffId, Guid exceptProfileId, CancellationToken cancellationToken) =>
        db.TeacherProfiles.AnyAsync(p => p.NormalizedEmployeeStaffId == normalizedStaffId && p.Id != exceptProfileId, cancellationToken);

    // The Designation query filter already hides deleted records.
    public Task<bool> DesignationAvailableAsync(Guid designationId, CancellationToken cancellationToken) =>
        db.Designations.AnyAsync(d => d.Id == designationId && d.IsActive, cancellationToken);

    public Task<bool> HasOtherActiveAdminAsync(string exceptUserId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id != exceptUserId
            && u.AccountStatus == TeacherAccountStatus.Active
            && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PortalRoles.Admin)),
            cancellationToken);

    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken) =>
        await db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
            .ToListAsync(cancellationToken);

    public async Task StageRoleAsync(string userId, string roleName, bool assigned, CancellationToken cancellationToken)
    {
        var roleId = await db.Roles.Where(r => r.Name == roleName).Select(r => r.Id).SingleAsync(cancellationToken);
        var existing = await db.UserRoles.FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, cancellationToken);
        if (assigned && existing is null)
        {
            db.UserRoles.Add(new IdentityUserRole<string> { UserId = userId, RoleId = roleId });
        }
        else if (!assigned && existing is not null)
        {
            db.UserRoles.Remove(existing);
        }
    }

    public void Add(TeacherProfile profile) => db.TeacherProfiles.Add(profile);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Classify(ex) is { } kind)
        {
            throw new TeacherConflictException(kind, ex);
        }
    }

    public async Task<TeacherDetail?> GetDetailAsync(Guid teacherId, CancellationToken cancellationToken)
    {
        var row = await Rows(db.TeacherProfiles.AsNoTracking().Where(p => p.Id == teacherId)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDetail(row);
    }

    public async Task<TeacherListPage> ListAsync(TeacherListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.TeacherProfiles.AsNoTracking().AsQueryable();
        if (filter.Status is { } status)
        {
            query = query.Where(p => db.Users.Any(u => u.Id == p.UserId && u.AccountStatus == status));
        }

        if (filter.NormalizedSearch is { } search)
        {
            query = query.Where(p =>
                p.DisplayName.ToUpper().Contains(search)
                || (p.NormalizedEmployeeStaffId != null && p.NormalizedEmployeeStaffId.Contains(search))
                || db.Users.Any(u => u.Id == p.UserId && u.NormalizedEmail != null && u.NormalizedEmail.Contains(search)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await Rows(query.OrderBy(p => p.DisplayName.ToUpper()).ThenBy(p => p.Id).Skip(skip).Take(pageSize)).ToListAsync(cancellationToken);
        var totalPages = (int)Math.Min((total + pageSize - 1) / pageSize, int.MaxValue);
        return new TeacherListPage(rows.Select(ToDetail).ToList(), page, total, totalPages);
    }

    // One round trip for a page: the profile, its account, designation name, and assigned roles. Hashes and tokens are never selected.
    private IQueryable<Row> Rows(IQueryable<TeacherProfile> profiles) =>
        profiles.Select(p => new Row(
            p,
            db.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault(),
            db.Users.Where(u => u.Id == p.UserId).Select(u => u.AccountStatus).FirstOrDefault(),
            db.Users.Where(u => u.Id == p.UserId).Select(u => u.DefaultRole).FirstOrDefault(),
            db.Designations.Where(d => d.Id == p.DesignationId).Select(d => d.Name).FirstOrDefault(),
            db.UserRoles.Where(ur => ur.UserId == p.UserId).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToList()));

    private static TeacherDetail ToDetail(Row row) => new(
        row.Profile.Id, row.Profile.UserId, row.Email ?? string.Empty, row.Profile.DisplayName, row.Profile.EmployeeStaffId,
        row.Profile.Phone, row.Profile.DesignationId, row.DesignationName, row.Profile.IsActive, row.Status,
        PortalRoles.Normalize(row.Roles), row.DefaultRole,
        row.Profile.CreatedAtUtc, row.Profile.CreatedByUserId, null, row.Profile.LastModifiedAtUtc, row.Profile.LastModifiedByUserId, null);

    private sealed record Row(TeacherProfile Profile, string? Email, TeacherAccountStatus Status, string? DefaultRole, string? DesignationName, List<string> Roles);

    /// <summary>Recognises a unique violation from PostgreSQL, or from SQLite in the test host, and which rule it was.</summary>
    private static TeacherConflictKind? Classify(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolation ? Kind(postgres.ConstraintName ?? postgres.MessageText) : null;
            }

            // The SQLite provider is referenced only by the tests, so it is recognised by name.
            if (inner.GetType().Name == "SqliteException")
            {
                return inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ? Kind(inner.Message) : null;
            }
        }

        return null;
    }

    private static TeacherConflictKind Kind(string text) =>
        text.Contains("NormalizedEmployeeStaffId", StringComparison.Ordinal) ? TeacherConflictKind.StaffId
        : text.Contains("EmailIndex", StringComparison.Ordinal) || text.Contains("NormalizedEmail", StringComparison.Ordinal)
            || text.Contains("NormalizedUserName", StringComparison.Ordinal) || text.Contains("UserNameIndex", StringComparison.Ordinal)
            ? TeacherConflictKind.Email
            : TeacherConflictKind.Other;

    private sealed class Transaction(IDbContextTransaction inner) : ITeacherTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
