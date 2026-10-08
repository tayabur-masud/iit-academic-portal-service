using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IitAcademicPortal.Api.Security;

public sealed class AuthenticationRateLimitOptions
{
    public const string SectionName = "RateLimiting:Authentication";

    public int PermitLimit { get; set; } = 10;

    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(1);
}

public static class RateLimitPolicies
{
    /// <summary>Per-client limit for sign-in and password-recovery requests.</summary>
    public const string Authentication = "Authentication";

    public static IServiceCollection AddPortalRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(AuthenticationRateLimitOptions.SectionName).Get<AuthenticationRateLimitOptions>()
            ?? new AuthenticationRateLimitOptions();

        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problems.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many attempts",
                        Detail = "Wait a minute, then try again.",
                    },
                });
            };

            // Partitioned by client address. Behind a reverse proxy, configure forwarded headers so this is
            // the real client address rather than the proxy's.
            options.AddPolicy(Authentication, http => RateLimitPartition.GetFixedWindowLimiter(
                http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = settings.PermitLimit, Window = settings.Window }));
        });
    }
}
