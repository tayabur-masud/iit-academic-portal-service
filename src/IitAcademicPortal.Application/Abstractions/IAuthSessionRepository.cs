using IitAcademicPortal.Domain.Sessions;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>An active (unrevoked) session with the account's currently assigned roles and stored default role.</summary>
public sealed record SessionSnapshot(AuthSession Session, IReadOnlyList<string> AssignedRoles, string? DefaultRole);

/// <summary>A session that was revoked on this request because it had been idle too long.</summary>
public sealed record IdleRevokedSession(Guid SessionId, string UserId);

/// <summary>
/// The outcome of validating a session handle: the active session, or nothing, and whether the check itself
/// revoked an idle session so the caller can record it.
/// </summary>
public sealed record SessionTouchResult(SessionSnapshot? Active, IdleRevokedSession? IdleRevoked);

public interface IAuthSessionRepository
{
    Task<SessionTouchResult> TouchActiveByDigestAsync(
        string handleDigest, DateTimeOffset now, CancellationToken cancellationToken);

    Task<SessionSnapshot?> FindActiveByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes every active session of the account in one statement and returns how many were revoked. It saves on its own,
    /// so callers needing atomicity run it inside their own transaction.
    /// </summary>
    Task<int> RevokeAllActiveForUserAsync(
        string userId, SessionRevocationReason reason, DateTimeOffset now, CancellationToken cancellationToken);

    void Add(AuthSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
