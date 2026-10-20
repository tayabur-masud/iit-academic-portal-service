using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Persistence for the global Grade scale. A replacement runs in one <see cref="BeginTransactionAsync"/> transaction: it takes
/// <see cref="LockScaleAsync"/> first, so concurrent replacements run one after another, then reads, changes, and saves the bands
/// and their staged audit events together. Deleted bands are invisible to every method here.
/// </summary>
public interface IGradeScaleStore
{
    Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>Serializes Grade scale replacements until the current transaction ends (a PostgreSQL advisory lock).</summary>
    Task LockScaleAsync(CancellationToken cancellationToken);

    /// <summary>Every live band, tracked so that changes to them are saved.</summary>
    Task<List<Grade>> LoadLiveForChangeAsync(CancellationToken cancellationToken);

    /// <summary>Live bands matching the status and a normalized letter-grade search, highest lower bound first.</summary>
    Task<IReadOnlyList<Grade>> ListAsync(string? normalizedSearch, ConfigurationStatus status, CancellationToken cancellationToken);

    Task<Grade?> FindAsync(Guid id, CancellationToken cancellationToken);

    void Add(Grade grade);

    /// <summary>Saves pending changes; a unique-index violation is raised as <see cref="ConfigurationConflictException"/>.</summary>
    Task SaveAsync(CancellationToken cancellationToken);
}
