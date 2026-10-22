using IitAcademicPortal.Application.Staff;
using IitAcademicPortal.Domain.Staff;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Staff profiles over the request's unit of work. The account, profile, staged audit events, and session revocations of one change are
/// saved by <see cref="SaveAsync"/> inside one <see cref="BeginTransactionAsync"/> transaction, so they commit together or not at all.
/// </summary>
public interface IStaffStore
{
    Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serializes every change that could leave no active Admin (shared with Teacher and User Management) until the transaction ends,
    /// so two concurrent deactivations cannot each see the other Admin and both proceed.
    /// </summary>
    Task LockAdminInvariantAsync(CancellationToken cancellationToken);

    /// <summary>True when an active account other than this one holds the Admin role.</summary>
    Task<bool> HasOtherActiveAdminAsync(string exceptUserId, CancellationToken cancellationToken);

    /// <summary>The portal account with its status and whether it holds the Admin role, or null when there is none.</summary>
    Task<StaffAccount?> FindAccountAsync(string userId, CancellationToken cancellationToken);

    Task<bool> ProfileExistsForUserAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The profile, tracked for change.</summary>
    Task<StaffProfile?> FindProfileAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>True when the Designation exists, is not deleted, and is active.</summary>
    Task<bool> DesignationAvailableAsync(Guid designationId, CancellationToken cancellationToken);

    void Add(StaffProfile profile);

    /// <summary>Saves pending changes; a duplicate profile or account is raised as <see cref="StaffConflictException"/>.</summary>
    Task SaveAsync(CancellationToken cancellationToken);

    Task<StaffDetail?> GetDetailAsync(Guid profileId, CancellationToken cancellationToken);

    /// <summary>One page ordered by name, matching a normalized search over name, account email, mobile number, and Designation name.</summary>
    Task<StaffListPage> ListAsync(string? normalizedSearch, int page, int pageSize, CancellationToken cancellationToken);
}
