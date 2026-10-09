using IitAcademicPortal.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

public sealed class AuditActorDirectory(PortalDbContext db) : IAuditActorDirectory
{
    public async Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(
        IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        // One query for the whole page, not one per event.
        var rows = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && u.Email != null)
            .Select(u => new { u.Id, Email = u.Email! })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.Id, r => r.Email);
    }
}
