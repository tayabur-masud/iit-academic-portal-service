using System.Net;
using IitAcademicPortal.Api.Tests.Auditing.Fixtures;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Auditing;
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
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>
/// Hosts the real API pipeline over an isolated in-memory SQLite database with a capturing email sink.
/// </summary>
public class PortalApiFactory : WebApplicationFactory<Program>
{
    public const string Password = "Initial-pass1";

    /// <summary>The address every request appears to come from (a documentation range address).</summary>
    public const string ClientAddress = "203.0.113.9";

    private readonly SqliteConnection connection = new("DataSource=:memory:");

    public PortalApiFactory()
    {
        Metrics = new AuditMetricsListener(auditMetrics);
    }

    public CapturingEmailSender Emails { get; } = new();

    /// <summary>Follows real time; tests advance it to expire retries and leases without waiting.</summary>
    public TestClock Clock { get; } = new();

    /// <summary>Stands in for the durable fallback sink used when the outbox write fails.</summary>
    public CapturingSecurityEventSink FallbackSink { get; } = new();

    /// <summary>Counts audit metric increments for the lifetime of this factory.</summary>
    public AuditMetricsListener Metrics { get; }

    private readonly AuditMetrics auditMetrics = new();

    /// <summary>Every log message the application writes, so tests can prove no secret reaches the logs.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    private readonly string fallbackDirectory = Path.Combine(Path.GetTempPath(), "iit-audit-tests", Guid.NewGuid().ToString("N"));

    protected virtual TimeSpan ProofLifespan => TimeSpan.FromHours(1);

    /// <summary>The wrapped outbox store, so a test can make enqueueing or delivery fail.</summary>
    public FailableOutboxStore Outbox => Services.GetRequiredService<FailableOutboxStore>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        connection.Open();

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Portal", "Host=unused");
        builder.UseSetting("PasswordRecovery:ResetPageUrl", "https://portal.test/reset-password");
        builder.UseSetting("PasswordRecovery:ProofLifespan", ProofLifespan.ToString());
        builder.UseSetting("RateLimiting:Authentication:PermitLimit", "100000");

        // Tests drive the outbox processor directly, with short retry delays; the background worker stays off.
        builder.UseSetting("AuditDelivery:WorkerEnabled", "false");
        builder.UseSetting("AuditDelivery:MaxAttempts", "3");
        builder.UseSetting("AuditDelivery:BaseRetryDelay", "00:00:01");
        builder.UseSetting("AuditDelivery:MaxRetryDelay", "00:00:10");
        builder.UseSetting("AuditDelivery:LeaseDuration", "00:01:00");
        builder.UseSetting("AuditFallback:Sink", "File");
        builder.UseSetting("AuditFallback:Directory", fallbackDirectory);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PortalDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<PortalDbContext>>();
            services.RemoveAll<IDbContextFactory<PortalDbContext>>();
            services.RemoveAll<PortalDbContext>();
            services.AddPortalDatabase(options => options
                .UseSqlite(connection)
                .ReplaceService<IModelCustomizer, StandInModelCustomizer>());

            // The in-memory test server has no client address; give every request one so the audit source can be checked.
            services.AddSingleton<IStartupFilter>(new FixedRemoteAddressStartupFilter(IPAddress.Parse(ClientAddress)));

            services.AddSingleton<ILoggerProvider>(Logs);

            // This host's own metrics instance, so its counters are not mixed with other hosts running in parallel.
            services.RemoveAll<AuditMetrics>();
            services.AddSingleton(auditMetrics);

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IDurableSecurityEventSink>();
            services.AddSingleton<IDurableSecurityEventSink>(FallbackSink);

            // The real outbox store, wrapped so tests can force failures.
            services.RemoveAll<ISecurityAuditOutboxStore>();
            services.AddSingleton<SecurityAuditOutboxStore>();
            services.AddSingleton<FailableOutboxStore>(sp => new FailableOutboxStore(sp.GetRequiredService<SecurityAuditOutboxStore>()));
            services.AddSingleton<ISecurityAuditOutboxStore>(sp => sp.GetRequiredService<FailableOutboxStore>());

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

    public async Task SetDefaultRoleAsync(string email, string? role)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();
        var user = await users.FindByEmailAsync(email);
        user!.DefaultRole = role;
        Assert.True((await users.UpdateAsync(user)).Succeeded);
    }

    /// <summary>Delivers every due security event from the outbox to the formal audit store.</summary>
    public async Task FlushAuditAsync()
    {
        var processor = Services.GetRequiredService<SecurityAuditOutboxProcessor>();
        for (var pass = 0; pass < 20 && await processor.ProcessOnceAsync(CancellationToken.None) > 0; pass++)
        {
        }
    }

    /// <summary>Flushes the outbox, then returns the formal audit events, oldest first.</summary>
    public async Task<List<AuditEvent>> AuditEventsAsync()
    {
        await FlushAuditAsync();
        var events = new List<AuditEvent>();
        await WithDbAsync(async db => events.AddRange(await db.AuditEvents.AsNoTracking().OrderBy(e => e.OccurredAtUtc).ToListAsync()));
        return events;
    }

    /// <summary>The formal audit events right now, without delivering anything from the outbox first.</summary>
    public async Task<List<AuditEvent>> AuditEventsAsyncWithoutFlush()
    {
        var events = new List<AuditEvent>();
        await WithDbAsync(async db => events.AddRange(await db.AuditEvents.AsNoTracking().ToListAsync()));
        return events;
    }

    public async Task<List<AuditEvent>> AuditEventsAsync(string eventType) =>
        (await AuditEventsAsync()).Where(e => e.EventType == eventType).ToList();

    public async Task<List<SecurityAuditOutboxItem>> OutboxItemsAsync()
    {
        var items = new List<SecurityAuditOutboxItem>();
        await WithDbAsync(async db => items.AddRange(await db.SecurityAuditOutbox.AsNoTracking().OrderBy(i => i.EnqueuedAtUtc).ToListAsync()));
        return items;
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
            Metrics.Dispose();
            auditMetrics.Dispose();
            try
            {
                Directory.Delete(fallbackDirectory, recursive: true);
            }
            catch (IOException)
            {
                // The folder may not exist, or may be briefly locked; it is temporary test output.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
