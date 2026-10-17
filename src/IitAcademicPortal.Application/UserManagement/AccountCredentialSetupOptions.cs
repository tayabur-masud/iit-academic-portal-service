namespace IitAcademicPortal.Application.UserManagement;

/// <summary>
/// First-password setup for newly provisioned accounts. Separate from password recovery: its own Identity token provider, purpose,
/// and lifespan, so a reset proof can never activate an account and a setup proof can never reset a password.
/// </summary>
public sealed class AccountCredentialSetupOptions
{
    public const string SectionName = "AccountCredentialSetup";

    /// <summary>
    /// Absolute URL of the frontend setup page that receives the account ID and proof. When empty, the page is
    /// <c>{Notifications:PortalUrl}/account-setup</c>. Production must use the public https portal address.
    /// </summary>
    public string SetupPageUrl { get; set; } = string.Empty;

    /// <summary>How long a setup link stays valid after it is generated. Links are also single-use and invalidated by a reissue.</summary>
    public TimeSpan ProofLifespan { get; set; } = TimeSpan.FromDays(3);

    public string EffectiveSetupPageUrl(string portalUrl) =>
        string.IsNullOrWhiteSpace(SetupPageUrl) ? $"{portalUrl.TrimEnd('/')}/account-setup" : SetupPageUrl;
}
