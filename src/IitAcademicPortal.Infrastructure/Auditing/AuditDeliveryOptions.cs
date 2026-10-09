namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>How the outbox worker delivers security events to the formal audit store.</summary>
public sealed class AuditDeliveryOptions
{
    public const string SectionName = "AuditDelivery";

    /// <summary>When false the background worker does not run (tests drive the processor directly).</summary>
    public bool WorkerEnabled { get; set; } = true;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public int BatchSize { get; set; } = 50;

    /// <summary>Automatic attempts before an item is marked exhausted and awaits authorized recovery.</summary>
    public int MaxAttempts { get; set; } = 8;

    public TimeSpan BaseRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long a worker holds an item before another worker may claim it again.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(2);
}
