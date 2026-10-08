using System.Threading.Channels;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.PasswordRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Infrastructure.PasswordRecovery;

public sealed class PasswordRecoveryQueue(ILogger<PasswordRecoveryQueue> logger) : IPasswordRecoveryQueue
{
    private readonly Channel<string> channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });

    public ChannelReader<string> Reader => channel.Reader;

    public void Enqueue(string email)
    {
        if (!channel.Writer.TryWrite(email))
        {
            logger.LogWarning("Password recovery queue is full; a recovery request was dropped");
        }
    }
}

/// <summary>Processes recovery requests off the request path so response time does not reveal account existence.</summary>
public sealed class PasswordRecoveryWorker(
    PasswordRecoveryQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordRecoveryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var recovery = scope.ServiceProvider.GetRequiredService<PasswordRecoveryService>();
                await recovery.ProcessRequestAsync(email, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The exception type only: messages from SMTP or Identity may echo addresses.
                logger.LogError("Password recovery processing failed with {ExceptionType}", ex.GetType().Name);
            }
        }
    }
}
