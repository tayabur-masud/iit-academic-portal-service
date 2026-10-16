using System.Text.RegularExpressions;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// Re-sends a failed notification as a new, linked execution that runs in the background. The failed record and its attempts are
/// never changed. Only a Failed record qualifies, once at a time, and only while the recipient is still eligible and still has the
/// address the message was addressed to. A security message revalidates the owning workflow and gets a fresh link when it is
/// delivered; nothing is replayed from history. Authorization is the caller's job.
/// </summary>
public sealed partial class NotificationResendService(
    INotificationAdminStore store,
    IRecipientDirectory directory,
    IEnumerable<ISecurityContentProvider> securityProviders,
    IAuditEventRecorder recorder,
    TimeProvider timeProvider)
{
    [GeneratedRegex(@"^[A-Za-z0-9._:\-]{1,128}$")]
    private static partial Regex KeyPattern();

    public async Task<NotificationResult<NotificationDetail>> ResendAsync(Guid notificationId, string? idempotencyKey, CancellationToken cancellationToken)
    {
        var errors = new NotificationErrors();
        if (string.IsNullOrWhiteSpace(idempotencyKey) || !KeyPattern().IsMatch(idempotencyKey.Trim()))
        {
            errors.Add("idempotencyKey", "Send an Idempotency-Key header of 1 to 128 letters, digits, or . _ : - characters.");
            return NotificationResult<NotificationDetail>.Invalid(errors);
        }

        var key = idempotencyKey.Trim();
        var predecessor = await store.FindRecordAsync(notificationId, cancellationToken);
        if (predecessor is null)
        {
            return NotificationResult<NotificationDetail>.NotFound;
        }

        // The same confirmation repeated (a retry, a double click) returns the execution it already created.
        if (await store.FindResendAsync(notificationId, key, cancellationToken) is { } existing)
        {
            return await DetailAsync(existing.Id, cancellationToken);
        }

        var status = await store.GetStatusAsync(notificationId, cancellationToken);
        if (status != NotificationStatus.Failed)
        {
            return Refuse(status switch
            {
                NotificationStatus.Pending or NotificationStatus.Retrying => ("InProgress", "This notification is still being delivered."),
                NotificationStatus.Sent => ("AlreadySent", "This notification was already accepted by the delivery service."),
                NotificationStatus.Suppressed => ("Suppressed", "This notification was suppressed because its template was disabled. Enabling the template does not replay it."),
                NotificationStatus.Uncertain => ("DeliveryUncertain", "Whether this notification was delivered cannot be determined, so it cannot be resent."),
                _ => ("NotResendable", "This notification cannot be resent."),
            });
        }

        var children = await store.GetResendStatusesAsync(notificationId, cancellationToken);
        if (children.Any(s => s == NotificationStatus.Sent))
        {
            return Refuse(("AlreadyResent", "A resend of this notification was already accepted by the delivery service."));
        }

        if (children.Any(s => s != NotificationStatus.Failed))
        {
            return Refuse(("ResendInProgress", "A resend of this notification is already in progress."));
        }

        var refusal = await CheckRecipientAsync(predecessor, cancellationToken);
        if (refusal is not null)
        {
            return Refuse(refusal.Value);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var record = NotificationRecord.Create(
            predecessor.EventType, predecessor.OccurrenceId, predecessor.RecipientUserId, predecessor.Destination,
            predecessor.TemplateRevisionId, now, predecessor.Subject, predecessor.Body, predecessor.Id, key);
        store.AddNotification(record, NotificationDispatchState.Create(record.Id, NotificationStatus.Pending, now));
        recorder.Stage(new AuditEventRequest(NotificationAuditEventDefinitions.ResendRequested, AuditOutcome.Success)
        {
            EntityType = "Notification",
            EntityId = record.Id.ToString(),
            Metadata = new Dictionary<string, object?>
            {
                ["event"] = predecessor.EventType.ToString(),
                ["predecessorId"] = predecessor.Id.ToString(),
                ["recipientUserId"] = predecessor.RecipientUserId,
            },
        });

        try
        {
            await store.SaveAsync(cancellationToken);
        }
        catch (NotificationConflictException)
        {
            // A concurrent request with the same key won: return its execution rather than creating a second.
            if (await store.FindResendAsync(notificationId, key, cancellationToken) is { } winner)
            {
                return await DetailAsync(winner.Id, cancellationToken);
            }

            return NotificationResult<NotificationDetail>.Conflict("ResendInProgress", "A resend of this notification is already in progress.");
        }

        return await DetailAsync(record.Id, cancellationToken);
    }

    private async Task<(string Code, string Message)?> CheckRecipientAsync(NotificationRecord predecessor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(predecessor.Destination))
        {
            return ("NoDestination", "The recipient had no email address when this was requested, so there is nothing to resend to.");
        }

        var people = await directory.FindAsync([predecessor.RecipientUserId], cancellationToken);
        if (!people.TryGetValue(predecessor.RecipientUserId, out var person) || person.IsDeactivated)
        {
            return ("RecipientIneligible", "The recipient account no longer exists or is deactivated.");
        }

        if (!string.Equals(person.Email, predecessor.Destination, StringComparison.OrdinalIgnoreCase))
        {
            return ("DestinationChanged", "The address no longer belongs to the intended recipient. Resend from the owning workflow instead.");
        }

        if (!predecessor.EventType.IsSecurity())
        {
            return null;
        }

        var provider = securityProviders.FirstOrDefault(p => p.EventType == predecessor.EventType);
        var check = provider is null
            ? SecurityContentResult.NoProvider("OwnerWorkflowUnavailable")
            : await provider.CheckAsync(predecessor.RecipientUserId, predecessor.Destination, cancellationToken);
        return check.Status switch
        {
            SecurityContentStatus.Ready => null,
            SecurityContentStatus.Ineligible => (check.Code ?? "RecipientIneligible", "The owning workflow no longer authorizes this message. Resend it from there."),
            _ => ("OwnerWorkflowUnavailable", "A fresh secure link cannot be produced yet. Use the owning account workflow instead."),
        };
    }

    private static NotificationResult<NotificationDetail> Refuse((string Code, string Message) reason) =>
        NotificationResult<NotificationDetail>.Conflict(reason.Code, reason.Message);

    private async Task<NotificationResult<NotificationDetail>> DetailAsync(Guid id, CancellationToken cancellationToken) =>
        new(NotificationOutcome.Accepted, await store.GetDetailAsync(id, cancellationToken));
}
