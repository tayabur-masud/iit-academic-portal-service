using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.MyProfile;
using IitAcademicPortal.Application.Staff;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Staff;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// Staff profiles over the request's <see cref="PortalDbContext"/>. Identity, the profile, session revocation, and the staged audit events
/// share this context, so one transaction around them makes a change atomic.
/// </summary>
public sealed class StaffStore(PortalDbContext db) : IStaffStore
{
    private const string UniqueViolation = "23505";

    public async Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task LockAdminInvariantAsync(CancellationToken cancellationToken)
    {
        // The same key as Teacher and User Management, so every change that could remove the last active Admin waits for the others.
        // SQLite (the test host) has a single writer, so only PostgreSQL needs the lock. It is held until the transaction ends.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({TeacherStore.AdminInvariantLockKey})", cancellationToken);
        }
    }

    public Task<bool> HasOtherActiveAdminAsync(string exceptUserId, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(u => u.Id != exceptUserId
            && u.AccountStatus == TeacherAccountStatus.Active
            && db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PortalRoles.Admin)),
            cancellationToken);

    public async Task<StaffAccount?> FindAccountAsync(string userId, CancellationToken cancellationToken)
    {
        var account = await db.Users.AsNoTracking().Where(u => u.Id == userId)
            .Select(u => new
            {
                u.Id,
                u.AccountStatus,
                IsAdmin = db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PortalRoles.Admin)),
            })
            .FirstOrDefaultAsync(cancellationToken);
        return account is null ? null : new StaffAccount(account.Id, account.AccountStatus, account.IsAdmin);
    }

    public Task<bool> ProfileExistsForUserAsync(string userId, CancellationToken cancellationToken) =>
        db.StaffProfiles.AnyAsync(p => p.UserId == userId, cancellationToken);

    public Task<StaffProfile?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken) =>
        db.StaffProfiles.FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);

    // The Designation query filter already hides deleted records.
    public Task<bool> DesignationAvailableAsync(Guid designationId, CancellationToken cancellationToken) =>
        db.Designations.AnyAsync(d => d.Id == designationId && d.IsActive, cancellationToken);

    public void Add(StaffProfile profile) => db.StaffProfiles.Add(profile);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicate(ex))
        {
            throw new StaffConflictException(ex);
        }
    }

    public async Task<StaffDetail?> GetDetailAsync(Guid profileId, CancellationToken cancellationToken)
    {
        var row = await Rows(db.StaffProfiles.AsNoTracking().Where(p => p.Id == profileId)).FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToDetail(row);
    }

    // The Designation's current name, even if it was deleted since, as on the staff screens.
    public Task<MyStaffProfile?> GetOwnProfileAsync(string userId, CancellationToken cancellationToken) =>
        db.StaffProfiles.AsNoTracking().Where(p => p.UserId == userId)
            .Select(p => new MyStaffProfile(
                p.Name, p.MobileNumber, db.Designations.IgnoreQueryFilters().Where(d => d.Id == p.DesignationId).Select(d => d.Name).FirstOrDefault()))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<StaffListPage> ListAsync(string? normalizedSearch, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.StaffProfiles.AsNoTracking().AsQueryable();
        if (normalizedSearch is { } search)
        {
            query = query.Where(p =>
                p.Name.ToUpper().Contains(search)
                || p.MobileNumber.ToUpper().Contains(search)
                || db.Users.Any(u => u.Id == p.UserId && u.NormalizedEmail != null && u.NormalizedEmail.Contains(search))
                || db.Designations.IgnoreQueryFilters().Any(d => d.Id == p.DesignationId && d.NormalizedName.Contains(search)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await Rows(query.OrderBy(p => p.Name.ToUpper()).ThenBy(p => p.Id).Skip(skip).Take(pageSize)).ToListAsync(cancellationToken);
        var totalPages = (int)Math.Min((total + pageSize - 1) / pageSize, int.MaxValue);
        return new StaffListPage(rows.Select(ToDetail).ToList(), page, total, totalPages);
    }

    // One round trip for a page: the profile, its account's email, status and Admin role, and the Designation's current name (even if
    // it was deleted since, so the profile still reads). Hashes and tokens are never selected.
    private IQueryable<Row> Rows(IQueryable<StaffProfile> profiles) =>
        profiles.Select(p => new Row(
            p,
            db.Users.Where(u => u.Id == p.UserId).Select(u => u.Email).FirstOrDefault(),
            db.Users.Where(u => u.Id == p.UserId).Select(u => u.AccountStatus).FirstOrDefault(),
            db.UserRoles.Any(ur => ur.UserId == p.UserId && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PortalRoles.Admin)),
            db.Designations.IgnoreQueryFilters().Where(d => d.Id == p.DesignationId).Select(d => d.Name).FirstOrDefault()));

    private static StaffDetail ToDetail(Row row) => new(
        row.Profile.Id, row.Profile.UserId, row.Email ?? string.Empty, row.Status, row.IsAdmin, row.Profile.Name, row.Profile.MobileNumber,
        row.Profile.DesignationId, row.DesignationName, row.Profile.IsActive, row.Profile.CreatedAtUtc, row.Profile.CreatedByUserId, null,
        row.Profile.LastModifiedAtUtc, row.Profile.LastModifiedByUserId, null);

    private sealed record Row(StaffProfile Profile, string? Email, TeacherAccountStatus Status, bool IsAdmin, string? DesignationName);

    /// <summary>A unique violation from PostgreSQL, or from SQLite in the test host: a second profile or account that raced past the checks.</summary>
    private static bool IsDuplicate(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolation;
            }

            // The SQLite provider is referenced only by the tests, so it is recognised by name.
            if (inner.GetType().Name == "SqliteException")
            {
                return inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private sealed class Transaction(IDbContextTransaction inner) : ITeacherTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
