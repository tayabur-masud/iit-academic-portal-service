using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Email;

/// <summary>
/// The email channel, the only one the portal delivers through. It builds the IIT-branded HTML from the plain text in memory (never stored)
/// and hands both to whichever provider sits behind <see cref="IEmailSender"/>.
/// </summary>
public sealed class EmailDeliveryChannel(IEmailSender sender, IOptions<NotificationOptions> options) : IDeliveryChannel
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(ChannelMessage message, CancellationToken cancellationToken) =>
        sender.SendAsync(
            new EmailMessage(message.Destination, message.Subject, message.Text, EmailLayout.Render(message.Subject, message.Text, options.Value.Branding)),
            cancellationToken);
}
