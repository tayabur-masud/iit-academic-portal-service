namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// One message to one recipient. <paramref name="Body"/> is the plain-text message and is always sent; <paramref name="HtmlBody"/>, when present,
/// is the branded HTML version sent as its alternative. Neither is ever stored for a security message.
/// </summary>
public sealed record EmailMessage(string To, string Subject, string Body, string? HtmlBody = null);

/// <summary>
/// The provider-neutral email boundary. Swapping the provider (Brevo in Development, an approved replacement in Production)
/// means swapping the adapter behind this interface; no notification producer changes.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Submits one message to one recipient.
    /// </summary>
    /// <exception cref="EmailDeliveryException">
    /// The provider outcome is known to be a retryable failure, a permanent failure, or ambiguous. Any other exception is treated
    /// as retryable.
    /// </exception>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>How the dispatcher should treat a failed submission.</summary>
public enum EmailFailureKind
{
    /// <summary>Worth trying again (provider outage, rate limit, network failure before the request was sent).</summary>
    Retryable,

    /// <summary>Will not succeed by retrying (rejected recipient, rejected request, recipient not allowed).</summary>
    Permanent,

    /// <summary>The provider may or may not have accepted the message (for example a timeout after sending). Never retried automatically.</summary>
    Uncertain,
}

/// <summary>
/// A classified delivery failure. <see cref="Code"/> is a short, safe classification (for example <c>Http503</c>): adapters must
/// never put a recipient, message content, a credential, or a raw provider error in it.
/// </summary>
public sealed class EmailDeliveryException(EmailFailureKind kind, string code, Exception? inner = null)
    : Exception($"Email delivery failed ({code}).", inner)
{
    public EmailFailureKind Kind { get; } = kind;

    public string Code { get; } = code;
}
