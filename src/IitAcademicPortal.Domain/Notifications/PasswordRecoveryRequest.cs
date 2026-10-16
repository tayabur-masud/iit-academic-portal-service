namespace IitAcademicPortal.Domain.Notifications;

/// <summary>
/// Durable, short-lived input for a password-recovery request, so a restart or overflow cannot lose it. The submitted address is
/// kept only in protected (Data Protection) form and the whole row is deleted once processed or expired. No proof is ever stored.
/// </summary>
public sealed class PasswordRecoveryRequest
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string ProtectedAddress { get; private set; } = string.Empty;

    public DateTime RequestedAtUtc { get; private set; }

    public DateTime ExpiresAtUtc { get; private set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    public int AttemptCount { get; set; }

    /// <summary>A key-only instance used to delete a row by identifier without loading it.</summary>
    public static PasswordRecoveryRequest Stub(Guid id) => new() { Id = id };

    public static PasswordRecoveryRequest Create(string protectedAddress, DateTime requestedAtUtc, TimeSpan lifetime) =>
        new() { ProtectedAddress = protectedAddress, RequestedAtUtc = requestedAtUtc, ExpiresAtUtc = requestedAtUtc + lifetime };
}
