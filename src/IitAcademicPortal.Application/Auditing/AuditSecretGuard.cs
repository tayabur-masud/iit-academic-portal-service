using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Defense in depth for audit payloads. The allowlist decides what may be kept; this guard additionally
/// masks anything that looks like a credential, even in an allowlisted field.
/// </summary>
public static partial class AuditSecretGuard
{
    public const string Redacted = "[redacted]";

    private static readonly string[] SensitiveNameFragments =
    [
        "password", "passwd", "secret", "token", "cookie", "handle", "proof", "credential",
        "authorization", "apikey", "api-key", "api_key", "privatekey", "private-key", "private_key", "bearer", "csrf",
    ];

    /// <summary>True when a field or property name suggests a credential.</summary>
    public static bool IsSensitiveName(string? name) =>
        !string.IsNullOrEmpty(name)
        && SensitiveNameFragments.Any(fragment => name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when a string looks like a token, session handle, cryptographic secret, or JWT.</summary>
    public static bool LooksLikeSecret(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 32)
        {
            return false;
        }

        // Identifiers written as hyphenated GUIDs (the default) are 36 characters with hyphens, so none of these
        // patterns match them. A bare 32-digit hex string cannot be told apart from a 128-bit secret, so it is
        // masked: format identifiers with hyphens.
        return HexSecret().IsMatch(value) || Base64UrlSecret().IsMatch(value) || JwtLike().IsMatch(value);
    }

    /// <summary>
    /// Returns a copy of <paramref name="node"/> with credential-like property values and credential-like
    /// strings replaced by <see cref="Redacted"/>.
    /// </summary>
    public static JsonNode? Sanitize(JsonNode? node, string? propertyName = null)
    {
        switch (node)
        {
            case null:
                return null;

            case JsonObject obj:
            {
                var copy = new JsonObject();
                foreach (var (name, child) in obj)
                {
                    copy[name] = IsSensitiveName(name) ? JsonValue.Create(Redacted) : Sanitize(child, name);
                }

                return copy;
            }

            case JsonArray array:
            {
                var copy = new JsonArray();
                foreach (var child in array)
                {
                    copy.Add(Sanitize(child, propertyName));
                }

                return copy;
            }

            case JsonValue value when value.TryGetValue<string>(out var text):
                return LooksLikeSecret(text) ? JsonValue.Create(Redacted) : JsonValue.Create(text);

            default:
                return node.DeepClone();
        }
    }

    [GeneratedRegex("^[A-Fa-f0-9]{32,}$")]
    private static partial Regex HexSecret();

    [GeneratedRegex("^[A-Za-z0-9_-]{43,}$")]
    private static partial Regex Base64UrlSecret();

    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*$")]
    private static partial Regex JwtLike();
}
