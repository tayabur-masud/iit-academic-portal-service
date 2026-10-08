namespace IitAcademicPortal.Application.PasswordRecovery;

/// <summary>
/// The approved replacement-password rule: at least 8 characters, including at least one letter and
/// at least one number. No other composition rules apply.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;

    public static IReadOnlyList<string> Validate(string? password)
    {
        password ??= string.Empty;
        var errors = new List<string>();

        if (password.Length < MinimumLength)
        {
            errors.Add($"Use at least {MinimumLength} characters.");
        }

        if (!password.Any(char.IsAsciiLetter))
        {
            errors.Add("Include at least one letter.");
        }

        if (!password.Any(char.IsAsciiDigit))
        {
            errors.Add("Include at least one number.");
        }

        return errors;
    }
}
