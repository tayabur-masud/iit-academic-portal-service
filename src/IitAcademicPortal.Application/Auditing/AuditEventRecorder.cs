using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Application.Auditing;

public sealed class AuditEventRecorder(
    IAuditEventStore store,
    ISecurityAuditOutboxStore outbox,
    IDurableSecurityEventSink fallbackSink,
    IAuditRequestContext context,
    TimeProvider timeProvider,
    AuditMetrics metrics,
    ILogger<AuditEventRecorder> logger) : IAuditEventRecorder
{
    // Security events must be written even if the client disconnects, so they do not use the request token.
    private static readonly TimeSpan SecurityWriteTimeout = TimeSpan.FromSeconds(5);

    public void Stage(AuditEventRequest request)
    {
        if (request.Definition.Category != AuditEventCategory.Business)
        {
            throw new AuditValidationException("Only business events are staged in a unit of work; use RecordSecurityAsync for security events.");
        }

        store.Add(AuditEventFactory.Create(request, context, timeProvider.GetUtcNow().UtcDateTime, strict: true));
    }

    public async Task RecordSecurityAsync(AuditEventRequest request)
    {
        // Nothing here may throw: the primary authentication or authorization outcome never depends on it.
        string? envelope = null;
        try
        {
            if (request.Definition.Category != AuditEventCategory.Security)
            {
                throw new AuditValidationException("Only security events use the security-event path.");
            }

            var auditEvent = AuditEventFactory.Create(request, context, timeProvider.GetUtcNow().UtcDateTime, strict: false);
            envelope = AuditEnvelope.From(auditEvent).ToJson();

            using var timeout = new CancellationTokenSource(SecurityWriteTimeout);
            await outbox.EnqueueAsync(auditEvent.Id, envelope, auditEvent.OccurredAtUtc, timeout.Token);
        }
        catch (Exception ex)
        {
            await HandleFailureAsync(envelope, ex);
        }
    }

    private async Task HandleFailureAsync(string? envelope, Exception cause)
    {
        metrics.OutboxWriteFailed();

        // Log the exception type only: messages from drivers can echo values.
        logger.LogCritical("A security audit event could not be written to the outbox ({ExceptionType})", cause.GetType().Name);

        if (envelope is null)
        {
            // The event could not even be built, so there is no envelope to retain.
            metrics.EventLossRisked();
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(SecurityWriteTimeout);
            await fallbackSink.WriteAsync(envelope, timeout.Token);
        }
        catch (Exception sinkCause)
        {
            metrics.FallbackFailed();
            metrics.EventLossRisked();
            logger.LogCritical("A security audit event could not be retained by the durable fallback sink ({ExceptionType})", sinkCause.GetType().Name);
        }
    }
}
