using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>
/// One pass over the due outbox items: claim, deliver to the formal store, and on failure retry with
/// bounded backoff until the automatic limit, after which the item is retained for authorized recovery.
/// </summary>
public sealed class SecurityAuditOutboxProcessor(
    ISecurityAuditOutboxStore store,
    IOptions<AuditDeliveryOptions> options,
    TimeProvider timeProvider,
    AuditMetrics metrics,
    ILogger<SecurityAuditOutboxProcessor> logger)
{
    /// <returns>The number of items claimed in this pass.</returns>
    public async Task<int> ProcessOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var claimed = await store.ClaimDueAsync(now, settings.LeaseDuration, settings.BatchSize, cancellationToken);

        foreach (var item in claimed)
        {
            // The claim already counted this attempt in the stored row; the loaded item holds the count before it.
            var attempt = item.AttemptCount + 1;
            try
            {
                await store.DeliverAsync(item, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var failedAt = timeProvider.GetUtcNow().UtcDateTime;
                var exhausted = attempt >= settings.MaxAttempts;
                DateTime? nextAttempt = exhausted ? null : failedAt + Backoff(settings, attempt);

                await store.RecordFailureAsync(item.EventId, failedAt, ex.GetType().Name, nextAttempt, exhausted, cancellationToken);

                if (exhausted)
                {
                    metrics.RetryExhausted();
                    logger.LogCritical(
                        "Security audit event {EventId} used every automatic delivery attempt and awaits authorized recovery ({FailureCode})",
                        item.EventId, ex.GetType().Name);
                }
                else
                {
                    logger.LogWarning(
                        "Delivery of security audit event {EventId} failed on attempt {Attempt} ({FailureCode}); a retry is scheduled",
                        item.EventId, attempt, ex.GetType().Name);
                }
            }
        }

        return claimed.Count;
    }

    private static TimeSpan Backoff(AuditDeliveryOptions settings, int attempt)
    {
        var factor = Math.Pow(2, Math.Max(0, attempt - 1));
        var delay = TimeSpan.FromTicks((long)Math.Min(settings.MaxRetryDelay.Ticks, settings.BaseRetryDelay.Ticks * factor));
        return delay < settings.BaseRetryDelay ? settings.BaseRetryDelay : delay;
    }
}
