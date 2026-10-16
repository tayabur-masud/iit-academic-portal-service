using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>
/// One pass of notification work: resolve retained password-recovery requests into notifications, schedule and issue feedback
/// reminders, then claim due notifications and deliver them. Everything durable lives in the database, so a restart loses nothing;
/// the worker advances only the mutable dispatch state and appends attempt facts.
/// </summary>
/// <remarks>
/// Security content (a reset link and its proof) is generated here for one attempt, rendered in memory, handed to the provider, and
/// dropped. It is never stored, logged, or placed in an attempt. A provider call that may or may not have been accepted becomes
/// Uncertain and is never retried automatically.
/// </remarks>
public sealed class NotificationDispatchProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDispatchOptions> options,
    IOptions<NotificationOptions> notificationOptions,
    TimeProvider timeProvider,
    INotificationFailureSignal failureSignal,
    NotificationMetrics metrics,
    ILogger<NotificationDispatchProcessor> logger)
{
    /// <returns>The number of items handled in this pass; zero means the queue was idle.</returns>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var handled = await ProcessRecoveryRequestsAsync(cancellationToken);
        handled += await ProcessRemindersAsync(cancellationToken);
        handled += await DeliverDueAsync(cancellationToken);
        return handled;
    }

    private async Task<int> ProcessRecoveryRequestsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var expired = await store.DeleteExpiredRecoveryRequestsAsync(now, cancellationToken);
        var claims = await store.ClaimRecoveryRequestsAsync(now, settings.LeaseDuration, settings.BatchSize, settings.MaxAttempts, cancellationToken);
        var protector = scope.ServiceProvider.GetRequiredService<ITransientSecretProtector>();

        foreach (var claim in claims)
        {
            try
            {
                string address;
                try
                {
                    address = protector.Unprotect(claim.ProtectedAddress);
                }
                catch (InvalidOperationException)
                {
                    // Unreadable (a key was lost): it can never be processed, so remove it rather than retry forever.
                    logger.LogError("A retained password recovery request could not be read and was discarded");
                    await store.DeleteRecoveryRequestAsync(claim.Id, cancellationToken);
                    continue;
                }

                await using var itemScope = scopeFactory.CreateAsyncScope();
                var recovery = itemScope.ServiceProvider.GetRequiredService<PasswordRecoveryService>();
                if (await recovery.ProcessRequestAsync(claim.Id, address, cancellationToken))
                {
                    await store.DeleteRecoveryRequestAsync(claim.Id, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The exception type only: messages from Identity or the database can echo addresses. The lease expiry retries it.
                logger.LogError("Password recovery processing failed ({ExceptionType}); it will be retried", ex.GetType().Name);
            }
        }

        return claims.Count + expired;
    }

    private async Task<int> ProcessRemindersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<FeedbackReminderScheduler>().ProcessAsync(cancellationToken);
            return result.Dispatched;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError("Feedback reminder scheduling failed ({ExceptionType}); it will be retried", ex.GetType().Name);
            return 0;
        }
    }

    private async Task<int> DeliverDueAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<INotificationStore>();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var claimed = await store.ClaimDueAsync(now, settings.LeaseDuration, settings.BatchSize, cancellationToken);
        for (var i = 0; i < claimed.Interrupted; i++)
        {
            metrics.Uncertain();
        }

        foreach (var notification in claimed.ToSend)
        {
            await DeliverAsync(scope.ServiceProvider, store, notification, cancellationToken);
        }

        return claimed.ToSend.Count + claimed.Interrupted;
    }

    private async Task DeliverAsync(IServiceProvider services, INotificationStore store, ClaimedNotification notification, CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        var attemptNumber = notification.AttemptCount + 1;

        // 1. The message. Ordinary content was fixed when the work was accepted; security content exists only in this method.
        string subject;
        string body;
        if (string.IsNullOrWhiteSpace(notification.Destination))
        {
            await CompleteAsync(store, notification, attemptNumber, startedAt, DeliveryAttemptOutcome.PermanentFailure, "NoDestination", cancellationToken);
            return;
        }

        if (notification.EventType.IsSecurity())
        {
            var provider = services.GetServices<ISecurityContentProvider>().FirstOrDefault(p => p.EventType == notification.EventType);
            SecurityContentResult content;
            try
            {
                content = provider is null
                    ? SecurityContentResult.NoProvider("OwnerWorkflowUnavailable")
                    : await provider.PrepareAsync(notification.RecipientUserId, notification.Destination, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                await CompleteAsync(store, notification, attemptNumber, startedAt, DeliveryAttemptOutcome.RetryableFailure, "ContentUnavailable", cancellationToken);
                logger.LogWarning("Preparing security content failed for notification {NotificationId} ({ExceptionType})", notification.Id, ex.GetType().Name);
                return;
            }

            if (content.Status != SecurityContentStatus.Ready || content.Values is null)
            {
                await CompleteAsync(
                    store, notification, attemptNumber, startedAt, DeliveryAttemptOutcome.PermanentFailure, Safe(content.Code ?? "NotAuthorized"), cancellationToken);
                return;
            }

            var rendered = services.GetRequiredService<NotificationTemplateRenderer>().Render(notification.RevisionSubject, notification.RevisionBody, content.Values);
            (subject, body) = (rendered.Subject, rendered.Body);
        }
        else if (notification.Subject is { } ordinarySubject && notification.Body is { } ordinaryBody)
        {
            (subject, body) = (ordinarySubject, ordinaryBody);
        }
        else
        {
            await CompleteAsync(store, notification, attemptNumber, startedAt, DeliveryAttemptOutcome.PermanentFailure, "ContentMissing", cancellationToken);
            return;
        }

        // 2. Mark the provider call as started. If this worker dies after this point, acceptance is unknown (Uncertain), not retried.
        if (!await store.BeginSendAsync(notification.Id, timeProvider.GetUtcNow().UtcDateTime, cancellationToken))
        {
            return;
        }

        // 3. The provider call, and what it means.
        var outcome = DeliveryAttemptOutcome.Accepted;
        string? failureCode = null;
        try
        {
            // The branded HTML is built here, in memory, from the plain text; it is never stored. The plain text is sent alongside it.
            var html = EmailLayout.Render(subject, body, notificationOptions.Value.Branding);
            await services.GetRequiredService<IEmailSender>().SendAsync(new EmailMessage(notification.Destination, subject, body, html), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down mid-call: the lease expires and the interrupted-send rule makes it Uncertain.
            throw;
        }
        catch (EmailDeliveryException ex)
        {
            failureCode = Safe(ex.Code);
            outcome = ex.Kind switch
            {
                EmailFailureKind.Permanent => DeliveryAttemptOutcome.PermanentFailure,
                EmailFailureKind.Uncertain => DeliveryAttemptOutcome.Uncertain,
                _ => DeliveryAttemptOutcome.RetryableFailure,
            };
        }
        catch (Exception ex)
        {
            // The exception type only: a provider or SMTP message can echo the address or content.
            failureCode = Safe(ex.GetType().Name);
            outcome = DeliveryAttemptOutcome.RetryableFailure;
        }

        await CompleteAsync(store, notification, attemptNumber, startedAt, outcome, failureCode, cancellationToken);
    }

    private async Task CompleteAsync(
        INotificationStore store, ClaimedNotification notification, int attemptNumber, DateTime startedAt,
        DeliveryAttemptOutcome outcome, string? failureCode, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var completedAt = timeProvider.GetUtcNow().UtcDateTime;
        NotificationStatus status;
        DateTime? next = null;
        var recordedCode = failureCode;

        switch (outcome)
        {
            case DeliveryAttemptOutcome.Accepted:
                status = NotificationStatus.Sent;
                recordedCode = null;
                metrics.Sent();
                break;
            case DeliveryAttemptOutcome.Uncertain:
                status = NotificationStatus.Uncertain;
                metrics.Uncertain();
                logger.LogWarning("Notification {NotificationId} has an uncertain provider outcome ({FailureCode})", notification.Id, failureCode);
                break;
            case DeliveryAttemptOutcome.RetryableFailure when attemptNumber < settings.MaxAttempts:
                status = NotificationStatus.Retrying;
                next = completedAt + Backoff(settings, attemptNumber);
                logger.LogWarning(
                    "Delivery of notification {NotificationId} failed on attempt {Attempt} ({FailureCode}); a retry is scheduled",
                    notification.Id, attemptNumber, failureCode);
                break;
            case DeliveryAttemptOutcome.RetryableFailure:
                status = NotificationStatus.Failed;
                recordedCode = "RetriesExhausted";
                failureSignal.DeliveryExhausted(notification.Id, notification.EventType, failureCode ?? "Unknown");
                break;
            default:
                status = NotificationStatus.Failed;
                break;
        }

        await store.CompleteAttemptAsync(
            new AttemptCompletion(
                notification.Id, attemptNumber, startedAt, completedAt, outcome, failureCode,
                outcome == DeliveryAttemptOutcome.Accepted ? completedAt : null, status, next, recordedCode),
            cancellationToken);
    }

    private static TimeSpan Backoff(NotificationDispatchOptions settings, int attempt)
    {
        var factor = Math.Pow(2, Math.Max(0, attempt - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(settings.MaxRetryDelay.Ticks, settings.BaseRetryDelay.Ticks * factor));
        return delay < settings.BaseRetryDelay ? settings.BaseRetryDelay : delay;
    }

    // Failure codes are classifications, never provider text: letters and digits, bounded.
    private static string Safe(string code)
    {
        var cleaned = new string(code.Where(char.IsLetterOrDigit).Take(NotificationDispatchState.MaxFailureCodeLength).ToArray());
        return cleaned.Length == 0 ? "Unknown" : cleaned;
    }
}
