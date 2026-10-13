using System.Text.RegularExpressions;

namespace IitAcademicPortal.Domain.Configuration;

/// <summary>
/// The one rule for comparing configuration names and codes. Names are trimmed, repeated whitespace collapses to
/// one space, and comparison uses invariant upper case. Accents are not folded, and the value the Admin typed
/// (trimmed) is what stays on screen.
/// </summary>
public static partial class NameNormalizer
{
    public const int MaxNameLength = 200;
    public const int MaxCodeLength = 50;
    public const int MaxAddressLength = 500;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The text as entered, trimmed. Null becomes empty.</summary>
    public static string Display(string? value) => (value ?? string.Empty).Trim();

    /// <summary>The comparison form of a name: trimmed, single spaces, invariant upper case.</summary>
    public static string Normalize(string? value) => Whitespace().Replace(Display(value), " ").ToUpperInvariant();

    /// <summary>The display form of an optional code; whitespace-only is absent.</summary>
    public static string? DisplayCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : code.Trim();

    /// <summary>The comparison form of an optional code; whitespace-only is absent (null).</summary>
    public static string? NormalizeCode(string? code) => string.IsNullOrWhiteSpace(code) ? null : Normalize(code);
}
