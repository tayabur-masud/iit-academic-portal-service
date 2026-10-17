using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Identity;

/// <summary>
/// Options for the setup-token provider. Its name is also the Data Protection purpose, so its protector differs from the default
/// provider's (used for password reset), and its lifespan comes from <see cref="AccountCredentialSetupOptions.ProofLifespan"/>.
/// </summary>
public sealed class AccountCredentialSetupTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public AccountCredentialSetupTokenProviderOptions() => Name = AccountCredentialSetupService.TokenProvider;
}

/// <summary>
/// Issues and verifies first-password setup proofs. Like the default provider, a proof is bound to the account's security stamp and
/// the token purpose, so it stops validating after a reissue, a completed setup, or an email change, and a reset proof never validates
/// here. Nothing is stored.
/// </summary>
public sealed class AccountCredentialSetupTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AccountCredentialSetupTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<PortalUser>> logger)
    : DataProtectorTokenProvider<PortalUser>(dataProtectionProvider, options, logger);
