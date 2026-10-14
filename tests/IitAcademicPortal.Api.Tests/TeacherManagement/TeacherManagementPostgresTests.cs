using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Teachers;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
    public async Task Concurrent_removals_cannot_leave_the_portal_without_an_active_Admin()
    {
        for (var round = 0; round < 5; round++)
        {
            // Only these two accounts count: earlier rounds' Admins are removed from the role by the loop below.
            await using (var clean = await Db.OpenAsync(Db.DdlConnectionString))
            {
                await PostgresTestDatabase.ExecuteAsync(clean,
                    "DELETE FROM \"AspNetUserRoles\" WHERE \"RoleId\" IN (SELECT \"Id\" FROM \"AspNetRoles\" WHERE \"Name\" = 'Admin')");
            }

            var a = await AddUserAsync(TeacherAccountStatus.Active, PortalRoles.Admin);
            var b = await AddUserAsync(TeacherAccountStatus.Active, PortalRoles.Admin);
            using var barrier = new Barrier(2);

            var results = await Task.WhenAll(
                Task.Run(() => TryRemoveAdminAsync(a.Id, barrier)),
                Task.Run(() => TryRemoveAdminAsync(b.Id, barrier)));

            Assert.Equal(1, results.Count(removed => removed));
            await using var db = Db.CreateRuntimeContext();
            var remaining = await db.UserRoles
                .Join(db.Roles.Where(r => r.Name == PortalRoles.Admin), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
                .ToListAsync();
            Assert.Single(remaining);
        }
    }

    // The same sequence the role and deactivation services run: transaction, lock, check, change, commit.
    private async Task<bool> TryRemoveAdminAsync(string userId, Barrier barrier)
    {
        await using var db = Db.CreateRuntimeContext();
        var store = new TeacherStore(db);
        await using var transaction = await store.BeginTransactionAsync(CancellationToken.None);
        barrier.SignalAndWait(TimeSpan.FromSeconds(10));
        await store.LockAdminInvariantAsync(CancellationToken.None);
        if (!await store.HasOtherActiveAdminAsync(userId, CancellationToken.None))
        {
            return false;
        }

        await Task.Delay(100);
        await store.StageRoleAsync(userId, PortalRoles.Admin, false, CancellationToken.None);
        await db.SaveChangesAsync();
        await transaction.CommitAsync(CancellationToken.None);
        return true;
    }
}
