using IitAcademicPortal.Application.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IitAcademicPortal.Infrastructure.Auditing;

/// <summary>
/// Reports the security-event outbox. Unhealthy when any event awaits authorized recovery; degraded when the
/// oldest undelivered event has been waiting longer than the threshold. It reports counts only, never content.
/// </summary>
public sealed class AuditOutboxHealthCheck(ISecurityAuditOutboxStore store, TimeProvider timeProvider) : IHealthCheck
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = await store.GetStatusAsync(cancellationToken);
        var data = new Dictionary<string, object>
        {
            ["pending"] = status.Pending,
            ["retryScheduled"] = status.RetryScheduled,
            ["exhausted"] = status.Exhausted,
        };

        if (status.Exhausted > 0)
        {
            return HealthCheckResult.Unhealthy($"{status.Exhausted} security audit event(s) await authorized recovery.", data: data);
        }

        var stale = status.OldestUndeliveredEnqueuedAtUtc is { } oldest
            && timeProvider.GetUtcNow().UtcDateTime - oldest > StaleAfter;
        return stale
            ? HealthCheckResult.Degraded("Security audit events have been waiting for delivery longer than expected.", data: data)
            : HealthCheckResult.Healthy("The security audit outbox is draining.", data);
    }
}
