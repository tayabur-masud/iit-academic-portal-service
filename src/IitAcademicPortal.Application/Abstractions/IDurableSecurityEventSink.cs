namespace IitAcademicPortal.Application.Abstractions;

/// <summary>
/// The separately configured durable sink that retains a safe event envelope when the outbox write itself
/// fails. Console logging is not a durable sink.
/// </summary>
public interface IDurableSecurityEventSink
{
    Task WriteAsync(string envelopeJson, CancellationToken cancellationToken);
}
