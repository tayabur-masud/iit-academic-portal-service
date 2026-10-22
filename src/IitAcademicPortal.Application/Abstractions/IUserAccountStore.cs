using IitAcademicPortal.Application.UserManagement;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Read projections of portal accounts for the Admin directory. Writes go through ASP.NET Identity and the shared
/// <see cref="ITeacherStore"/> unit of work, so account changes use the same transaction and last-Admin lock as Teacher Management.
/// Hashes, security stamps, and tokens are never selected.
/// </summary>
public interface IUserAccountStore
{
    Task<UserListPage> ListAsync(UserListFilter filter, int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>The account with its roles, teacher link, and created/last-modified facts (actor IDs only), or null when unknown.</summary>
    Task<UserAccountDetail?> GetDetailAsync(string userId, CancellationToken cancellationToken);

    /// <summary>The account's current email, or null when the account is unknown.</summary>
    Task<string?> GetEmailAsync(string userId, CancellationToken cancellationToken);
}
