using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using IitAcademicPortal.Domain.Teachers;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.TeacherManagement;

/// <summary>
/// Teacher Management on a real PostgreSQL 18 database, as the restricted runtime role: the migration backfill, the unique and
/// restrictive constraints, the runtime grants, and the last-Admin lock under concurrency. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class TeacherManagementPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private const string PreviousMigration = "20261014085502_AuditedEntityColumns";

    private PostgresTestDatabase Db => fixture.Database!;

    private static PortalUser NewUser(TeacherAccountStatus status = TeacherAccountStatus.Active)
    {
        var email = PortalApiFactory.UniqueEmail("pg");
        return new PortalUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
            AccountStatus = status,
        };
    }

    private static TeacherProfile NewProfile(string userId, string? staffId = null)
    {
        var profile = TeacherProfile.Create(userId);
        profile.Update("PG Teacher", staffId, null, null);
        return profile;
    }

    private async Task<PortalUser> AddUserAsync(TeacherAccountStatus status = TeacherAccountStatus.Active, params string[] roles)
    {
        await using var db = Db.CreateRuntimeContext();
        var user = NewUser(status);
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

    private static async Task<string?> SqlStateAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<DbUpdateException>(action);
        return (error.InnerException as PostgresException)?.SqlState;
    }

    [PostgresFact]
    public async Task Existing_accounts_become_Active_when_the_migration_is_applied()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        await using (var ddl = database.CreateContext(database.DdlConnectionString))
        {
            var migrator = ddl.GetService<IMigrator>();
            await migrator.MigrateAsync(PreviousMigration);
            await using var connection = await database.OpenAsync(database.DdlConnectionString);
            await PostgresTestDatabase.ExecuteAsync(connection,
                "INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", " +
                "\"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") " +
                "VALUES ('old-user', 'old@iit.test', 'OLD@IIT.TEST', 'old@iit.test', 'OLD@IIT.TEST', false, false, false, false, 0)");
            await migrator.MigrateAsync();
        }

        await using var verify = await database.OpenAsync(database.DdlConnectionString);
        await using var command = new NpgsqlCommand("SELECT \"AccountStatus\" FROM \"AspNetUsers\" WHERE \"Id\" = 'old-user'", verify);
        Assert.Equal("Active", await command.ExecuteScalarAsync());
    }

    [PostgresFact]
    public async Task Pending_and_deactivated_statuses_are_stored_as_written()
    {
        var pending = await AddUserAsync(TeacherAccountStatus.PendingCredentialSetup);
        var deactivated = await AddUserAsync(TeacherAccountStatus.Deactivated);

        await using var db = Db.CreateRuntimeContext();
        Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, (await db.Users.SingleAsync(u => u.Id == pending.Id)).AccountStatus);
        Assert.Equal(TeacherAccountStatus.Deactivated, (await db.Users.SingleAsync(u => u.Id == deactivated.Id)).AccountStatus);
    }

    [PostgresFact]
    public async Task The_runtime_role_can_use_the_teacher_table_and_the_audit_columns_are_filled()
    {
        var user = await AddUserAsync();
        await using (var db = Db.CreateRuntimeContext())
        {
            db.TeacherProfiles.Add(NewProfile(user.Id, "PG-1"));
            await db.SaveChangesAsync();
        }

        await using var read = Db.CreateRuntimeContext();
        var profile = await read.TeacherProfiles.SingleAsync(p => p.UserId == user.Id);
        profile.Deactivate();
        await read.SaveChangesAsync();
        Assert.InRange(profile.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
    }

    [PostgresFact]
    public async Task An_account_can_have_only_one_teacher_profile()
    {
        var user = await AddUserAsync();
        await using (var db = Db.CreateRuntimeContext())
        {
            db.TeacherProfiles.Add(NewProfile(user.Id));
            await db.SaveChangesAsync();
        }

        await using var second = Db.CreateRuntimeContext();
        second.TeacherProfiles.Add(NewProfile(user.Id));
        Assert.Equal("23505", await SqlStateAsync(() => second.SaveChangesAsync()));
    }

    [PostgresFact]
    public async Task A_staff_id_is_unique_once_normalized_even_for_an_inactive_profile_and_blank_ones_may_repeat()
    {
        var staffId = $"EMP {Guid.NewGuid():N}";
        var first = await AddUserAsync();
        await using (var db = Db.CreateRuntimeContext())
        {
            var profile = NewProfile(first.Id, staffId);
            profile.Deactivate();
            db.TeacherProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        var other = await AddUserAsync();
        await using (var clash = Db.CreateRuntimeContext())
        {
            clash.TeacherProfiles.Add(NewProfile(other.Id, $" {staffId.ToLowerInvariant()} "));
            Assert.Equal("23505", await SqlStateAsync(() => clash.SaveChangesAsync()));
        }

        foreach (var _ in new[] { 1, 2 })
        {
            var blank = await AddUserAsync();
            await using var db = Db.CreateRuntimeContext();
            db.TeacherProfiles.Add(NewProfile(blank.Id));
            await db.SaveChangesAsync();
        }
    }

    [PostgresFact]
    public async Task An_account_with_a_profile_and_a_designation_in_use_cannot_be_removed()
    {
        var user = await AddUserAsync();
        var designationId = Guid.NewGuid();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection,
                $"INSERT INTO \"Designations\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{designationId}', 'PG Post {designationId:N}', 'PG POST {designationId:N}', true)");
        }

        await using (var db = Db.CreateRuntimeContext())
        {
            var profile = TeacherProfile.Create(user.Id);
            profile.Update("Referenced", null, null, designationId);
            db.TeacherProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        var userError = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{user.Id}'"));
        var designationError = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"Designations\" WHERE \"Id\" = '{designationId}'"));
        Assert.Contains(userError.SqlState, new[] { "23001", "23503" });
        Assert.Contains(designationError.SqlState, new[] { "23001", "23503" });
    }

    [PostgresFact]
    public async Task Concurrent_role_removal_and_deactivation_services_preserve_an_active_Admin()
    {
        for (var round = 0; round < 5; round++)
        {
            // Only the pair in this round count: remove Admin from accounts left by earlier rounds.
            await using (var clean = await Db.OpenAsync(Db.DdlConnectionString))
            {
                await PostgresTestDatabase.ExecuteAsync(clean,
                    "DELETE FROM \"AspNetUserRoles\" WHERE \"RoleId\" IN (SELECT \"Id\" FROM \"AspNetRoles\" WHERE \"Name\" = 'Admin')");
            }

            var roleTeacher = await AddUserAsync(TeacherAccountStatus.Active, PortalRoles.Admin, PortalRoles.Teacher);
            var deactivationTeacher = await AddUserAsync(TeacherAccountStatus.Active, PortalRoles.Admin, PortalRoles.Teacher);
            var roleProfile = NewProfile(roleTeacher.Id);
            var deactivationProfile = NewProfile(deactivationTeacher.Id);
            await using (var db = Db.CreateRuntimeContext())
            {
                db.TeacherProfiles.AddRange(roleProfile, deactivationProfile);
                await db.SaveChangesAsync();
            }

            using var gate = new ConcurrentLockGate();

            var results = await Task.WhenAll(
                Task.Run(() => RunAdminChangeAsync(roleProfile.Id, deactivate: false, gate)),
                Task.Run(() => RunAdminChangeAsync(deactivationProfile.Id, deactivate: true, gate)));

            Assert.Equal(2, gate.LockCalls);
            Assert.Equal(1, results.Count(result => result.Outcome == TeacherOutcome.Conflict));
            Assert.Equal(1, results.Count(result => result.Outcome == TeacherOutcome.Ok));
            await using var verifyDb = Db.CreateRuntimeContext();
            var remainingActiveAdmins = await verifyDb.UserRoles
                .Join(verifyDb.Roles.Where(r => r.Name == PortalRoles.Admin), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
                .Join(verifyDb.Users.Where(user => user.AccountStatus == TeacherAccountStatus.Active), userId => userId, user => user.Id, (userId, _) => userId)
                .CountAsync();
            Assert.Equal(1, remainingActiveAdmins);
        }
    }

    private async Task<TeacherResult<TeacherDetail>> RunAdminChangeAsync(Guid teacherId, bool deactivate, ConcurrentLockGate gate)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddIdentityCore<PortalUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<PortalDbContext>();
        services.AddScoped(_ => Db.CreateRuntimeContext());
        services.AddScoped<ITeacherStore>(provider => new BarrierTeacherStore(
            new TeacherStore(provider.GetRequiredService<PortalDbContext>()), gate));
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddSingleton<IAuditEventRecorder, NoopAuditRecorder>();
        services.AddSingleton<IAuditActorDirectory, EmptyAuditActorDirectory>();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TeacherLifecycleService>();
        services.AddScoped<TeacherRoleService>();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        return deactivate
            ? await scope.ServiceProvider.GetRequiredService<TeacherLifecycleService>()
                .DeactivateAsync(teacherId, CancellationToken.None)
            : await scope.ServiceProvider.GetRequiredService<TeacherRoleService>()
                .UpdateAsync(teacherId, new TeacherRolesInput([PortalRoles.Teacher], null), CancellationToken.None);
    }

    internal sealed class ConcurrentLockGate : IDisposable
    {
        private readonly Barrier barrier = new(2);
        private int lockCalls;

        public int LockCalls => Volatile.Read(ref lockCalls);

        public async Task LockAsync(ITeacherStore store, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref lockCalls);
            if (!barrier.SignalAndWait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException("Both application operations must reach the last-Admin lock.");
            }

            await store.LockAdminInvariantAsync(cancellationToken);
        }

        public void Dispose() => barrier.Dispose();
    }

    /// <summary>Delegates every operation to the production store while coordinating just before its advisory lock.</summary>
    internal sealed class BarrierTeacherStore(ITeacherStore inner, ConcurrentLockGate gate) : ITeacherStore
    {
        public Task<ITeacherTransaction> BeginTransactionAsync(CancellationToken cancellationToken) => inner.BeginTransactionAsync(cancellationToken);

        public Task LockAdminInvariantAsync(CancellationToken cancellationToken) => gate.LockAsync(inner, cancellationToken);

        public Task<TeacherProfile?> FindProfileAsync(Guid teacherId, CancellationToken cancellationToken) => inner.FindProfileAsync(teacherId, cancellationToken);

        public Task<TeacherProfile?> FindProfileByUserAsync(string userId, CancellationToken cancellationToken) => inner.FindProfileByUserAsync(userId, cancellationToken);

        public Task<bool> StaffIdExistsAsync(string normalizedStaffId, Guid exceptProfileId, CancellationToken cancellationToken) =>
            inner.StaffIdExistsAsync(normalizedStaffId, exceptProfileId, cancellationToken);

        public Task<bool> DesignationAvailableAsync(Guid designationId, CancellationToken cancellationToken) =>
            inner.DesignationAvailableAsync(designationId, cancellationToken);

        public async Task<bool> HasOtherActiveAdminAsync(string exceptUserId, CancellationToken cancellationToken)
        {
            var found = await inner.HasOtherActiveAdminAsync(exceptUserId, cancellationToken);
            if (found)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }

            return found;
        }

        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, CancellationToken cancellationToken) => inner.GetRolesAsync(userId, cancellationToken);

        public Task StageRoleAsync(string userId, string roleName, bool assigned, CancellationToken cancellationToken) =>
            inner.StageRoleAsync(userId, roleName, assigned, cancellationToken);

        public void Add(TeacherProfile profile) => inner.Add(profile);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);

        public Task<TeacherDetail?> GetDetailAsync(Guid teacherId, CancellationToken cancellationToken) => inner.GetDetailAsync(teacherId, cancellationToken);

        public Task<TeacherListPage> ListAsync(TeacherListFilter filter, int page, int pageSize, CancellationToken cancellationToken) =>
            inner.ListAsync(filter, page, pageSize, cancellationToken);
    }

    internal sealed class NoopAuditRecorder : IAuditEventRecorder
    {
        public void Stage(AuditEventRequest request) { }

        public Task RecordSecurityAsync(AuditEventRequest request) => Task.CompletedTask;
    }

    internal sealed class EmptyAuditActorDirectory : IAuditActorDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }
}
