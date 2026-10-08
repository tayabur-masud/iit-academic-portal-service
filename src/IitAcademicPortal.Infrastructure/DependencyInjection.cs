using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Domain.Identity;
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
        services.AddDbContext<PortalDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Portal")));

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

        return services;
    }
}
