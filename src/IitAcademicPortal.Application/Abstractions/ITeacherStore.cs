using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Teachers;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>An open database transaction. Disposing it without committing rolls everything back.</summary>
public interface ITeacherTransaction : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken);
}

public enum TeacherConflictKind
{
    Email,
    StaffId,
    Other,
}

/// <summary>Raised by the store when the database rejected a change: a duplicate that raced past the application check.</summary>
public sealed class TeacherConflictException(TeacherConflictKind kind, Exception inner)
    : Exception("A teacher rule in the database rejected the change.", inner)
{
    public TeacherConflictKind Kind { get; } = kind;
}

/// <summary>
/// Persistence for teacher profiles over the request's unit of work. ASP.NET Identity saves inside its own calls on the same
/// context, so every multi-step change runs inside one <see cref="BeginTransactionAsync"/> transaction together with its staged
/// audit event.
/// </summary>
public interface ITeacherStore
{
    Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serializes the last-Admin check with the change that depends on it, for the rest of the transaction. Call it first, then
    /// <see cref="HasOtherActiveAdminAsync"/>, so two concurrent changes cannot each see the other Admin and both remove it.
    /// </summary>
    Task LockAdminInvariantAsync(CancellationToken cancellationToken);

    Task<TeacherProfile?> FindProfileAsync(Guid teacherId, CancellationToken cancellationToken);

    Task<TeacherProfile?> FindProfileByUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>True when another teacher profile (active or not) already has this normalized staff ID.</summary>
    Task<bool> StaffIdExistsAsync(string normalizedStaffId, Guid exceptProfileId, CancellationToken cancellationToken);

    /// <summary>True when the Designation exists, is not deleted, and is active, so it may be newly chosen.</summary>
    Task<bool> DesignationAvailableAsync(Guid designationId, CancellationToken cancellationToken);

    /// <summary>True when an account other than the given one is an Admin and Active.</summary>
    Task<bool> HasOtherActiveAdminAsync(string exceptUserId, CancellationToken cancellationToken);

    /// <summary>The supported role names currently assigned to the account.</summary>
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Adds or removes a role for the account in the pending unit of work, without saving.</summary>
    Task StageRoleAsync(string userId, string roleName, bool assigned, CancellationToken cancellationToken);

    void Add(TeacherProfile profile);

    /// <exception cref="TeacherConflictException">A uniqueness rule in the database rejected the change.</exception>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<TeacherDetail?> GetDetailAsync(Guid teacherId, CancellationToken cancellationToken);

    Task<TeacherListPage> ListAsync(TeacherListFilter filter, int page, int pageSize, CancellationToken cancellationToken);
}
