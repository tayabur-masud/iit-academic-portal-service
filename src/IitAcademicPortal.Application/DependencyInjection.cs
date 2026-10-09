using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Application.PasswordRecovery;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PasswordRecoveryOptions>()
            .Bind(configuration.GetSection(PasswordRecoveryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditMetrics>();
        services.AddScoped<IAuditEventRecorder, AuditEventRecorder>();
        services.AddScoped<AuditReviewService>();
        services.AddScoped<PortalAuthenticationService>();
        services.AddScoped<PasswordRecoveryService>();
        return services;
    }
}
