using System.Text.Json;
using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Infrastructure.Auditing;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IitAcademicPortal.Api.Auditing;

/// <summary>
/// A protected health endpoint for the security-event outbox. It reports counts only, never event content, and
/// requires the same Admin session as audit review. Alerting is driven by the audit counters and critical logs;
/// this endpoint is for operators to inspect the outbox.
/// </summary>
public static class AuditHealthEndpoint
{
    public const string Route = "/health/audit";

    private const string CheckName = "audit-outbox";

    public static IServiceCollection AddAuditHealth(this IServiceCollection services)
    {
        services.AddHealthChecks().AddCheck<AuditOutboxHealthCheck>(CheckName);
        return services;
    }

    public static IEndpointRouteBuilder MapAuditHealth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapHealthChecks(Route, new HealthCheckOptions
        {
            // Unhealthy (events awaiting recovery) is a 503 so a probe notices; degraded stays 200.
            ResultStatusCodes =
            {
                [HealthStatus.Healthy] = StatusCodes.Status200OK,
                [HealthStatus.Degraded] = StatusCodes.Status200OK,
                [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
            },
            ResponseWriter = async (context, report) =>
            {
                var entry = report.Entries[CheckName];
                context.Response.ContentType = "application/json";
                await JsonSerializer.SerializeAsync(context.Response.Body, new
                {
                    status = report.Status.ToString(),
                    pending = entry.Data.GetValueOrDefault("pending"),
                    retryScheduled = entry.Data.GetValueOrDefault("retryScheduled"),
                    exhausted = entry.Data.GetValueOrDefault("exhausted"),
                    description = entry.Description,
                }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            },
        }).RequireAuthorization(PortalPolicies.AuditReview);

        return endpoints;
    }
}
