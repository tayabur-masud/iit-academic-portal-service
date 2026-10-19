using System.ComponentModel.DataAnnotations;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>
/// Request-only body of the public setup page. <c>proof</c> and <c>newPassword</c> are accepted here only; they are never returned,
/// audited, or logged, and no response type carries them. Unknown JSON members are rejected globally.
/// </summary>
public sealed class AccountCredentialSetupRequest
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; init; } = string.Empty;

    [Required]
    [MaxLength(4096)]
    public string Proof { get; init; } = string.Empty;

    [Required]
    public string NewPassword { get; init; } = string.Empty;
}

/// <summary>
/// Request-only body of the setup page's link check. The proof is accepted here only, never returned, audited, or logged. Sent in a body,
/// not a query string, so it never appears in request logs.
/// </summary>
public sealed class AccountCredentialSetupCheckRequest
{
    [Required]
    [MaxLength(450)]
    public string UserId { get; init; } = string.Empty;

    [Required]
    [MaxLength(4096)]
    public string Proof { get; init; } = string.Empty;
}

/// <summary>Whether the setup link can still be used. Nothing else about the account is returned.</summary>
public sealed record AccountCredentialSetupCheckResponse(bool Usable);
