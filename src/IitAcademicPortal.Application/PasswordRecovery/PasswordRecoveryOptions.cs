using System.ComponentModel.DataAnnotations;

namespace IitAcademicPortal.Application.PasswordRecovery;

public sealed class PasswordRecoveryOptions
{
    public const string SectionName = "PasswordRecovery";

    /// <summary>Absolute URL of the frontend reset page that receives the email and proof.</summary>
    [Required]
    [Url]
    public string ResetPageUrl { get; set; } = string.Empty;

    /// <summary>How long a recovery proof remains valid. Proofs are also single-use.</summary>
    public TimeSpan ProofLifespan { get; set; } = TimeSpan.FromHours(1);
}
