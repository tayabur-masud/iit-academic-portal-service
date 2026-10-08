using System.Net;
using System.Net.Mail;
using IitAcademicPortal.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Email;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string FromAddress { get; set; } = "no-reply@iit-academic-portal.local";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    public string? UserName { get; set; }

    public string? Password { get; set; }

    /// <summary>Development sink: when set, messages are written as .eml files here instead of sent.</summary>
    public string? PickupDirectory { get; set; }
}

public sealed class SmtpEmailSender(IOptions<SmtpOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        using var client = new SmtpClient();

        if (!string.IsNullOrWhiteSpace(settings.PickupDirectory))
        {
            Directory.CreateDirectory(settings.PickupDirectory);
            client.DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory;
            client.PickupDirectoryLocation = Path.GetFullPath(settings.PickupDirectory);
        }
        else
        {
            client.Host = settings.Host ?? throw new InvalidOperationException("Smtp:Host is not configured.");
            client.Port = settings.Port;
            client.EnableSsl = settings.EnableSsl;
            if (!string.IsNullOrEmpty(settings.UserName))
            {
                client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
            }
        }

        using var mail = new MailMessage(settings.FromAddress, message.To, message.Subject, message.Body);
        await client.SendMailAsync(mail, cancellationToken);
    }
}
