namespace IitAcademicPortal.Domain.Notifications;

/// <summary>
/// The stable definition of one event on one channel: which revision is current and whether it is enabled. Wording lives in
/// immutable <see cref="NotificationTemplateRevision"/> rows, so saving never rewrites what an earlier notification used.
/// </summary>
public sealed class NotificationTemplate : AuditedEntity
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public NotificationEventType EventType { get; private set; }

    public NotificationChannel Channel { get; private set; }

    public bool Enabled { get; private set; } = true;

    public Guid? CurrentRevisionId { get; private set; }

    public int CurrentRevisionNumber { get; private set; }

    public static NotificationTemplate Create(NotificationEventType eventType, NotificationChannel channel) =>
        new() { EventType = eventType, Channel = channel };

    public void Publish(NotificationTemplateRevision revision, bool enabled)
    {
        CurrentRevisionId = revision.Id;
        CurrentRevisionNumber = revision.RevisionNumber;
        Enabled = enabled;
    }
}

/// <summary>One saved wording of a template. Immutable: the runtime database role cannot update or delete these rows.</summary>
public sealed class NotificationTemplateRevision : AuditedEntity
{
    public const int MaxSubjectLength = 200;
    public const int MaxBodyLength = 20_000;

    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid TemplateId { get; private set; }

    public int RevisionNumber { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    /// <summary>The placeholder names this revision may use, comma separated, for traceability.</summary>
    public string Placeholders { get; private set; } = string.Empty;

    public static NotificationTemplateRevision Create(Guid templateId, int revisionNumber, string subject, string body, IEnumerable<string> placeholders) =>
        new()
        {
            TemplateId = templateId,
            RevisionNumber = revisionNumber,
            Subject = subject,
            Body = body,
            Placeholders = string.Join(',', placeholders),
        };
}
