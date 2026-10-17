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
