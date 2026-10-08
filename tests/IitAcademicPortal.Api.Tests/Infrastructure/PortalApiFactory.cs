using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline over an isolated in-memory SQLite database with a capturing email sink.
/// </summary>
public class PortalApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Initial-pass1";

    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public CapturingEmailSender Emails { get; } = new();

    protected virtual TimeSpan ProofLifespan => TimeSpan.FromHours(1);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Portal", "Host=unused");
        builder.UseSetting("PasswordRecovery:ResetPageUrl", "https://portal.test/reset-password");
        builder.UseSetting("PasswordRecovery:ProofLifespan", ProofLifespan.ToString());
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "100000");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PortalDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PortalDbContext>>();
            services.AddDbContext<PortalDbContext>(options => options.UseSqlite(connection));

            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);

            // Test-only endpoints that exercise the authorization policies.
            services.AddControllers().AddApplicationPart(typeof(ProbeController).Assembly);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<PortalDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>A browser-like client: HTTPS (so Secure cookies flow), its own cookie jar, no redirects.</summary>
    public PortalClient CreatePortalClient()
    {
        var cookies = new CookieContainerHandler();
        var http = CreateDefaultClient(new Uri("https://localhost"), cookies);
        return new PortalClient(http, cookies.Container);
    }

    public async Task<string> CreateUserAsync(string email, params string[] roles)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = new PortalUser { UserName = email, Email = email };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(e => e.Description)));
        if (roles.Length > 0)
        {
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }

        return user.Id;
    }

    public async Task RemoveRoleAsync(string email, string role)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = await users.FindByEmailAsync(email);
        Assert.True((await users.RemoveFromRoleAsync(user!, role)).Succeeded);
    }

    public async Task WithDbAsync(Func<PortalDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<PortalDbContext>());
    }

    public static string UniqueEmail(string prefix) => $"{prefix}.{Guid.NewGuid():N}@iit.test";

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            connection.Dispose();
        }
    }
}
