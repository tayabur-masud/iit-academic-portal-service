using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Email;

/// <summary>Which email adapter delivers notifications. Smtp is the default; Brevo is for Development only.</summary>
public sealed class EmailProviderOptions
{
    public const string SectionName = "Email";

    public const string Smtp = "Smtp";
    public const string Brevo = "Brevo";

    public string Provider { get; set; } = Smtp;
}

/// <summary>
/// Brevo transactional email, for Development with synthetic identities only. The API key and sender address come from user secrets
/// or environment configuration and are never committed. Every destination must be in <see cref="AllowedRecipients"/>; an empty list
/// rejects every send, before any request leaves the process.
/// </summary>
public sealed class BrevoOptions
{
    public const string SectionName = "Brevo";

    public string? ApiKey { get; set; }

    /// <summary>The verified sender address configured in the Brevo account.</summary>
    public string? SenderEmail { get; set; }

    public string SenderName { get; set; } = "IIT Academic Portal";

    public string BaseAddress { get; set; } = "https://api.brevo.com/";

    /// <summary>Exact addresses of controlled, non-production test mailboxes. Matching ignores case.</summary>
    public string[] AllowedRecipients { get; set; } = [];

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Refuses to start with Brevo anywhere but Development, and refuses an incomplete Brevo configuration. A Brevo API key present in
/// another environment is also rejected, so a Development secret cannot be carried into Production by accident.
/// </summary>
public sealed class EmailProviderOptionsValidator(IHostEnvironment environment, IOptions<BrevoOptions> brevo) : IValidateOptions<EmailProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, EmailProviderOptions options)
    {
        var provider = options.Provider;
        if (!string.Equals(provider, EmailProviderOptions.Smtp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(provider, EmailProviderOptions.Brevo, StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail($"Email:Provider must be {EmailProviderOptions.Smtp} or {EmailProviderOptions.Brevo}.");
        }

        var usesBrevo = string.Equals(provider, EmailProviderOptions.Brevo, StringComparison.OrdinalIgnoreCase);
        if (!environment.IsDevelopment() && (usesBrevo || !string.IsNullOrWhiteSpace(brevo.Value.ApiKey)))
        {
            return ValidateOptionsResult.Fail("Brevo is a Development-only email provider and must not be configured outside Development. Select an approved Production provider.");
        }

        if (usesBrevo && (string.IsNullOrWhiteSpace(brevo.Value.ApiKey) || string.IsNullOrWhiteSpace(brevo.Value.SenderEmail)))
        {
            return ValidateOptionsResult.Fail("Brevo:ApiKey and Brevo:SenderEmail must be configured (user secrets or environment) to use the Brevo provider.");
        }

        return ValidateOptionsResult.Success;
    }
}
