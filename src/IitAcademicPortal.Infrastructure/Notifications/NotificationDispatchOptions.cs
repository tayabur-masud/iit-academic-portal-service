using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>
/// How the notification worker claims and retries work. Eight total attempts (the first plus at most seven retries), a five-second
/// base delay that doubles up to fifteen minutes, polling every five seconds, batches of 50, and a two-minute lease: the same
/// bounded policy the security audit outbox uses.
/// </summary>
public sealed class NotificationDispatchOptions
{
    public const string SectionName = "NotificationDispatch";

    /// <summary>When false the background worker does not run (tests drive the processor directly).</summary>
    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 50;

    /// <summary>Total automatic attempts. A retryable failure on the last one makes the notification Failed.</summary>
    public int MaxAttempts { get; set; } = 8;

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a worker holds work before another worker may claim it again.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}

public sealed class NotificationDispatchOptionsValidator : IValidateOptions<NotificationDispatchOptions>
{
    public ValidateOptionsResult Validate(string? name, NotificationDispatchOptions options)
    {
        var failures = new List<string>();
        if (options.PollInterval <= TimeSpan.Zero)
        {
            failures.Add("NotificationDispatch:PollInterval must be positive.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("NotificationDispatch:BatchSize must be between 1 and 1000.");
        }

        if (options.MaxAttempts is < 1 or > 20)
        {
            failures.Add("NotificationDispatch:MaxAttempts must be between 1 and 20: retries are always bounded.");
        }

        if (options.BaseRetryDelay <= TimeSpan.Zero || options.MaxRetryDelay < options.BaseRetryDelay)
        {
            failures.Add("NotificationDispatch:BaseRetryDelay must be positive and no more than MaxRetryDelay.");
        }

        if (options.LeaseDuration <= TimeSpan.Zero)
        {
            failures.Add("NotificationDispatch:LeaseDuration must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
