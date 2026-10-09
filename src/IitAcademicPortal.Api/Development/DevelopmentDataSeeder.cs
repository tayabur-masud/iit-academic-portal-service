using IitAcademicPortal.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace IitAcademicPortal.Api.Development;

/// <summary>
/// Development-only test accounts for the quickstart scenarios. Account provisioning is otherwise out
/// of scope. Runs only in the Development environment when <c>DevelopmentSeed:Password</c> is set
/// (for example with <c>dotnet user-secrets</c>); no password is stored in source.
/// </summary>
public static class DevelopmentDataSeeder
{
    // The first role listed is the account's default role. Teacher+Coordinator defaults to Teacher, which
    // differs from the fallback order (Coordinator first), so the stored default is observable.
    private static readonly (string Email, string[] Roles)[] Accounts =
    [
        ("admin@iit.test", [PortalRoles.Admin]),
        ("student.personal@example.test", [PortalRoles.Student]),
        ("teacher@iit.test", [PortalRoles.Teacher]),
        ("coordinator@iit.test", [PortalRoles.Coordinator]),
        ("teacher.coordinator@iit.test", [PortalRoles.Teacher, PortalRoles.Coordinator]),
    ];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration, ILogger logger)
    {
        var password = configuration["DevelopmentSeed:Password"];
        if (string.IsNullOrEmpty(password))
        {
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<PortalUser>>();

        foreach (var (email, roles) in Accounts)
        {
            if (await users.FindByEmailAsync(email) is { } existing)
            {
                // Accounts seeded before default roles existed get one; an existing value is left alone.
                if (existing.DefaultRole is null)
                {
                    existing.DefaultRole = roles[0];
                    await users.UpdateAsync(existing);
                }

                continue;
            }

            var user = new PortalUser { UserName = email, Email = email, EmailConfirmed = true, DefaultRole = roles[0] };
            var created = await users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                logger.LogWarning("Development seed account could not be created: {Errors}", string.Join("; ", created.Errors.Select(e => e.Code)));
                continue;
            }

            await users.AddToRolesAsync(user, roles);
        }
    }
}
