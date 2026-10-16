namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by Notification Management. They record what an Admin changed and the stable identifiers involved, never
/// message content, a link or proof, a recipient address, or feedback data. Template wording is not recorded either: the immutable
/// revision it created is the record of it.
/// </summary>
public static class NotificationAuditEventDefinitions
{
    public static readonly string[] ApprovedFields = ["revision", "enabled", "leadTimeMinutes"];

    public static AuditEventDefinition TemplateUpdated { get; } = AuditEventDefinition.Business("notification.template.updated", ApprovedFields);

    public static AuditEventDefinition ResendRequested { get; } = AuditEventDefinition.Business("notification.resend.requested", ApprovedFields);

    public static AuditEventDefinition ReminderPolicyUpdated { get; } = AuditEventDefinition.Business("notification.reminder-policy.updated", ApprovedFields);
}
