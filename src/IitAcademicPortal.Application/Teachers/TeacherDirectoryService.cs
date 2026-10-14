using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Application.Teachers;

/// <summary>
/// The Admin teacher directory: numbered pages, a search over name, email and staff ID, and a filter by account status. Pages are
/// bounded so a large directory is never loaded at once.
/// </summary>
public sealed class TeacherDirectoryService(ITeacherStore store)
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public async Task<TeacherResult<TeacherListPage>> ListAsync(TeacherQuery query, CancellationToken cancellationToken)
    {
        var errors = new TeacherErrors();
        TeacherAccountStatus? status = null;
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (Enum.TryParse<TeacherAccountStatus>(query.Status, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
            {
                status = parsed;
            }
            else
            {
                errors.Add("status", "The status must be Active, PendingCredentialSetup, or Deactivated.");
            }
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"The page size must be between 1 and {MaxPageSize}.");
        }

        var page = query.Page ?? 1;
        if (page < 1)
        {
            errors.Add("page", "The page number must be 1 or more.");
        }

        if (errors.Any)
        {
            return TeacherResult<TeacherListPage>.Invalid(errors);
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : NameNormalizer.Normalize(query.Search);
        var result = await store.ListAsync(new TeacherListFilter(search, status), page, pageSize, cancellationToken);
        return new TeacherResult<TeacherListPage>(TeacherOutcome.Ok, result);
    }
}
