using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>Drains durable notification work. State lives in the database, so a restart loses nothing.</summary>
public sealed class NotificationDispatchWorker(
    NotificationDispatchProcessor processor,
    IOptions<NotificationDispatchOptions> options,
    ILogger<NotificationDispatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.WorkerEnabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await processor.ProcessOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The exception type only: messages from the database driver can echo values.
                logger.LogError("A notification pass failed ({ExceptionType}); it will be retried", ex.GetType().Name);
            }

            if (handled < options.Value.BatchSize)
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
