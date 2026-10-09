using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>Drains the persisted security-event outbox. State lives in the database, so a restart loses nothing.</summary>
public sealed class SecurityAuditOutboxWorker(
    SecurityAuditOutboxProcessor processor,
    IOptions<AuditDeliveryOptions> options,
    ILogger<SecurityAuditOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var claimed = 0;
            try
            {
                claimed = await processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The exception type only: messages from the database driver can echo values.
                logger.LogError("The security audit outbox pass failed ({ExceptionType}); it will be retried", ex.GetType().Name);
            }

            if (claimed < options.Value.BatchSize)
            {
                try
                {
                    await Task.Delay(options.Value.PollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }
}
