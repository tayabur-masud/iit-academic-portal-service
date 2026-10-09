using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Captures every formatted log message (and exception text) so tests can prove no secret is logged.</summary>
public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> messages = new();

    public IReadOnlyCollection<string> Messages => messages.ToArray();

    public string Everything => string.Join("\n", messages);

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, messages);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<string> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            sink.Enqueue($"[{logLevel}] {category}: {formatter(state, exception)}{(exception is null ? string.Empty : " | " + exception)}");
        }
    }
}
