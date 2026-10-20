using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Infrastructure.Identity;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Auditing;
using IitAcademicPortal.Infrastructure.Email;
using IitAcademicPortal.Infrastructure.Notifications;
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
            .AddTokenProvider<AccountCredentialSetupTokenProvider>(AccountCredentialSetupService.TokenProvider)
            .AddPasswordValidator<PortalPasswordValidator>();

        services.AddOptions<DataProtectionTokenProviderOptions>()
            .Configure<IOptions<PasswordRecoveryOptions>>((tokens, recovery) => tokens.TokenLifespan = recovery.Value.ProofLifespan);

        // Setup proofs have their own provider, protector purpose, and lifespan, so they never interchange with reset proofs.
        services.AddOptions<AccountCredentialSetupTokenProviderOptions>()
            .Configure<IOptions<AccountCredentialSetupOptions>>((tokens, setup) => tokens.TokenLifespan = setup.Value.ProofLifespan);

        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();

        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddEmailProvider(configuration);
        AddNotificationInfrastructure(services, configuration);

        services.AddAuditInfrastructure(configuration);

        return services;
    }

    // SMTP is the default. Brevo is a Development-only adapter behind the same IEmailSender, so no producer changes when the provider
    // does; options validation refuses it (and a Brevo key) in any other environment, at startup.
    public static IServiceCollection AddEmailProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailProviderOptions>()
            .Bind(configuration.GetSection(EmailProviderOptions.SectionName))
            .ValidateOnStart();
        services.AddOptions<BrevoOptions>().Bind(configuration.GetSection(BrevoOptions.SectionName));
        services.AddSingleton<IValidateOptions<EmailProviderOptions>, EmailProviderOptionsValidator>();

        services.AddSingleton<SmtpEmailSender>();
        services.AddHttpClient<BrevoEmailSender>((provider, client) =>
            client.Timeout = provider.GetRequiredService<IOptions<BrevoOptions>>().Value.RequestTimeout);
        services.AddTransient<IEmailSender>(provider =>
            string.Equals(provider.GetRequiredService<IOptions<EmailProviderOptions>>().Value.Provider, EmailProviderOptions.Brevo, StringComparison.OrdinalIgnoreCase)
                ? provider.GetRequiredService<BrevoEmailSender>()
                : provider.GetRequiredService<SmtpEmailSender>());
        return services;
    }

    private static void AddNotificationInfrastructure(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<INotificationStore, NotificationStore>();
        services.AddScoped<IFeedbackReminderStore, FeedbackReminderStore>();
        services.AddScoped<INotificationAdminStore, NotificationAdminStore>();
        services.AddScoped<IRecipientDirectory, RecipientDirectory>();

        // Email is the only delivery channel. Another channel is a new adapter registered here, with a seeded template; no producer changes.
        services.AddScoped<IDeliveryChannel, EmailDeliveryChannel>();
        services.AddSingleton<ITransientSecretProtector, TransientSecretProtector>();

        services.AddOptions<NotificationDispatchOptions>()
            .Bind(configuration.GetSection(NotificationDispatchOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationDispatchOptions>, NotificationDispatchOptionsValidator>();
        services.AddSingleton<NotificationDispatchProcessor>();
        services.AddHostedService<NotificationDispatchWorker>();
    }

    private static IServiceCollection AddAuditInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IConfigurationStore, ConfigurationStore>();
        services.AddScoped<IGradeScaleStore, GradeScaleStore>();
        services.AddScoped<ITeacherStore, TeacherStore>();
        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IStudentStore, StudentStore>();
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
