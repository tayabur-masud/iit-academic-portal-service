using IitAcademicPortal.Domain.Sessions;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>An active (unrevoked) session with the account's currently assigned roles and stored default role.</summary>
public sealed record SessionSnapshot(AuthSession Session, IReadOnlyList<string> AssignedRoles, string? DefaultRole);

public interface IAuthSessionRepository
{
    Task<SessionSnapshot?> TouchActiveByDigestAsync(
        string handleDigest, DateTimeOffset now, CancellationToken cancellationToken);

    Task<SessionSnapshot?> FindActiveByIdAsync(Guid sessionId, CancellationToken cancellationToken);

    void Add(AuthSession session);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
