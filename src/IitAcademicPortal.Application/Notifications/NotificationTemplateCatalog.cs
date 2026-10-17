using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// The fixed definition of one supported event: the placeholders its wording may use, the ones it must use, what a producer may
/// supply, the initial wording seeded by the migration, and the synthetic values a preview uses. Admins edit wording only;
/// recipient, privacy classification, and placeholders are not editable.
/// </summary>
public sealed record NotificationEventDefinition(
    NotificationEventType EventType,
    string DisplayName,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> Required,
    IReadOnlyList<string> ProducerContextKeys,
    string DefaultSubject,
    string DefaultBody,
    IReadOnlyDictionary<string, string> SampleValues)
{
    public bool IsSecurity => EventType.IsSecurity();
}

/// <summary>The eight supported events, their initial wording, and the placeholder rules.</summary>
public static class NotificationTemplateCatalog
{
    public const string ActionLink = "actionLink";
    public const string ExpiresIn = "expiresIn";
    public const string PortalUrl = "portalUrl";

    /// <summary>What a security preview shows instead of a link: visibly synthetic and not functional.</summary>
    public const string SyntheticActionLink = "[SAMPLE LINK - not functional]";

    private static readonly IReadOnlyDictionary<string, string> Empty = new Dictionary<string, string>();

    public static IReadOnlyList<NotificationEventDefinition> All { get; } =
    [
        Security(
            NotificationEventType.TeacherInvitation,
            "Teacher invitation",
            "You are invited to the IIT Academic Portal",
            "You have been added as a teacher on the IIT Academic Portal.\n\n" +
            "Use this link to set up your account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\n" +
            "If you were not expecting this invitation, you can ignore this email."),
        Security(
            NotificationEventType.StudentInvitation,
            "Student invitation",
            "You are invited to the IIT Academic Portal",
            "You have been added as a student on the IIT Academic Portal.\n\n" +
            "Use this link to set up your account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\n" +
            "If you were not expecting this invitation, you can ignore this email."),
        Security(
            NotificationEventType.EmailConfirmation,
            "Email confirmation",
            "Confirm your email address for the IIT Academic Portal",
            "Please confirm that this is your email address for the IIT Academic Portal.\n\n" +
            "Use this link to confirm it. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\n" +
            "If you did not ask for this, you can ignore this email."),
        Security(
            NotificationEventType.PasswordReset,
            "Password reset",
            "Reset your IIT Academic Portal password",
            "A password reset was requested for your IIT Academic Portal account.\n\n" +
            "Use this link to choose a new password. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\n" +
            "If you did not request this, you can ignore this email. Your password will not change."),
        Ordinary(
            NotificationEventType.ResultsPublished,
            "Results published",
            ["courseName", "termName", PortalUrl],
            ["courseName", "termName"],
            "Results published for {{courseName}}",
            "Results for {{courseName}} ({{termName}}) have been published.\n\n" +
            "Sign in to the IIT Academic Portal to see your results:\n\n{{portalUrl}}",
            new Dictionary<string, string> { ["courseName"] = "Sample Course", ["termName"] = "Sample Term", [PortalUrl] = "https://portal.example.invalid" }),
        Ordinary(
            NotificationEventType.FeedbackWindowOpened,
            "Feedback window opened",
            ["courseName", "windowName", "closesAtUtc", PortalUrl],
            ["courseName", "windowName", "closesAtUtc"],
            "Feedback is open for {{courseName}}",
            "The feedback window \"{{windowName}}\" for {{courseName}} is now open and closes at {{closesAtUtc}}.\n\n" +
            "Sign in to the IIT Academic Portal to give your feedback:\n\n{{portalUrl}}",
            new Dictionary<string, string>
            {
                ["courseName"] = "Sample Course", ["windowName"] = "Sample Window", ["closesAtUtc"] = "2030-01-01 12:00 UTC", [PortalUrl] = "https://portal.example.invalid",
            }),
        Ordinary(
            NotificationEventType.FeedbackClosingReminder,
            "Feedback closing reminder",
            ["courseName", "windowName", "closesAtUtc", PortalUrl],
            ["courseName", "windowName", "closesAtUtc"],
            "Feedback for {{courseName}} closes soon",
            "The feedback window \"{{windowName}}\" for {{courseName}} closes at {{closesAtUtc}}.\n\n" +
            "Sign in to the IIT Academic Portal to give your feedback:\n\n{{portalUrl}}",
            new Dictionary<string, string>
            {
                ["courseName"] = "Sample Course", ["windowName"] = "Sample Window", ["closesAtUtc"] = "2030-01-01 12:00 UTC", [PortalUrl] = "https://portal.example.invalid",
            }),
        Security(
            NotificationEventType.AccountCredentialSetup,
            "Account setup",
            "Set up your IIT Academic Portal account",
            "An account has been created for you on the IIT Academic Portal.\n\n" +
            "Use this link to choose your password and activate the account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\n" +
            "If you were not expecting this, you can ignore this email. The account stays inactive until a password is set."),
    ];

    public static NotificationEventDefinition For(NotificationEventType eventType) => All.Single(d => d.EventType == eventType);

    private static NotificationEventDefinition Security(NotificationEventType eventType, string name, string subject, string body) =>
        new(
            eventType,
            name,
            [ActionLink, ExpiresIn],
            [ActionLink],
            [],
            subject,
            body,
            new Dictionary<string, string> { [ActionLink] = SyntheticActionLink, [ExpiresIn] = "1 hour" });

    private static NotificationEventDefinition Ordinary(
        NotificationEventType eventType, string name, string[] placeholders, string[] contextKeys, string subject, string body,
        IReadOnlyDictionary<string, string> samples) =>
        new(eventType, name, placeholders, [], contextKeys, subject, body, samples);

    /// <summary>Deterministic identifiers so the seed is the same in every database.</summary>
    public static Guid TemplateId(NotificationEventType eventType) => new($"7a110000-0000-4000-8000-{(int)eventType + 1:D12}");

    public static Guid InitialRevisionId(NotificationEventType eventType) => new($"7a120000-0000-4000-8000-{(int)eventType + 1:D12}");

    internal static IReadOnlyDictionary<string, string> NoSamples => Empty;
}
