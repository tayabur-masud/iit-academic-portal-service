using System.Collections.Concurrent;
using IitAcademicPortal.Application.Abstractions;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>A durable-sink double that records envelopes and can be told to fail.</summary>
public sealed class CapturingSecurityEventSink : IDurableSecurityEventSink
{
    private readonly ConcurrentQueue<string> envelopes = new();

    public bool Fail { get; set; }

    public IReadOnlyCollection<string> Envelopes => envelopes.ToArray();

    public Task WriteAsync(string envelopeJson, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new IOException("The durable sink is unavailable.");
        }

        envelopes.Enqueue(envelopeJson);
        return Task.CompletedTask;
    }
}
