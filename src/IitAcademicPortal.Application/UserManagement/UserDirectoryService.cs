using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.UserManagement;

/// <summary>
/// The Admin account directory: every portal account (Admin, Student, Teacher, Coordinator), searched by registered email or stable
/// user ID and filtered by role and account status, in bounded pages ordered by normalized email then user ID. Read-only: it never
/// changes an account or a feature-owned profile. Authorization is the caller's job.
/// </summary>
public sealed class UserDirectoryService(IUserAccountStore store, IAuditActorDirectory actors)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;
    public const int MaxSearchLength = 256;

    public async Task<AccountResult<UserListPage>> ListAsync(UserQuery query, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        TeacherAccountStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<TeacherAccountStatus>(query.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                && !int.TryParse(query.Status, out _))
            {
                status = parsed;
            }
            else
            {
                errors["status"] = ["The status must be Active, PendingCredentialSetup, or Deactivated."];
            }
        }

        string? role = null;
        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            role = PortalRoles.All.FirstOrDefault(r => string.Equals(r, query.Role.Trim(), StringComparison.OrdinalIgnoreCase));
            if (role is null)
            {
                errors["role"] = ["The role must be Admin, Student, Teacher, or Coordinator."];
            }
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
        if (search?.Length > MaxSearchLength)
        {
            errors["search"] = [$"The search can have at most {MaxSearchLength} characters."];
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors["pageSize"] = [$"The page size must be between 1 and {MaxPageSize}."];
        }

        var page = query.Page ?? 1;
        if (page < 1)
        {
            errors["page"] = ["The page number must be 1 or more."];
        }

        if (errors.Count > 0)
        {
            return AccountResult<UserListPage>.Invalid(errors);
        }

        var result = await store.ListAsync(new UserListFilter(search, role, status), page, pageSize, cancellationToken);
        return new AccountResult<UserListPage>(AccountOutcome.Ok, result);
    }

    public async Task<AccountResult<UserAccountDetail>> GetAsync(string userId, CancellationToken cancellationToken)
    {
        var detail = await GetDetailAsync(store, actors, userId, cancellationToken);
        return detail is null ? AccountResult<UserAccountDetail>.NotFound : new AccountResult<UserAccountDetail>(AccountOutcome.Ok, detail);
    }

    /// <summary>The account detail with the current email of the accounts that created and last changed it.</summary>
    internal static async Task<UserAccountDetail?> GetDetailAsync(
        IUserAccountStore store, IAuditActorDirectory actors, string userId, CancellationToken cancellationToken)
    {
        var detail = await store.GetDetailAsync(userId, cancellationToken);
        if (detail is null)
        {
            return null;
        }

        var ids = new[] { detail.CreatedByUserId, detail.LastModifiedByUserId }.OfType<string>().Distinct().ToList();
        var emails = ids.Count == 0 ? new Dictionary<string, string>() : await actors.GetEmailsAsync(ids, cancellationToken);
        return detail with
        {
            CreatedBy = detail.CreatedByUserId is { } c ? emails.GetValueOrDefault(c) : null,
            LastModifiedBy = detail.LastModifiedByUserId is { } m ? emails.GetValueOrDefault(m) : null,
        };
    }
}
