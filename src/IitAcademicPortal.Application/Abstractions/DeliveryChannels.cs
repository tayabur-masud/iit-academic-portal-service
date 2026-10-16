using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Abstractions;

/// <summary>What every channel is given: where to send it, the subject or title, and the plain-text message.</summary>
public sealed record ChannelMessage(string Destination, string Subject, string Text);

/// <summary>
/// One way of delivering a notification. A producer never names a channel: the request service creates one notification per registered
/// channel that has an enabled template for the event, and the dispatcher hands each to its own channel. A channel owns whatever is specific
/// to it (the email channel builds the branded HTML and talks to <see cref="IEmailSender"/>). Failures are reported with
/// <see cref="EmailDeliveryException"/>, the classified delivery failure shared by every channel.
/// </summary>
public interface IDeliveryChannel
{
    NotificationChannel Channel { get; }

    /// <exception cref="EmailDeliveryException">The outcome is a retryable failure, a permanent failure, or ambiguous.</exception>
    Task SendAsync(ChannelMessage message, CancellationToken cancellationToken);
}
