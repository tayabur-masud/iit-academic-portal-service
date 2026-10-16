using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// The one IIT-branded HTML layout every notification email is sent in: the logo, a heading (the subject), the message, and a footer.
/// It is built in memory at send time from the plain-text subject and body, which stay the only thing an Admin edits and the only thing
/// stored. Every piece of text is HTML-encoded, so supplied values can never become markup; only whole http(s) addresses become links,
/// and an address on a line of its own becomes a button. The plain-text body is sent alongside as the alternative.
/// </summary>
public static partial class EmailLayout
{
    // Neutral colors shared by every brand; only the primary (brand) color is configurable.
    private const string Background = "#F8F9FC";
    private const string TextPrimary = "#111827";
    private const string TextSecondary = "#4B5563";
    private const string Border = "#D1D5DB";

    [GeneratedRegex(@"^\s*(https?://\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LoneAddress();

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    /// <summary>
    /// Wraps a message in the branded layout.
    /// </summary>
    /// <param name="subject">Shown as the heading and as the document title.</param>
    /// <param name="plainBody">The plain-text message; blank lines separate paragraphs.</param>
    /// <param name="branding">The logo, portal name, brand color, footer, and button label. Configured, never supplied by an Admin or a producer.</param>
    public static string Render(string subject, string plainBody, EmailBranding branding)
    {
        var logoUrl = branding.LogoUrl;
        var primary = branding.Color;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<meta name=\"color-scheme\" content=\"light\"><title>").Append(Encode(subject)).Append("</title></head>");
        html.Append("<body style=\"margin:0;padding:0;background:").Append(Background).Append(";\">");
        html.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"background:").Append(Background).Append(";\"><tr><td align=\"center\" style=\"padding:24px 12px;\">");
        html.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background:#FFFFFF;border:1px solid ").Append(Border).Append(";border-radius:8px;font-family:'DM Sans',Arial,Helvetica,sans-serif;color:").Append(TextPrimary).Append(";\">");

        // Header: the logo on white, with a brand-color rule beneath it.
        html.Append("<tr><td style=\"padding:20px 28px;border-bottom:4px solid ").Append(primary).Append(";\">");
        if (IsWebAddress(logoUrl))
        {
            html.Append("<img src=\"").Append(Encode(logoUrl!)).Append("\" alt=\"").Append(Encode(branding.LogoAlt)).Append("\" height=\"48\" style=\"display:block;height:48px;width:auto;border:0;\">");
        }
        else
        {
            html.Append("<span style=\"font-size:20px;font-weight:bold;color:").Append(primary).Append(";\">").Append(Encode(branding.PortalName)).Append("</span>");
        }

        html.Append("</td></tr>");

        // Heading and message.
        html.Append("<tr><td style=\"padding:28px 28px 8px 28px;\"><h1 style=\"margin:0 0 16px 0;font-size:22px;line-height:1.3;color:").Append(primary).Append(";\">").Append(Encode(subject)).Append("</h1>");
        foreach (var paragraph in Paragraphs(plainBody))
        {
            AppendParagraph(html, paragraph, branding);
        }

        html.Append("</td></tr>");

        // Footer.
        html.Append("<tr><td style=\"padding:16px 28px 24px 28px;border-top:1px solid ").Append(Border).Append(";font-size:13px;line-height:1.5;color:").Append(TextSecondary).Append(";\">");
        html.Append(Encode(branding.FooterText)).Append("</td></tr></table></td></tr></table></body></html>");
        return html.ToString();
    }

    private static IEnumerable<string> Paragraphs(string body) =>
        Regex.Split(body.Replace("\r\n", "\n").Replace('\r', '\n').Trim(), @"\n{2,}").Where(p => p.Trim().Length > 0);

    private static void AppendParagraph(StringBuilder html, string paragraph, EmailBranding branding)
    {
        // A paragraph that is only an address (a secure link, or the portal address) becomes a button, with the address shown below it
        // as text so it can still be copied where a button does not work.
        if (LoneAddress().Match(paragraph) is { Success: true } lone && IsWebAddress(lone.Groups[1].Value))
        {
            var url = lone.Groups[1].Value;
            html.Append("<p style=\"margin:0 0 16px 0;\"><a href=\"").Append(Encode(url)).Append("\" style=\"display:inline-block;background:").Append(branding.Color)
                .Append(";color:#FFFFFF;text-decoration:none;font-weight:bold;padding:12px 24px;border-radius:6px;\">").Append(Encode(branding.ButtonLabel)).Append("</a></p>");
            html.Append("<p style=\"margin:0 0 16px 0;font-size:13px;line-height:1.5;color:").Append(TextSecondary).Append(";word-break:break-all;\">If the button does not work, copy this address into your browser:<br>")
                .Append(Encode(url)).Append("</p>");
            return;
        }

        html.Append("<p style=\"margin:0 0 16px 0;font-size:16px;line-height:1.6;color:").Append(TextPrimary).Append(";\">");
        var lines = paragraph.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                html.Append("<br>");
            }

            AppendLine(html, lines[i], branding.Color);
        }

        html.Append("</p>");
    }

    // Text is encoded piece by piece; only a whole http(s) address becomes a link, so nothing else is ever interpreted as markup.
    private static void AppendLine(StringBuilder html, string line, string primary)
    {
        var position = 0;
        foreach (Match match in Address().Matches(line))
        {
            html.Append(Encode(line[position..match.Index]));
            var url = match.Value.TrimEnd('.', ',', ')', ';', ':');
            if (IsWebAddress(url))
            {
                html.Append("<a href=\"").Append(Encode(url)).Append("\" style=\"color:").Append(primary).Append(";\">").Append(Encode(url)).Append("</a>");
                html.Append(Encode(match.Value[url.Length..]));
            }
            else
            {
                html.Append(Encode(match.Value));
            }

            position = match.Index + match.Length;
        }

        html.Append(Encode(line[position..]));
    }

    /// <summary>True only for an absolute http or https address, so a <c>javascript:</c> or <c>data:</c> value is never linked or loaded.</summary>
    public static bool IsWebAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
