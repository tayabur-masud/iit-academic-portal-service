using System.Diagnostics.Metrics;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application.Notifications;

public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>Where messages send recipients to sign in (the <c>portalUrl</c> placeholder).</summary>
    public string PortalUrl { get; set; } = "http://localhost:4200";

    /// <summary>
    /// Absolute https address of the logo shown at the top of every email. Mail clients load it from the internet, so it must be publicly
    /// reachable (a localhost address will not show for real recipients). When not set it is the portal's own logo under
    /// <see cref="PortalUrl"/>.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Whether emails show the logo image. When false the portal name is shown as text and no image address is needed, which is the explicit
    /// way to run in Development, where the portal is http and not reachable from the internet.
    /// </summary>
    public bool ShowLogo { get; set; } = true;

    public string EffectiveLogoUrl => string.IsNullOrWhiteSpace(LogoUrl) ? $"{PortalUrl.TrimEnd('/')}/images/iit-logo.png" : LogoUrl;

    /// <summary>The portal name shown in the email header when there is no logo.</summary>
    public string PortalName { get; set; } = EmailBranding.DefaultPortalName;

    /// <summary>Alternative text of the logo for people and programs that cannot see it.</summary>
    public string LogoAlt { get; set; } = EmailBranding.DefaultLogoAlt;

    /// <summary>The brand color (heading, rule, link button) as a six-digit hex value. It must stay dark enough for white button text.</summary>
    public string BrandColor { get; set; } = EmailBranding.DefaultColor;

    public string FooterText { get; set; } = EmailBranding.DefaultFooter;

    /// <summary>The label on the button that carries a link.</summary>
    public string ButtonLabel { get; set; } = EmailBranding.DefaultButtonLabel;

    /// <summary>All of the above as the branding every email is rendered with.</summary>
    public EmailBranding Branding => new(PortalName, ShowLogo ? EffectiveLogoUrl : null, LogoAlt, BrandColor, FooterText, ButtonLabel);
}

/// <summary>Operational counters for notification delivery, scoped to the instance so test hosts do not mix.</summary>
public sealed class NotificationMetrics : IDisposable
{
    public const string MeterName = "IitAcademicPortal.Notifications";

    private readonly Meter meter;
    private readonly Counter<long> persistenceFailures;
    private readonly Counter<long> retryExhausted;
    private readonly Counter<long> uncertain;
    private readonly Counter<long> sent;

    public NotificationMetrics()
    {
        meter = new Meter(new MeterOptions(MeterName) { Scope = this });
        persistenceFailures = meter.CreateCounter<long>(
            "notifications.persistence_failures", description: "Notification requests that could not be retained and need reconciliation from their source occurrence.");
        retryExhausted = meter.CreateCounter<long>(
            "notifications.retry_exhausted", description: "Notifications that used every automatic attempt and are Failed.");
        uncertain = meter.CreateCounter<long>(
            "notifications.uncertain", description: "Notifications whose provider acceptance could not be established.");
        sent = meter.CreateCounter<long>("notifications.sent", description: "Notifications accepted by the delivery provider.");
    }

    public void PersistenceFailed() => persistenceFailures.Add(1);

    public void RetryExhausted() => retryExhausted.Add(1);

    public void Uncertain() => uncertain.Add(1);

    public void Sent() => sent.Add(1);

    public void Dispose() => meter.Dispose();
}

/// <summary>
/// The operational alarm for lost notification work. It carries only identifiers, never an address, content, a proof, or a
/// provider message, so it is safe for logs and alerting.
/// </summary>
public interface INotificationFailureSignal
{
    /// <summary>Work could not be retained after the owning action committed. The owner can replay <paramref name="occurrenceId"/>.</summary>
    void PersistenceFailed(NotificationEventType eventType, string occurrenceId, int recipientCount, string failureCode);

    /// <summary>A notification used every automatic attempt.</summary>
    void DeliveryExhausted(Guid notificationId, NotificationEventType eventType, string failureCode);
}

public sealed class LoggingNotificationFailureSignal(NotificationMetrics metrics, ILogger<LoggingNotificationFailureSignal> logger) : INotificationFailureSignal
{
    public void PersistenceFailed(NotificationEventType eventType, string occurrenceId, int recipientCount, string failureCode)
    {
        metrics.PersistenceFailed();
        logger.LogCritical(
            "Notification work was not retained for {EventType} occurrence {OccurrenceId} ({RecipientCount} recipients, {FailureCode}); replay the occurrence to reconcile",
            eventType, occurrenceId, recipientCount, failureCode);
    }

    public void DeliveryExhausted(Guid notificationId, NotificationEventType eventType, string failureCode)
    {
        metrics.RetryExhausted();
        logger.LogCritical(
            "Notification {NotificationId} ({EventType}) used every automatic attempt and awaits an Admin resend ({FailureCode})",
            notificationId, eventType, failureCode);
    }
}

/// <summary>
/// The one entry point producers use. It never throws and never fails the originating action: it validates the request, fixes the
/// template revision and destination, snapshots ordinary content, and stores the work durably so a background worker delivers it.
/// Replaying the same source occurrence is safe and creates no extra logical notification, which is also how a producer reconciles
/// after <see cref="NotificationRequestStatus.PersistenceFailed"/>.
/// </summary>
public interface INotificationRequestService
{
    Task<NotificationRequestResult> RequestAsync(NotificationEventRequest request, CancellationToken cancellationToken);
}

public sealed class NotificationRequestService(
    INotificationStore store,
    IRecipientDirectory directory,
    IEnumerable<IDeliveryChannel> channels,
    NotificationTemplateRenderer renderer,
    IOptions<NotificationOptions> options,
    TimeProvider timeProvider,
    INotificationFailureSignal failureSignal,
    ILogger<NotificationRequestService> logger) : INotificationRequestService
{
    public const int MaxRecipients = 10_000;

    public async Task<NotificationRequestResult> RequestAsync(NotificationEventRequest request, CancellationToken cancellationToken)
    {
        var recipients = request.RecipientUserIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList() ?? [];
        var invalid = Validate(request, recipients);
        if (invalid is not null)
        {
            return NotificationRequestResult.Invalid(invalid);
        }

        try
        {
            var templates = await store.GetTemplatesAsync(request.EventType, cancellationToken);
            var active = channels.Select(c => c.Channel).ToHashSet();
            if (active.Contains(NotificationChannel.Email) && templates.All(t => t.Channel != NotificationChannel.Email))
            {
                throw new InvalidOperationException("No email template is seeded for the event.");
            }

            var values = new Dictionary<string, string>(request.Context ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                [NotificationTemplateCatalog.PortalUrl] = options.Value.PortalUrl,
            };
            var people = await directory.FindAsync(recipients, cancellationToken);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var (accepted, duplicates, suppressed, failed) = (0, 0, 0, 0);

            // One notification per recipient for every channel that has a template for the event and an adapter to deliver it. The producer
            // never names a channel, so adding one later changes nothing it sends.
            foreach (var template in templates.Where(t => active.Contains(t.Channel)))
            foreach (var recipientId in recipients)
            {
                var person = people.GetValueOrDefault(recipientId);
                var destination = string.IsNullOrWhiteSpace(person?.Email) ? null : person!.Email;

                string? subject = null;
                string? body = null;
                NotificationStatus status;
                string? failureCode = null;

                if (destination is null)
                {
                    (status, failureCode) = (NotificationStatus.Failed, "NoDestination");
                }
                else if (person!.IsDeactivated)
                {
                    (status, failureCode) = (NotificationStatus.Failed, "RecipientIneligible");
                }
                else if (!template.Enabled && !request.EventType.IsSecurity())
                {
                    status = NotificationStatus.Suppressed;
                }
                else
                {
                    status = NotificationStatus.Pending;
                    if (!request.EventType.IsSecurity())
                    {
                        // Ordinary content is fixed now, from the revision current now, so later edits never change it.
                        var missing = renderer.MissingValues(template.Subject, template.Body, values);
                        if (missing is not null)
                        {
                            return NotificationRequestResult.Invalid($"The request has no value for {{{{{missing}}}}}.");
                        }

                        var rendered = renderer.Render(template.Subject, template.Body, values);
                        (subject, body) = (rendered.Subject, rendered.Body);
                    }
                }

                var record = NotificationRecord.Create(
                    request.EventType, request.OccurrenceId, recipientId, destination, template.RevisionId, now, subject, body,
                    channel: template.Channel);
                var state = NotificationDispatchState.Create(record.Id, status, now, failureCode);
                if (await store.AcceptAsync(record, state, cancellationToken) == AcceptResult.Duplicate)
                {
                    duplicates++;
                }
                else if (status == NotificationStatus.Suppressed)
                {
                    suppressed++;
                }
                else if (status == NotificationStatus.Failed)
                {
                    failed++;
                }
                else
                {
                    accepted++;
                }
            }

            return new NotificationRequestResult(NotificationRequestStatus.Accepted, accepted, duplicates, suppressed, failed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The owning action already committed and must stay successful. Raise the alarm and let the owner replay the occurrence.
            logger.LogError("A notification request failed ({ExceptionType})", ex.GetType().Name);
            failureSignal.PersistenceFailed(request.EventType, request.OccurrenceId, recipients.Count, ex.GetType().Name);
            return NotificationRequestResult.PersistenceFailed;
        }
    }

    private static string? Validate(NotificationEventRequest request, IReadOnlyCollection<string> recipients)
    {
        if (!Enum.IsDefined(request.EventType))
        {
            return "The event type is not supported.";
        }

        if (string.IsNullOrWhiteSpace(request.OccurrenceId) || request.OccurrenceId.Length > NotificationRecord.MaxOccurrenceIdLength)
        {
            return "A stable occurrence identifier of at most 200 characters is required.";
        }

        if (recipients.Count == 0 || recipients.Count > MaxRecipients)
        {
            return "Between 1 and 10,000 recipients are required.";
        }

        var allowed = NotificationTemplateCatalog.For(request.EventType).ProducerContextKeys;
        var unsupported = (request.Context ?? new Dictionary<string, string>()).Keys.FirstOrDefault(k => !allowed.Contains(k, StringComparer.Ordinal));
        return unsupported is null ? null : "The context contains a value this event does not accept.";
    }
}
