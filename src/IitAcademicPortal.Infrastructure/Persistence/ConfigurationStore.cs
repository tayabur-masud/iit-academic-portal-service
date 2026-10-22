using System.Linq.Expressions;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// Configuration records over the request's <see cref="PortalDbContext"/>. Staged audit events live in the same
/// context, so <see cref="SaveAsync"/> commits a change and its event in one transaction.
/// </summary>
public sealed class ConfigurationStore(PortalDbContext db) : IConfigurationStore
{
    private const string UniqueViolation = "23505";

    // SQLite extended result codes, used by the test host: UNIQUE and PRIMARYKEY.
    private const int SqliteUnique = 2067;
    private const int SqlitePrimaryKey = 1555;

    public Task<T?> FindAsync<T>(Guid id, CancellationToken cancellationToken) where T : ConfigurationRecord =>
        db.Set<T>().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync<T>(Expression<Func<T, bool>> scope, string normalizedName, Guid exceptId, CancellationToken cancellationToken)
        where T : ConfigurationRecord =>
        db.Set<T>().Where(scope).AnyAsync(e => e.NormalizedName == normalizedName && e.Id != exceptId, cancellationToken);

    public Task<bool> CodeExistsAsync<T>(string normalizedCode, Guid exceptId, CancellationToken cancellationToken)
        where T : ConfigurationRecord, ICodedRecord =>
        db.Set<T>().AnyAsync(e => EF.Property<string?>(e, nameof(ICodedRecord.NormalizedCode)) == normalizedCode && e.Id != exceptId, cancellationToken);

    public async Task<IReadOnlyList<T>> ListAsync<T>(
        Expression<Func<T, bool>>? filter,
        string? normalizedSearch,
        ConfigurationStatus status,
        string? afterName,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken,
        int skip = 0,
        string? sortProperty = null) where T : ConfigurationRecord
    {
        var query = Matching(filter, normalizedSearch, status);
        var sort = sortProperty ?? nameof(ConfigurationRecord.NormalizedName);

        // Keyset continuation: strictly after the last row of the previous page in (sort key, id) order.
        if (afterName is not null && afterId is { } lastId)
        {
            query = query.Where(e => string.Compare(EF.Property<string>(e, sort), afterName) > 0
                || (EF.Property<string>(e, sort) == afterName && e.Id.CompareTo(lastId) > 0));
        }

        // Numbered pages skip whole pages instead; the order is the same, so both modes agree on what comes first.
        return await query.OrderBy(e => EF.Property<string>(e, sort)).ThenBy(e => e.Id).Skip(skip).Take(take).ToListAsync(cancellationToken);
    }

    public Task<long> CountMatchingAsync<T>(
        Expression<Func<T, bool>>? filter, string? normalizedSearch, ConfigurationStatus status, CancellationToken cancellationToken)
        where T : ConfigurationRecord =>
        Matching(filter, normalizedSearch, status).LongCountAsync(cancellationToken);

    /// <summary>The records a list shows before paging: the parent filter, the status, and the search.</summary>
    private IQueryable<T> Matching<T>(Expression<Func<T, bool>>? filter, string? normalizedSearch, ConfigurationStatus status)
        where T : ConfigurationRecord
    {
        var query = db.Set<T>().AsNoTracking();

        if (filter is not null)
        {
            query = query.Where(filter);
        }

        query = status switch
        {
            ConfigurationStatus.Active => query.Where(e => e.IsActive),
            ConfigurationStatus.Inactive => query.Where(e => !e.IsActive),
            _ => query,
        };

        if (normalizedSearch is not null)
        {
            query = typeof(ICodedRecord).IsAssignableFrom(typeof(T))
                ? query.Where(e => e.NormalizedName.Contains(normalizedSearch)
                    || (EF.Property<string?>(e, nameof(ICodedRecord.NormalizedCode)) ?? string.Empty).Contains(normalizedSearch))
                : query.Where(e => e.NormalizedName.Contains(normalizedSearch));
        }

        return query;
    }

    public Task<long> CountAsync<T>(CancellationToken cancellationToken) where T : ConfigurationRecord =>
        db.Set<T>().LongCountAsync(cancellationToken);

    public void Add<T>(T entity) where T : ConfigurationRecord => db.Set<T>().Add(entity);

    public void AddRange<T>(IEnumerable<T> entities) where T : ConfigurationRecord => db.Set<T>().AddRange(entities);

    public Task<bool> ExistsAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) where T : ConfigurationRecord =>
        db.Set<T>().AnyAsync(predicate, cancellationToken);

    public async Task<bool> DesignationInUseAsync(Guid designationId, CancellationToken cancellationToken) =>
        await db.TeacherProfiles.AnyAsync(t => t.DesignationId == designationId, cancellationToken)
        || await db.StaffProfiles.AnyAsync(s => s.DesignationId == designationId, cancellationToken);

    public Task<Guid?> PrerequisiteOfAsync(Guid courseId, CancellationToken cancellationToken) =>
        db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => c.PrerequisiteCourseId).FirstOrDefaultAsync(cancellationToken);

    public Task<bool> HasActiveDependentCoursesAsync(Guid courseId, Guid? outsideDegreeId, CancellationToken cancellationToken) =>
        db.Courses.AnyAsync(c => c.PrerequisiteCourseId == courseId && c.IsActive && c.Id != courseId
            && (outsideDegreeId == null || c.DegreeId != outsideDegreeId), cancellationToken);

    public Task<bool> UsedByStudentsAsync<T>(Guid id, CancellationToken cancellationToken) where T : ConfigurationRecord =>
        typeof(T) == typeof(Degree) ? db.StudentAdmissions.AnyAsync(a => a.DegreeId == id, cancellationToken)
        : typeof(T) == typeof(Batch) ? db.StudentAdmissions.AnyAsync(a => a.BatchId == id, cancellationToken)
        : typeof(T) == typeof(Hall) ? db.StudentAdmissions.AnyAsync(a => a.AssignedHallId == id, cancellationToken)
        : typeof(T) == typeof(Upazila) ? db.StudentProfiles.AnyAsync(p => p.MailingUpazilaId == id || p.PermanentUpazilaId == id, cancellationToken)
        : typeof(T) == typeof(CityCorporation)
            ? db.StudentProfiles.AnyAsync(p => p.MailingCityCorporationId == id || p.PermanentCityCorporationId == id, cancellationToken)
        : typeof(T) == typeof(EducationalInstitution)
            ? db.StudentAcademicHistoryEntries.AnyAsync(h => h.SchoolOrCollegeId == id || h.UniversityId == id || h.InstitutionId == id, cancellationToken)
        : typeof(T) == typeof(EducationBoard) ? db.StudentAcademicHistoryEntries.AnyAsync(h => h.BoardId == id, cancellationToken)
        : typeof(T) == typeof(EducationGroup) ? db.StudentAcademicHistoryEntries.AnyAsync(h => h.GroupId == id, cancellationToken)
        : Task.FromResult(false);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (Classify(ex) is { } kind)
        {
            throw new ConfigurationConflictException(kind, ex);
        }
    }

    /// <summary>Recognises a unique violation from PostgreSQL, or from SQLite in the test host.</summary>
    internal static ConfigurationConflictKind? Classify(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolation ? ConfigurationConflictKind.Duplicate : null;
            }

            // The SQLite provider is referenced only by the tests, so its error code is read by name.
            if (inner.GetType().Name == "SqliteException")
            {
                var extended = inner.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(inner) as int?;
                return extended is SqliteUnique or SqlitePrimaryKey || inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                    ? ConfigurationConflictKind.Duplicate
                    : null;
            }
        }

        return null;
    }
}
