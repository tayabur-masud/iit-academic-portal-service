using System.Collections.Concurrent;
using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Development email sink for tests: records messages instead of sending them.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => messages.ToArray();

    /// <summary>Lets a waiting test drive the notification worker, which does not run in the background under test.</summary>
    public Func<Task>? Pump { get; set; }

    /// <summary>
    /// A scripted provider outcome. Return null to accept the message, or throw: an <see cref="EmailDeliveryException"/> for a
    /// classified failure, or any other exception for an unclassified (retryable) one. The message is recorded only when accepted.
    /// </summary>
    public Func<EmailMessage, Exception?>? Behavior { get; set; }

    /// <summary>Every send attempt, accepted or not, so tests can prove how many provider calls were made.</summary>
    public int Attempts => Volatile.Read(ref attempts);

    private int attempts;

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref attempts);
        if (Behavior?.Invoke(message) is { } failure)
        {
            throw failure;
        }

        messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public async Task<EmailMessage> WaitForAsync(string recipient)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (Pump is not null)
            {
                await Pump();
            }

            var message = messages.FirstOrDefault(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase));
            if (message is not null)
            {
                return message;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("No recovery email was delivered to the expected recipient.");
    }

    public IEnumerable<EmailMessage> SentTo(string recipient) =>
        messages.Where(m => string.Equals(m.To, recipient, StringComparison.OrdinalIgnoreCase));
}
