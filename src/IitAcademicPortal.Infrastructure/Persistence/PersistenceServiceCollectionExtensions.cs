using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers the portal database as a context factory plus a scoped context built from it. The security
    /// outbox uses the factory for short-lived contexts that are independent of the unit of work of a request.
    /// </summary>
    public static IServiceCollection AddPortalDatabase(this IServiceCollection services, Action<DbContextOptionsBuilder> configure)
    {
        services.AddSingleton<AuditedEntityInterceptor>();
        services.AddDbContextFactory<PortalDbContext>((provider, options) =>
        {
            configure(options);
            options.AddInterceptors(provider.GetRequiredService<AuditedEntityInterceptor>());
        });
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<PortalDbContext>>().CreateDbContext());
        return services;
    }
}
