using System.Linq.Expressions;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Persistence for configuration records. Reads and writes share the request's unit of work, so a staged audit
/// event is saved by <see cref="SaveAsync"/> in the same transaction as the change. Deleted records are invisible to every
/// method here: a record is deleted by marking it, never by removing the row.
/// </summary>
public interface IConfigurationStore
{
    Task<T?> FindAsync<T>(Guid id, CancellationToken cancellationToken) where T : ConfigurationRecord;

    /// <summary>True when another record in <paramref name="scope"/> already has this normalized name.</summary>
    Task<bool> NameExistsAsync<T>(Expression<Func<T, bool>> scope, string normalizedName, Guid exceptId, CancellationToken cancellationToken)
        where T : ConfigurationRecord;

    /// <summary>True when another record of the same catalog already has this normalized code.</summary>
    Task<bool> CodeExistsAsync<T>(string normalizedCode, Guid exceptId, CancellationToken cancellationToken)
        where T : ConfigurationRecord, ICodedRecord;

    /// <summary>
    /// Records ordered by normalized name then identifier, starting after the given position. The search matches
    /// normalized names, and normalized codes where the catalog has them.
    /// </summary>
    Task<IReadOnlyList<T>> ListAsync<T>(
        Expression<Func<T, bool>>? filter,
        string? normalizedSearch,
        ConfigurationStatus status,
        string? afterName,
        Guid? afterId,
        int take,
        CancellationToken cancellationToken,
        int skip = 0) where T : ConfigurationRecord;

    /// <summary>How many records match the same filter, search, and status a list uses.</summary>
    Task<long> CountMatchingAsync<T>(
        Expression<Func<T, bool>>? filter,
        string? normalizedSearch,
        ConfigurationStatus status,
        CancellationToken cancellationToken) where T : ConfigurationRecord;

    Task<long> CountAsync<T>(CancellationToken cancellationToken) where T : ConfigurationRecord;

    void Add<T>(T entity) where T : ConfigurationRecord;

    void AddRange<T>(IEnumerable<T> entities) where T : ConfigurationRecord;

    /// <summary>True when any live (not deleted) record of this kind matches. Deleted records are never matched.</summary>
    Task<bool> ExistsAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken cancellationToken) where T : ConfigurationRecord;

    /// <summary>True when any teacher profile still uses this Designation, which blocks deleting it (deactivate it instead).</summary>
    Task<bool> DesignationInUseByTeacherAsync(Guid designationId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the pending changes and any staged audit event together.
    /// </summary>
    /// <exception cref="ConfigurationConflictException">A uniqueness rule in the database rejected the change.</exception>
    Task SaveAsync(CancellationToken cancellationToken);
}
