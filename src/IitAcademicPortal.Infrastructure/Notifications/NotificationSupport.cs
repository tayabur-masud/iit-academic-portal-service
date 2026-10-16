using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>Identity-owned recipient lookup. Each call reads the current email and status; nothing is cached.</summary>
public sealed class RecipientDirectory(IDbContextFactory<PortalDbContext> factory) : IRecipientDirectory
{
    public async Task<IReadOnlyDictionary<string, RecipientInfo>> FindAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<string, RecipientInfo>();
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = new List<RecipientInfo>();

        // Batches keep the IN list bounded for a large recipient list.
        foreach (var batch in userIds.Chunk(500))
        {
            rows.AddRange(await db.Users.AsNoTracking()
                .Where(u => batch.Contains(u.Id))
                .Select(u => new RecipientInfo(u.Id, u.Email, u.AccountStatus == TeacherAccountStatus.Deactivated))
                .ToListAsync(cancellationToken));
        }

        return rows.ToDictionary(r => r.UserId, StringComparer.Ordinal);
    }
}

/// <summary>Protects short-lived secrets with ASP.NET Core Data Protection under a dedicated purpose.</summary>
public sealed class TransientSecretProtector(IDataProtectionProvider provider) : ITransientSecretProtector
{
    private readonly IDataProtector protector = provider.CreateProtector("IitAcademicPortal.Notifications.TransientSecret.v1");

    public string Protect(string value) => protector.Protect(value);

    public string Unprotect(string protectedValue)
    {
        try
        {
            return protector.Unprotect(protectedValue);
        }
        catch (System.Security.Cryptography.CryptographicException ex)
        {
            throw new InvalidOperationException("The protected value cannot be read.", ex);
        }
    }
}
