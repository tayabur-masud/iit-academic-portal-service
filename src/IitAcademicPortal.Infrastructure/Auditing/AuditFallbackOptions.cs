using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>
/// The separately configured durable sink for security events whose outbox write failed. Console logging is
/// never accepted as durable.
/// </summary>
public sealed class AuditFallbackOptions
{
    public const string SectionName = "AuditFallback";

    /// <summary>The kind of sink. Only <c>File</c> exists today; the deployment owner may approve others.</summary>
    public string? Sink { get; set; }

    /// <summary>The folder for the <c>File</c> sink. Use a durable volume outside Development.</summary>
    public string? Directory { get; set; }
}

/// <summary>Refuses to start outside Development unless a durable sink is configured.</summary>
public sealed class AuditFallbackOptionsValidator(IHostEnvironment environment) : IValidateOptions<AuditFallbackOptions>
{
    public ValidateOptionsResult Validate(string? name, AuditFallbackOptions options)
    {
        if (environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (!string.Equals(options.Sink, "File", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "A durable security-event sink must be configured outside Development: set AuditFallback:Sink to a supported sink "
                + "approved by the deployment owner. Console logging is not durable.");
        }

        return string.IsNullOrWhiteSpace(options.Directory)
            ? ValidateOptionsResult.Fail("AuditFallback:Directory must name a folder on a durable volume for the File sink.")
            : ValidateOptionsResult.Success;
    }
}
