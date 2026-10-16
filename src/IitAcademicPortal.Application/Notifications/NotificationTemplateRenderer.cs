using System.Text.RegularExpressions;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>A message after placeholders were filled in. Plain text only.</summary>
public sealed record RenderedMessage(string Subject, string Body);

/// <summary>
/// Validates and renders template wording. Placeholders are named values (<c>{{name}}</c>) from a fixed list per event; there are no
/// expressions, no markup, and no HTML. Values are inserted as plain text and never interpreted.
/// </summary>
public sealed partial class NotificationTemplateRenderer
{
    public const int MaxSampleValueLength = 200;

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]{0,39})\s*\}\}")]
    private static partial Regex PlaceholderPattern();

    // Opening or closing tags, comments, declarations, and processing instructions.
    [GeneratedRegex(@"<\s*/?\s*[A-Za-z!?]")]
    private static partial Regex MarkupPattern();

    /// <summary>The placeholder names used in the text, in order of appearance, without duplicates.</summary>
    public static IReadOnlyList<string> PlaceholdersIn(string text) =>
        PlaceholderPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>Checks wording against the event's rules and returns field-keyed errors (<c>subject</c>, <c>body</c>).</summary>
    public NotificationErrors Validate(NotificationEventType eventType, string? subject, string? body)
    {
        var errors = new NotificationErrors();
        var definition = NotificationTemplateCatalog.For(eventType);

        var trimmedSubject = (subject ?? string.Empty).Trim();
        if (trimmedSubject.Length == 0)
        {
            errors.Add("subject", "Enter a subject.");
        }
        else if (trimmedSubject.Length > NotificationTemplateRevision.MaxSubjectLength)
        {
            errors.Add("subject", $"The subject can have at most {NotificationTemplateRevision.MaxSubjectLength} characters.");
        }
        else if (trimmedSubject.Any(char.IsControl))
        {
            errors.Add("subject", "The subject must be a single line of plain text.");
        }

        var text = body ?? string.Empty;
        if (text.Trim().Length == 0)
        {
            errors.Add("body", "Enter the message text.");
        }
        else if (text.Length > NotificationTemplateRevision.MaxBodyLength)
        {
            errors.Add("body", $"The message can have at most {NotificationTemplateRevision.MaxBodyLength} characters.");
        }

        CheckText("subject", trimmedSubject, definition, errors);
        CheckText("body", text, definition, errors);

        if (!errors.Has("body"))
        {
            var used = PlaceholdersIn(text);
            foreach (var required in definition.Required.Where(r => definition.IsSecurity && !used.Contains(r, StringComparer.Ordinal)))
            {
                errors.Add("body", $"The message must include {{{{{required}}}}}, the link the recipient needs.");
            }
        }

        return errors;
    }

    /// <summary>The first placeholder the wording uses that has no value, or null when every one has a value.</summary>
    public string? MissingValues(string subject, string body, IReadOnlyDictionary<string, string> values) =>
        PlaceholdersIn(subject).Concat(PlaceholdersIn(body)).FirstOrDefault(name => !values.ContainsKey(name));

    /// <summary>Fills the placeholders. Every placeholder the text uses must have a value; values lose line breaks in the subject.</summary>
    public RenderedMessage Render(string subject, string body, IReadOnlyDictionary<string, string> values) =>
        new(Fill(subject, values, singleLine: true), Fill(body, values, singleLine: false));

    /// <summary>
    /// A preview with synthetic values only. Sample values supplied by the Admin may replace the synthetic ones for ordinary events,
    /// but a security event always shows the fixed, nonfunctional link placeholder: a preview never resolves an account, generates
    /// a proof, or returns a live link.
    /// </summary>
    public RenderedMessage Preview(
        NotificationEventType eventType, string subject, string body, IReadOnlyDictionary<string, string>? sampleValues)
    {
        var definition = NotificationTemplateCatalog.For(eventType);
        var values = new Dictionary<string, string>(definition.SampleValues, StringComparer.Ordinal);
        foreach (var (key, value) in sampleValues ?? new Dictionary<string, string>())
        {
            if (definition.Placeholders.Contains(key, StringComparer.Ordinal) && !(definition.IsSecurity && key == NotificationTemplateCatalog.ActionLink))
            {
                values[key] = value;
            }
        }

        if (definition.IsSecurity)
        {
            values[NotificationTemplateCatalog.ActionLink] = NotificationTemplateCatalog.SyntheticActionLink;
        }

        return Render(subject, body, values);
    }

    /// <summary>Validates preview sample values: only the event's placeholders, each short and on a single line.</summary>
    public static void ValidateSampleValues(NotificationEventType eventType, IReadOnlyDictionary<string, string>? sampleValues, NotificationErrors errors)
    {
        var definition = NotificationTemplateCatalog.For(eventType);
        foreach (var (key, value) in sampleValues ?? new Dictionary<string, string>())
        {
            if (!definition.Placeholders.Contains(key, StringComparer.Ordinal))
            {
                errors.Add("sampleValues", $"\"{Truncate(key)}\" is not a placeholder of this message.");
            }
            else if (value.Length > MaxSampleValueLength || value.Any(char.IsControl))
            {
                errors.Add("sampleValues", $"The sample value for {key} must be a single line of at most {MaxSampleValueLength} characters.");
            }
        }
    }

    private static void CheckText(string field, string text, NotificationEventDefinition definition, NotificationErrors errors)
    {
        if (text.Length == 0)
        {
            return;
        }

        if (MarkupPattern().IsMatch(text))
        {
            errors.Add(field, "HTML and other markup are not allowed. Use plain text.");
        }

        if (text.Contains("{%", StringComparison.Ordinal) || text.Contains("%}", StringComparison.Ordinal)
            || text.Contains("<%", StringComparison.Ordinal) || text.Contains("${", StringComparison.Ordinal))
        {
            errors.Add(field, "Expressions and scripting syntax are not allowed.");
        }

        foreach (var name in PlaceholdersIn(text).Where(n => !definition.Placeholders.Contains(n, StringComparer.Ordinal)))
        {
            errors.Add(field, $"\"{{{{{Truncate(name)}}}}}\" is not a supported placeholder for this message. Supported: {Supported(definition)}.");
        }

        // Anything left over after removing well-formed placeholders is an unfinished or malformed one.
        var remainder = PlaceholderPattern().Replace(text, string.Empty);
        if (remainder.Contains("{{", StringComparison.Ordinal) || remainder.Contains("}}", StringComparison.Ordinal))
        {
            errors.Add(field, "A placeholder is not written correctly. Use {{name}} with a supported name.");
        }
    }

    private static string Supported(NotificationEventDefinition definition) => string.Join(", ", definition.Placeholders.Select(p => $"{{{{{p}}}}}"));

    private static string Truncate(string value) => value.Length <= 40 ? value : value[..40] + "...";

    private static string Fill(string text, IReadOnlyDictionary<string, string> values, bool singleLine) =>
        PlaceholderPattern().Replace(text, match =>
        {
            var value = values.GetValueOrDefault(match.Groups[1].Value) ?? string.Empty;
            return singleLine ? value.Replace("\r", " ").Replace("\n", " ") : value;
        });
}
