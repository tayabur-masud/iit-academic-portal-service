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

    /// <summary>
    /// How long an accepted request (the protected address) is kept while waiting to be processed. After this it is deleted
    /// unprocessed, so a long outage cannot send reset links for requests nobody is still waiting on. This is independent of
    /// <see cref="ProofLifespan"/>, which starts when the proof is generated.
    /// </summary>
    [Range(typeof(TimeSpan), "00:01:00", "1.00:00:00")]
    public TimeSpan RequestLifetime { get; set; } = TimeSpan.FromHours(1);
}
