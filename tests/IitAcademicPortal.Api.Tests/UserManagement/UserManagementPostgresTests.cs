using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Application.UserManagement;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Domain.Teachers;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static IitAcademicPortal.Api.Tests.TeacherManagement.TeacherManagementPostgresTests;

namespace IitAcademicPortal.Api.Tests.UserManagement;

/// <summary>
/// User Management on a real PostgreSQL 18 database, as the restricted runtime role: the setup-template seed and the last-Admin lock
/// shared with Teacher Management under concurrency, across the generic and the Teacher paths. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class UserManagementPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private async Task<PortalUser> AddUserAsync(params string[] roles)
    {
        await using var db = Db.CreateRuntimeContext();
        var email = PortalApiFactory.UniqueEmail("pg");
        var user = new PortalUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            AccountStatus = TeacherAccountStatus.Active,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var store = new TeacherStore(db);
        foreach (var role in roles)
        {
            await store.StageRoleAsync(user.Id, role, true, CancellationToken.None);
        }

        await db.SaveChangesAsync();
        return user;
    }

    private async Task ClearAdminsAsync()
    {
        // Only the accounts of the current round count: remove Admin from accounts left by earlier rounds and tests.
        await using var clean = await Db.OpenAsync(Db.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(clean,
            "DELETE FROM \"AspNetUserRoles\" WHERE \"RoleId\" IN (SELECT \"Id\" FROM \"AspNetRoles\" WHERE \"Name\" = 'Admin')");
    }

    private async Task<int> ActiveAdminCountAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        return await db.UserRoles
            .Join(db.Roles.Where(r => r.Name == PortalRoles.Admin), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Join(db.Users.Where(u => u.AccountStatus == TeacherAccountStatus.Active), id => id, u => u.Id, (id, _) => id)
            .CountAsync();
    }

    [PostgresFact]
    public async Task The_migration_seeds_the_account_setup_template_as_a_security_template_with_a_fixed_identifier()
    {
        await using var db = Db.CreateRuntimeContext();

        var template = await db.NotificationTemplates.AsNoTracking().SingleAsync(t => t.EventType == NotificationEventType.AccountCredentialSetup);
        var revision = await db.NotificationTemplateRevisions.AsNoTracking().SingleAsync(r => r.TemplateId == template.Id);

        Assert.Equal(NotificationTemplateCatalog.TemplateId(NotificationEventType.AccountCredentialSetup), template.Id);
        Assert.Equal(NotificationTemplateCatalog.InitialRevisionId(NotificationEventType.AccountCredentialSetup), revision.Id);
        Assert.True(template.Enabled);
        Assert.Contains("{{actionLink}}", revision.Body);
    }

    [PostgresFact]
    public async Task Concurrent_generic_deactivations_of_the_last_two_Admins_leave_one_active()
    {
        for (var round = 0; round < 5; round++)
        {
            await ClearAdminsAsync();
            var first = await AddUserAsync(PortalRoles.Admin);
            var second = await AddUserAsync(PortalRoles.Admin);
            using var gate = new ConcurrentLockGate();

            var results = await Task.WhenAll(
                Task.Run(() => RunAsync(gate, access => access.DeactivateAsync(first.Id, CancellationToken.None))),
                Task.Run(() => RunAsync(gate, access => access.DeactivateAsync(second.Id, CancellationToken.None))));

            Assert.Equal(2, gate.LockCalls);
            Assert.Equal(1, results.Count(r => r.Outcome == AccountOutcome.Conflict));
            Assert.Equal(1, results.Count(r => r.Outcome == AccountOutcome.Ok));
            Assert.Equal(1, await ActiveAdminCountAsync());
        }
    }

    [PostgresFact]
    public async Task A_generic_deactivation_and_a_delegated_Teacher_Admin_removal_share_the_lock_and_leave_one_active_Admin()
    {
        for (var round = 0; round < 5; round++)
        {
            await ClearAdminsAsync();
            var generic = await AddUserAsync(PortalRoles.Admin);
            var teacher = await AddUserAsync(PortalRoles.Admin, PortalRoles.Teacher);
            await using (var db = Db.CreateRuntimeContext())
            {
                var profile = TeacherProfile.Create(teacher.Id);
                profile.Update("PG Teacher", null, null, null);
                db.TeacherProfiles.Add(profile);
                await db.SaveChangesAsync();
            }

            using var gate = new ConcurrentLockGate();

            var results = await Task.WhenAll(
                Task.Run(() => RunAsync(gate, access => access.DeactivateAsync(generic.Id, CancellationToken.None))),
                Task.Run(() => RunAsync(gate, access => access.UpdateRolesAsync(
                    teacher.Id, new AccountRolesInput([PortalRoles.Teacher], null), CancellationToken.None))));

            Assert.Equal(2, gate.LockCalls);
            Assert.Equal(1, results.Count(r => r.Outcome == AccountOutcome.Conflict));
            Assert.Equal(1, results.Count(r => r.Outcome == AccountOutcome.Ok));
            Assert.Equal(1, await ActiveAdminCountAsync());
        }
    }

    // Each operation gets its own scope and connection, as two concurrent requests would, with no signed-in caller.
    private async Task<AccountResult<UserAccountDetail>> RunAsync(
        ConcurrentLockGate gate, Func<UserAccessService, Task<AccountResult<UserAccountDetail>>> operation)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddIdentityCore<PortalUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PortalDbContext>();
        services.AddScoped(_ => Db.CreateRuntimeContext());
        services.AddScoped<ITeacherStore>(provider => new BarrierTeacherStore(new TeacherStore(provider.GetRequiredService<PortalDbContext>()), gate));
        services.AddScoped<IUserAccountStore, UserAccountStore>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddSingleton<IAuditEventRecorder, NoopAuditRecorder>();
        services.AddSingleton<IAuditActorDirectory, EmptyAuditActorDirectory>();
        services.AddSingleton<IAuditRequestContext, NoRequestContext>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TeacherLifecycleService>();
        services.AddScoped<TeacherRoleService>();
        services.AddScoped<TeacherProfileService>();
        services.AddScoped<UserAccessService>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return await operation(scope.ServiceProvider.GetRequiredService<UserAccessService>());
    }

    private sealed class NoRequestContext : IAuditRequestContext
    {
        public string? ActorUserId => null;

        public string? CorrelationId => null;

        public string? Source => null;
    }
}
