namespace IitAcademicPortal.Domain.Sessions;

/// <summary>
/// One independently revocable authenticated browser session.
/// </summary>
/// <remarks>
/// There is deliberately no expiry, idle-timeout, or maximum-age field: the approved policy keeps a
/// session active until explicit logout or a specified revocation action. The raw session handle is
/// never stored; only its one-way digest is persisted.
/// </remarks>
public class AuthSession
{
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
    }

    public Guid Id { get; private set; }

    public string HandleDigest { get; private set; } = string.Empty;

    public string UserId { get; private set; } = string.Empty;

    /// <summary>Null only while a multi-role user has not yet chosen a role.</summary>
    public string? ActiveRole { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAt is not null;

    /// <summary>Changes this session's active role. Callers must verify the role is currently assigned.</summary>
    public void SetActiveRole(string role) => ActiveRole = role;

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
