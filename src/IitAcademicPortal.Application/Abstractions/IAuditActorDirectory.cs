namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// Resolves current account emails when audit history is viewed. Events store only the stable user ID, so no
/// personal data is copied into append-only history.
/// </summary>
public interface IAuditActorDirectory
{
    /// <summary>Returns the email of each existing account; IDs of removed accounts are absent.</summary>
    Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken);
}
