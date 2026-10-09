using System.Text;
using IitAcademicPortal.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>
/// Appends each safe event envelope as one JSON line to a daily file and flushes it to disk, so the event
/// survives a process crash. The envelope is already redacted before it reaches the sink.
/// </summary>
public sealed class FileSecurityEventSink(
    IOptions<AuditFallbackOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider) : IDurableSecurityEventSink
{
    private const string DevelopmentFolder = ".audit-fallback";

    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task WriteAsync(string envelopeJson, CancellationToken cancellationToken)
    {
        // Outside Development the options validator guarantees a folder; this default only serves Development.
        var directory = string.IsNullOrWhiteSpace(options.Value.Directory)
            ? Path.Combine(environment.ContentRootPath, DevelopmentFolder)
            : options.Value.Directory;
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, $"security-events-{timeProvider.GetUtcNow():yyyyMMdd}.jsonl");
        var line = Encoding.UTF8.GetBytes(envelopeJson + "\n");

        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough);
            await stream.WriteAsync(line, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            gate.Release();
        }
    }
}
