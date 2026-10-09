using System.Linq.Expressions;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

public sealed class AuthSessionRepository(PortalDbContext db) : IAuthSessionRepository
{
    public async Task<SessionSnapshot?> TouchActiveByDigestAsync(
        string handleDigest, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var matchingSession = db.AuthSessions
            .Where(s => s.HandleDigest == handleDigest && s.RevokedAt == null);
        var idleCutoff = now - AuthSession.IdleTimeout;
        var touched = await matchingSession
            .Where(s => s.LastActivityAt > idleCutoff)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastActivityAt, now), cancellationToken);

        if (touched == 0)
        {
            await matchingSession
                .Where(s => s.LastActivityAt <= idleCutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.RevocationReason, SessionRevocationReason.IdleTimeout), cancellationToken);
            return null;
        }

        return await FindActiveAsync(s => s.HandleDigest == handleDigest, cancellationToken);
    }

    public Task<SessionSnapshot?> FindActiveByIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        FindActiveAsync(s => s.Id == sessionId, cancellationToken);

    public void Add(AuthSession session) => db.AuthSessions.Add(session);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    // One round trip: the session plus the account's current role names, read on every protected request.
    private async Task<SessionSnapshot?> FindActiveAsync(
        Expression<Func<AuthSession, bool>> predicate, CancellationToken cancellationToken)
    {
        var row = await db.AuthSessions
            .Where(predicate)
            .Where(s => s.RevokedAt == null)
            .Select(s => new
            {
                Session = s,
                Roles = db.UserRoles
                    .Where(ur => ur.UserId == s.UserId)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                    .ToList(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new SessionSnapshot(row.Session, row.Roles);
    }
}
