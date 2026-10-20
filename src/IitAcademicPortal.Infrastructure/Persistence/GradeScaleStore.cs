using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>The Grade scale over the request's <see cref="PortalDbContext"/>, so a replacement and its audit events share one transaction.</summary>
public sealed class GradeScaleStore(PortalDbContext db) : IGradeScaleStore
{
    // Arbitrary, fixed key for the PostgreSQL advisory lock that serializes Grade scale replacements.
    private const long GradeScaleLockKey = 3_003_014;

    public async Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        new Transaction(await db.Database.BeginTransactionAsync(cancellationToken));

    public async Task LockScaleAsync(CancellationToken cancellationToken)
    {
        // SQLite (the test host) has a single writer, so only PostgreSQL needs the lock. It is held until the transaction ends.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({GradeScaleLockKey})", cancellationToken);
        }
    }

    public Task<List<Grade>> LoadLiveForChangeAsync(CancellationToken cancellationToken) => db.Grades.ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Grade>> ListAsync(string? normalizedSearch, ConfigurationStatus status, CancellationToken cancellationToken)
    {
        var query = db.Grades.AsNoTracking();
        query = status switch
        {
            ConfigurationStatus.Active => query.Where(g => g.IsActive),
            ConfigurationStatus.Inactive => query.Where(g => !g.IsActive),
            _ => query,
        };
        if (normalizedSearch is not null)
        {
            query = query.Where(g => g.NormalizedName.Contains(normalizedSearch));
        }

        // A scale has a handful of bands, and SQLite cannot order by a decimal, so they are ordered here.
        var grades = await query.ToListAsync(cancellationToken);
        return grades.OrderByDescending(g => g.MarksLowerBound).ThenBy(g => g.NormalizedName, StringComparer.Ordinal).ToList();
    }

    public Task<Grade?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Grades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id, cancellationToken);

    public void Add(Grade grade) => db.Grades.Add(grade);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ConfigurationStore.Classify(ex) is { } kind)
        {
            throw new ConfigurationConflictException(kind, ex);
        }
    }

    private sealed class Transaction(IDbContextTransaction inner) : ITeacherTransaction
    {
        public Task CommitAsync(CancellationToken cancellationToken) => inner.CommitAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
