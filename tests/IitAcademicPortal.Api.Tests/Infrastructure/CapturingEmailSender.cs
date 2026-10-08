using System.Collections.Concurrent;
using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Development email sink for tests: records messages instead of sending them.</summary>
public sealed class CapturingEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> messages = new();

    public IReadOnlyCollection<EmailMessage> Messages => messages.ToArray();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        messages.Enqueue(message);
        return Task.CompletedTask;
    }

    public async Task<EmailMessage> WaitForAsync(string recipient)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
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
