using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// The Admin account directory over the request's <see cref="PortalDbContext"/>. Only identity and access columns are selected;
/// password hashes, security stamps, and tokens never leave the database.
/// </summary>
public sealed class UserAccountStore(PortalDbContext db) : IUserAccountStore
{
    public async Task<UserListPage> ListAsync(UserListFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (filter.Status is { } status)
        {
            query = query.Where(u => u.AccountStatus == status);
        }

        if (filter.Role is { } role)
        {
            query = query.Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == role)));
        }

        if (filter.Search is { } search)
        {
            // Identity normalizes emails to upper case; a user ID matches exactly.
            var normalized = search.ToUpperInvariant();
            query = query.Where(u => u.Id == search || (u.NormalizedEmail != null && u.NormalizedEmail.Contains(normalized)));
        }

        var total = await query.LongCountAsync(cancellationToken);
        var skip = (int)Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await Rows(query.OrderBy(u => u.NormalizedEmail).ThenBy(u => u.Id).Skip(skip).Take(pageSize)).ToListAsync(cancellationToken);
        var totalPages = (int)Math.Min((total + pageSize - 1) / pageSize, int.MaxValue);
        return new UserListPage(rows.Select(ToDetail).ToList(), page, total, totalPages);
    }

    public async Task<UserAccountDetail?> GetDetailAsync(string userId, CancellationToken cancellationToken)
    {
        var row = await Rows(db.Users.AsNoTracking().Where(u => u.Id == userId)).FirstOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        var detail = ToDetail(row);
        if (row.TeacherId is { } teacherId)
        {
            var profile = await db.TeacherProfiles.AsNoTracking().FirstAsync(p => p.Id == teacherId, cancellationToken);
            return detail with
            {
                CreatedAtUtc = profile.CreatedAtUtc,
                CreatedByUserId = profile.CreatedByUserId,
                LastModifiedAtUtc = profile.LastModifiedAtUtc,
                LastModifiedByUserId = profile.LastModifiedByUserId,
            };
        }

        // A generic account has no audited row of its own, so its history is the account's audit events.
        var events = db.AuditEvents.AsNoTracking()
            .Where(e => e.EntityType == UserAccountAuditEventDefinitions.EntityType && e.EntityId == userId);
        var first = await events.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => new { e.OccurredAtUtc, e.ActorUserId }).FirstOrDefaultAsync(cancellationToken);
        var last = await events.OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.Id)
            .Select(e => new { e.OccurredAtUtc, e.ActorUserId }).FirstOrDefaultAsync(cancellationToken);
        return first is null
            ? detail
            : detail with
            {
                CreatedAtUtc = first.OccurredAtUtc,
                CreatedByUserId = first.ActorUserId,
                LastModifiedAtUtc = last!.OccurredAtUtc,
                LastModifiedByUserId = last.ActorUserId,
            };
    }

    // One round trip for a page: the account, its teacher link, and assigned roles.
    private IQueryable<Row> Rows(IQueryable<PortalUser> users) =>
        users.Select(u => new Row(
            u.Id,
            u.Email,
            u.AccountStatus,
            u.DefaultRole,
            u.ConcurrencyStamp,
            db.TeacherProfiles.Where(p => p.UserId == u.Id).Select(p => (Guid?)p.Id).FirstOrDefault(),
            db.UserRoles.Where(ur => ur.UserId == u.Id).Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!).ToList()));

    private static UserAccountDetail ToDetail(Row row) =>
        new(row.UserId, row.Email ?? string.Empty, row.Status, PortalRoles.Normalize(row.Roles), row.DefaultRole, row.TeacherId,
            row.ConcurrencyStamp ?? string.Empty);

    private sealed record Row(
        string UserId, string? Email, TeacherAccountStatus Status, string? DefaultRole, string? ConcurrencyStamp, Guid? TeacherId, List<string> Roles);
}
