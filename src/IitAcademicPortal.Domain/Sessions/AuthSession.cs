namespace IitAcademicPortal.Domain.Sessions;

/// <summary>
/// One independently revocable authenticated browser session.
/// </summary>
/// <remarks>
/// Sessions expire after three hours without authenticated activity. The raw session handle is never
/// stored; only its one-way digest is persisted.
/// </remarks>
public class AuthSession
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromHours(3);

    private AuthSession()
    {
    }

    public AuthSession(string userId, string handleDigest, string? activeRole, DateTimeOffset createdAt)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        HandleDigest = handleDigest;
        ActiveRole = activeRole;
        CreatedAt = createdAt;
        LastActivityAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string HandleDigest { get; private set; } = string.Empty;

    public string UserId { get; private set; } = string.Empty;

    /// <summary>Starts as the account's default role; null only when the account has no supported role.</summary>
    public string? ActiveRole { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset LastActivityAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Changes this session's active role. Callers must verify the role is currently assigned.</summary>
    public void SetActiveRole(string role) => ActiveRole = role;

    public bool HasExceededIdleTimeout(DateTimeOffset now) => now >= LastActivityAt + IdleTimeout;

    public void Revoke(SessionRevocationReason reason, DateTimeOffset at)
    {
        if (IsRevoked)
        {
            return;
        }

        RevokedAt = at;
        RevocationReason = reason;
    }
}
