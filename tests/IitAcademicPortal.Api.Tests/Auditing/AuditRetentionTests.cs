using System.Text.Json;
using System.Text.RegularExpressions;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>
/// No retention period is invented and nothing deletes or archives audit history without an approved policy
/// (FR-024, FR-025). These checks fail if a deletion or archive job, setting, or statement is introduced.
/// </summary>
public sealed partial class AuditRetentionTests : IDisposable
{
    private static readonly string[] ForbiddenNames = ["retention", "archive", "archival", "purge", "cleanup", "expire"];

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static string ServiceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IitAcademicPortal.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The service root was not found.");
    }

    [Fact]
    public void The_only_background_workers_deliver_security_events_and_send_recovery_email()
    {
        var workers = factory.Services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();

        Assert.Contains("SecurityAuditOutboxWorker", workers);
        Assert.Contains("PasswordRecoveryWorker", workers);
        Assert.DoesNotContain(workers, name => ForbiddenNames.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void No_source_file_deletes_truncates_or_archives_audit_events_or_outbox_rows()
    {
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(ServiceRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .ToList();
        Assert.NotEmpty(sourceFiles);

        foreach (var file in sourceFiles)
        {
            var text = File.ReadAllText(file);
            Assert.False(DeletionPattern().IsMatch(text), $"{Path.GetFileName(file)} deletes or truncates stored records.");
        }
    }

    [Fact]
    public void No_retention_or_archive_setting_exists_in_the_shipped_configuration()
    {
        var settings = Directory.EnumerateFiles(Path.Combine(ServiceRoot(), "src", "IitAcademicPortal.Api"), "appsettings*.json");

        foreach (var file in settings)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            var keys = new List<string>();
            Collect(document.RootElement, keys);
            Assert.DoesNotContain(keys, key => ForbiddenNames.Any(f => key.Contains(f, StringComparison.OrdinalIgnoreCase)));
        }
    }

    [Fact]
    public async Task An_old_event_survives_decades_of_processing()
    {
        var old = new AuditEvent(
            Guid.NewGuid(), new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc), AuditEventCategory.Security, "test.old",
            AuditOutcome.Success, null, null, null, null, null, null, null);
        await factory.WithDbAsync(async db =>
        {
            db.AuditEvents.Add(old);
            await db.SaveChangesAsync();
        });

        factory.Clock.Advance(TimeSpan.FromDays(365 * 50));
        await factory.FlushAuditAsync();

        Assert.Single(await factory.AuditEventsAsync("test.old"));
    }

    private static void Collect(JsonElement element, List<string> keys)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                keys.Add(property.Name);
                Collect(property.Value, keys);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, keys);
            }
        }
    }

    [GeneratedRegex(@"ExecuteDelete(Async)?\s*\(|\.AuditEvents\s*\.\s*(Remove|RemoveRange)\s*\(|\.SecurityAuditOutbox\s*\.\s*(Remove|RemoveRange)\s*\(|DELETE\s+FROM|TRUNCATE\s", RegexOptions.IgnoreCase)]
    private static partial Regex DeletionPattern();
}
