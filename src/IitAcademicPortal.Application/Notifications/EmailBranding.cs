using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// What makes an email look like the portal's: the logo, the portal name shown when there is no logo, the brand color, the footer,
/// and the label on a link button. Configured with the <c>Notifications:*</c> settings; never supplied by an Admin or a producer.
/// </summary>
public sealed record EmailBranding(
    string PortalName = EmailBranding.DefaultPortalName,
    string? LogoUrl = null,
    string LogoAlt = EmailBranding.DefaultLogoAlt,
    string Color = EmailBranding.DefaultColor,
    string FooterText = EmailBranding.DefaultFooter,
    string ButtonLabel = EmailBranding.DefaultButtonLabel)
{
    public const string DefaultPortalName = "IIT Academic Portal";
    public const string DefaultLogoAlt = "IIT, University of Dhaka";

    /// <summary>The IIT primary brand color from the design system.</summary>
    public const string DefaultColor = "#2E3192";

    public const string DefaultFooter = "This message was sent by the IIT Academic Portal, IIT, University of Dhaka. Please do not reply to this email.";
    public const string DefaultButtonLabel = "Open the portal link";
}

/// <summary>
/// Refuses to start with branding that would break the email: a color that is not a six-digit hex value (it goes into a style, so only
/// that exact shape is allowed) or that is too pale for the white button text (WCAG AA, 4.5:1), text that is empty, too long, or has
/// line breaks, or a logo address that is not an absolute https address.
/// </summary>
public sealed partial class NotificationOptionsValidator : IValidateOptions<NotificationOptions>
{
    public const int MaxTextLength = 300;

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColor();

    public ValidateOptionsResult Validate(string? name, NotificationOptions options)
    {
        var failures = new List<string>();

        if (!EmailLayout.IsWebAddress(options.PortalUrl))
        {
            failures.Add("Notifications:PortalUrl must be an absolute http or https address.");
        }

        if (options.ShowLogo)
        {
            // The logo is loaded by the recipient's mail client from the internet, so the address that will actually be used must be
            // absolute https: the explicit LogoUrl, or else the fallback built from PortalUrl. An http fallback is not accepted.
            if (!string.IsNullOrWhiteSpace(options.LogoUrl))
            {
                if (!EmailLayout.IsHttpsAddress(options.LogoUrl))
                {
                    failures.Add("Notifications:LogoUrl must be an absolute https address (mail clients load it from the internet).");
                }
            }
            else if (!EmailLayout.IsHttpsAddress(options.EffectiveLogoUrl))
            {
                failures.Add(
                    "Notifications:LogoUrl is required when Notifications:PortalUrl is not https, because the fallback logo address would be http. " +
                    "Set an explicit, publicly reachable https LogoUrl, or set Notifications:ShowLogo to false to send emails without the logo image.");
            }
        }

        if (!HexColor().IsMatch(options.BrandColor))
        {
            failures.Add("Notifications:BrandColor must be a six-digit hex color such as #2E3192.");
        }
        else if (ContrastWithWhite(options.BrandColor) < 4.5)
        {
            failures.Add("Notifications:BrandColor is too light for the white button text (it needs a contrast ratio of at least 4.5 to 1).");
        }

        foreach (var (key, value) in new[]
        {
            ("PortalName", options.PortalName), ("LogoAlt", options.LogoAlt), ("FooterText", options.FooterText), ("ButtonLabel", options.ButtonLabel),
        })
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxTextLength || value.Any(char.IsControl))
            {
                failures.Add($"Notifications:{key} must be one line of plain text, 1 to {MaxTextLength} characters.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>The WCAG contrast ratio between a six-digit hex color and white.</summary>
    public static double ContrastWithWhite(string hex)
    {
        double Channel(int shift)
        {
            var value = ((Convert.ToInt32(hex[1..], 16) >> shift) & 0xFF) / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(16) + 0.7152 * Channel(8) + 0.0722 * Channel(0);
        return 1.05 / (luminance + 0.05);
    }
}
