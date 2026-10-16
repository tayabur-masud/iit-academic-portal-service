using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>A template as an Admin sees it: its current wording, whether it is enabled, and the rules for editing it.</summary>
public sealed record TemplateView(
    NotificationEventType EventType,
    string Name,
    NotificationChannel Channel,
    bool IsSecurity,
    bool Enabled,
    int RevisionNumber,
    string Subject,
    string Body,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> RequiredPlaceholders,
    DateTime UpdatedAtUtc);

public sealed record TemplateUpdate(int? ExpectedRevision, string? Subject, string? Body, bool? Enabled);

/// <summary>A preview: the plain text, and the same message in the branded HTML layout. Both use sample data only.</summary>
public sealed record TemplatePreview(string Subject, string Body, string Html, bool Synthetic);

/// <summary>
/// Template list, view, edit, and preview for Admins. Every save that changes wording creates a new immutable revision, so a
/// notification already accepted keeps the wording it was accepted with. Authorization is the caller's job.
/// </summary>
public sealed class NotificationTemplateService(
    INotificationAdminStore store,
    NotificationTemplateRenderer renderer,
    IAuditEventRecorder recorder,
    IOptions<NotificationOptions> options)
{
    public async Task<IReadOnlyList<TemplateView>> ListAsync(CancellationToken cancellationToken) =>
        (await store.ListTemplatesAsync(cancellationToken))
            .Select(ToView)
            .OrderBy(v => (int)v.EventType)
            .ToList();

    public async Task<NotificationResult<TemplateView>> GetAsync(NotificationEventType eventType, CancellationToken cancellationToken) =>
        await store.FindTemplateAsync(eventType, cancellationToken) is { } found
            ? new NotificationResult<TemplateView>(NotificationOutcome.Ok, ToView(found))
            : NotificationResult<TemplateView>.NotFound;

    public async Task<NotificationResult<TemplateView>> UpdateAsync(
        NotificationEventType eventType, TemplateUpdate update, CancellationToken cancellationToken)
    {
        var errors = new NotificationErrors();
        if (update.ExpectedRevision is null)
        {
            errors.Add("expectedRevision", "Send the revision you were editing.");
        }

        if (update.Enabled is null)
        {
            errors.Add("enabled", "Say whether the template is enabled.");
        }
        else if (!update.Enabled.Value && eventType.IsSecurity())
        {
            errors.Add("enabled", "Invitation, email confirmation, and password reset messages must stay enabled.");
        }

        foreach (var (field, message) in renderer.Validate(eventType, update.Subject, update.Body).ToDictionary())
        {
            foreach (var text in message)
            {
                errors.Add(field, text);
            }
        }

        if (errors.Any)
        {
            return NotificationResult<TemplateView>.Invalid(errors);
        }

        var template = await store.FindTrackedTemplateAsync(eventType, cancellationToken);
        var current = await store.FindTemplateAsync(eventType, cancellationToken);
        if (template is null || current is null)
        {
            return NotificationResult<TemplateView>.NotFound;
        }

        if (update.ExpectedRevision != current.Template.CurrentRevisionNumber)
        {
            return NotificationResult<TemplateView>.Conflict(
                "StaleRevision", "The template was changed by someone else. Reload it and apply your edit again.");
        }

        var subject = update.Subject!.Trim();
        var body = update.Body!;
        var enabled = update.Enabled!.Value;
        var wordingChanged = subject != current.Revision.Subject || body != current.Revision.Body;
        var enabledChanged = enabled != current.Template.Enabled;
        if (!wordingChanged && !enabledChanged)
        {
            return new NotificationResult<TemplateView>(NotificationOutcome.Ok, ToView(current));
        }

        var changes = new List<AuditFieldChange>();
        var revision = current.Revision;
        if (wordingChanged)
        {
            revision = NotificationTemplateRevision.Create(
                template.Id, current.Template.CurrentRevisionNumber + 1, subject, body, NotificationTemplateCatalog.For(eventType).Placeholders);
            store.AddRevision(revision);
            template.Publish(revision, enabled);
            changes.Add(new AuditFieldChange("revision", current.Template.CurrentRevisionNumber, revision.RevisionNumber));
        }
        else
        {
            template.Publish(current.Revision, enabled);
        }

        if (enabledChanged)
        {
            changes.Add(new AuditFieldChange("enabled", current.Template.Enabled, enabled));
        }

        recorder.Stage(new AuditEventRequest(NotificationAuditEventDefinitions.TemplateUpdated, AuditOutcome.Success)
        {
            EntityType = "NotificationTemplate",
            EntityId = template.Id.ToString(),
            Metadata = new Dictionary<string, object?> { ["event"] = eventType.ToString(), ["channel"] = NotificationChannel.Email.ToString() },
            Changes = changes,
        });

        try
        {
            await store.SaveAsync(cancellationToken);
        }
        catch (NotificationConflictException)
        {
            // Another Admin saved the same next revision between the check and the write.
            return NotificationResult<TemplateView>.Conflict(
                "StaleRevision", "The template was changed by someone else. Reload it and apply your edit again.");
        }

        return await GetAsync(eventType, cancellationToken);
    }

    /// <summary>
    /// Renders draft wording with synthetic values. It sends nothing, resolves no account, generates no proof, returns no live link,
    /// and stores nothing: a security preview shows only the fixed, nonfunctional link placeholder.
    /// </summary>
    public NotificationResult<TemplatePreview> Preview(
        NotificationEventType eventType, string? subject, string? body, IReadOnlyDictionary<string, string>? sampleValues)
    {
        var errors = renderer.Validate(eventType, subject, body);
        NotificationTemplateRenderer.ValidateSampleValues(eventType, sampleValues, errors);
        if (errors.Any)
        {
            return NotificationResult<TemplatePreview>.Invalid(errors);
        }

        var rendered = renderer.Preview(eventType, subject!.Trim(), body!, sampleValues);
        return new NotificationResult<TemplatePreview>(
            NotificationOutcome.Ok,
            new TemplatePreview(rendered.Subject, rendered.Body, EmailLayout.Render(rendered.Subject, rendered.Body, options.Value.Branding), Synthetic: true));
    }

    private static TemplateView ToView(TemplateWithRevision found)
    {
        var definition = NotificationTemplateCatalog.For(found.Template.EventType);
        return new TemplateView(
            found.Template.EventType,
            definition.DisplayName,
            found.Template.Channel,
            definition.IsSecurity,
            found.Template.Enabled,
            found.Template.CurrentRevisionNumber,
            found.Revision.Subject,
            found.Revision.Body,
            definition.Placeholders,
            definition.Required,
            found.Template.LastModifiedAtUtc);
    }
}
