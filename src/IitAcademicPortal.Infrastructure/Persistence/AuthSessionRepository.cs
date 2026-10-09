using System.Linq.Expressions;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

public sealed class AuthSessionRepository(PortalDbContext db) : IAuthSessionRepository
{
    public async Task<SessionTouchResult> TouchActiveByDigestAsync(
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
            // Identify the idle session first so its revocation can be reported to the caller for auditing.
            var idle = await matchingSession
                .Where(s => s.LastActivityAt <= idleCutoff)
                .Select(s => new { s.Id, s.UserId })
                .FirstOrDefaultAsync(cancellationToken);
            if (idle is null)
            {
                return new SessionTouchResult(null, null);
            }

            var revoked = await matchingSession
                .Where(s => s.Id == idle.Id && s.LastActivityAt <= idleCutoff)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(s => s.RevokedAt, now)
                    .SetProperty(s => s.RevocationReason, SessionRevocationReason.IdleTimeout), cancellationToken);

            // Only the request that actually revoked the session reports it, so one timeout yields one event.
            return new SessionTouchResult(null, revoked == 1 ? new IdleRevokedSession(idle.Id, idle.UserId) : null);
        }

        return new SessionTouchResult(await FindActiveAsync(s => s.HandleDigest == handleDigest, cancellationToken), null);
    }

    public Task<SessionSnapshot?> FindActiveByIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
        FindActiveAsync(s => s.Id == sessionId, cancellationToken);

    public void Add(AuthSession session) => db.AuthSessions.Add(session);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    // One round trip: the session plus the account's current role names and default role, read on every
    // protected request.
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
                DefaultRole = db.Users.Where(u => u.Id == s.UserId).Select(u => u.DefaultRole).FirstOrDefault(),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null ? null : new SessionSnapshot(row.Session, row.Roles, row.DefaultRole);
    }
}
