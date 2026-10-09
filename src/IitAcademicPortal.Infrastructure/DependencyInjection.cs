using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Auditing;
using IitAcademicPortal.Infrastructure.Email;
using IitAcademicPortal.Infrastructure.PasswordRecovery;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // The API runs as a restricted runtime account. `dotnet ef` runs as the schema owner, so at design time
        // it uses the migrations connection string when one is configured.
        services.AddPortalDatabase(options =>
        {
            var migrations = configuration.GetConnectionString("PortalMigrations");
            var name = EF.IsDesignTime && !string.IsNullOrWhiteSpace(migrations) ? "PortalMigrations" : "Portal";
            options.UseNpgsql(configuration.GetConnectionString(name));
        });

        services.AddDataProtection();
        services
            .AddIdentityCore<PortalUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                // The approved rule (8+ characters, a letter, a number) is enforced by PortalPasswordValidator;
                // Identity's stricter defaults are switched off so no unapproved rules apply.
                options.Password.RequiredLength = PasswordPolicy.MinimumLength;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PortalDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<PortalPasswordValidator>();

        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<PasswordRecoveryOptions>>((tokens, recovery) => tokens.TokenLifespan = recovery.Value.ProofLifespan);

        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        services.AddSingleton<PasswordRecoveryQueue>();
        services.AddSingleton<IPasswordRecoveryQueue>(sp => sp.GetRequiredService<PasswordRecoveryQueue>());
        services.AddHostedService<PasswordRecoveryWorker>();

        services.AddAuditInfrastructure(configuration);

        return services;
    }

    private static IServiceCollection AddAuditInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IAuditActorDirectory, AuditActorDirectory>();
        services.AddSingleton<ISecurityAuditOutboxStore, SecurityAuditOutboxStore>();

        services.Configure<AuditDeliveryOptions>(configuration.GetSection(AuditDeliveryOptions.SectionName));
        services.AddSingleton<SecurityAuditOutboxProcessor>();
        services.AddHostedService<SecurityAuditOutboxWorker>();

        // Outside Development the service refuses to start without a configured durable fallback sink.
        services.AddOptions<AuditFallbackOptions>()
            .Bind(configuration.GetSection(AuditFallbackOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AuditFallbackOptions>, AuditFallbackOptionsValidator>();
        services.AddSingleton<IDurableSecurityEventSink, FileSecurityEventSink>();

        return services;
    }
}
