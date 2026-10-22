using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.Staff;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Staff;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Staff;

/// <summary>
/// Staff profiles on a real PostgreSQL 18 database, as the restricted runtime role: the StaffProfiles table and its additive
/// <c>IsActive</c> migration, one profile per account, restrictive keys to the account and the Designation, and the account, profile,
/// audit, and session changes of provisioning and deactivation committing together or not at all. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class StaffManagementPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private const string ProfilesMigration = "20261021174956_StaffProfiles";

    private PostgresTestDatabase Db => fixture.Database!;

    private static async Task<(string UserId, Guid Designation)> SeedAsync(PostgresTestDatabase database)
    {
        await using var db = database.CreateRuntimeContext();
        var email = PortalApiFactory.UniqueEmail("pg-staff");
        var user = new PortalUser
        {
            Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), AccountStatus = TeacherAccountStatus.Active,
        };
        db.Users.Add(user);
        var designation = new Designation();
        designation.Rename("PG Designation " + Guid.NewGuid().ToString("N")[..8]);
        db.Designations.Add(designation);
        await db.SaveChangesAsync();
        var adminRole = await db.Roles.SingleAsync(r => r.Name == PortalRoles.Admin);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = adminRole.Id });
        await db.SaveChangesAsync();
        return (user.Id, designation.Id);
    }

    private static async Task<List<string>> RowsAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    /// <summary>The application services over the runtime role, with audit events staged into the same unit of work.</summary>
    private static ServiceProvider Services(PostgresTestDatabase database, RecordingNotifications? notifications = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        services.AddIdentityCore<PortalUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<PortalDbContext>();
        services.AddScoped(_ => database.CreateRuntimeContext());
        services.AddScoped<IStaffStore, StaffStore>();
        services.AddScoped<IAuditEventStore, AuditEventStore>();
        services.AddScoped<IAuditEventRecorder, DirectRecorder>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddSingleton<IAuditRequestContext, NoRequest>();
        services.AddSingleton<IAuditActorDirectory, NoActors>();
        services.AddSingleton<INotificationRequestService>(notifications ?? new RecordingNotifications());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<StaffProfileService>();
        services.AddScoped<StaffLifecycleService>();
        return services.BuildServiceProvider();
    }

    private static async Task FailStaffAuditAsync(PostgresTestDatabase database, string pattern)
    {
        await using var ddl = await database.OpenAsync(database.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(ddl, $"""
            CREATE OR REPLACE FUNCTION fail_staff_audit() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'forced audit failure'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER fail_staff_audit BEFORE INSERT ON "AuditEvents" FOR EACH ROW WHEN (NEW."EventType" LIKE '{pattern}') EXECUTE FUNCTION fail_staff_audit();
            """);
    }

    [PostgresFact]
    public async Task One_profile_per_account_with_restrictive_account_and_designation_keys_and_a_required_active_flag()
    {
        var (userId, designation) = await SeedAsync(Db);
        await using (var db = Db.CreateRuntimeContext())
        {
            db.StaffProfiles.Add(StaffProfile.Create(userId, "PG Staff", "01700000000", designation));
            await db.SaveChangesAsync();
        }

        await using (var db = Db.CreateRuntimeContext())
        {
            Assert.True((await db.StaffProfiles.SingleAsync(p => p.UserId == userId)).IsActive);
            db.StaffProfiles.Add(StaffProfile.Create(userId, "Second", "01700000001", designation));
            var duplicate = await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.Equal(PostgresErrorCodes.UniqueViolation, (duplicate.InnerException as PostgresException)?.SqlState);
        }

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var (table, id) in new[] { ("AspNetUsers", userId), ("Designations", designation.ToString()) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"{table}\" WHERE \"Id\" = '{id}'"));
            Assert.Contains(error.SqlState, new[] { "23001", "23503" });
        }

        var keys = await RowsAsync(ddl,
            "SELECT confrelid::regclass::text || ':' || confdeltype::text FROM pg_constraint WHERE contype = 'f' " +
            "AND conrelid = '\"StaffProfiles\"'::regclass ORDER BY 1");
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Matches(":[ra]$", k));
        var columns = await RowsAsync(ddl,
            "SELECT column_name || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-') FROM information_schema.columns " +
            "WHERE table_name = 'StaffProfiles' AND column_name IN ('UserId', 'Name', 'MobileNumber', 'DesignationId', 'IsActive', 'CreatedAtUtc', 'LastModifiedAtUtc') ORDER BY 1");
        Assert.Equal(
            ["CreatedAtUtc:NO:-", "DesignationId:NO:-", "IsActive:NO:-", "LastModifiedAtUtc:NO:-", "MobileNumber:NO:50", "Name:NO:200", "UserId:NO:450"],
            columns);
        Assert.Empty(await RowsAsync(ddl, "SELECT column_name FROM information_schema.columns WHERE table_name = 'StaffProfiles' AND column_name ILIKE '%verif%'"));
    }

    [PostgresFact]
    public async Task The_lifecycle_migration_makes_existing_profiles_active_without_touching_them()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid profileId;
        await using (var ddl = database.CreateContext(database.DdlConnectionString))
        {
            var migrator = ddl.GetService<IMigrator>();
            await migrator.MigrateAsync(ProfilesMigration);
            var (userId, designation) = await SeedAsync(database);
            profileId = Guid.NewGuid();
            await using var connection = await database.OpenAsync(database.DdlConnectionString);
            await PostgresTestDatabase.ExecuteAsync(connection,
                "INSERT INTO \"StaffProfiles\" (\"Id\", \"UserId\", \"Name\", \"MobileNumber\", \"DesignationId\") " +
                $"VALUES ('{profileId}', '{userId}', 'Before Migration', '01700000000', '{designation}')");
            await migrator.MigrateAsync();
        }

        await using var verify = database.CreateRuntimeContext();
        var profile = await verify.StaffProfiles.SingleAsync(p => p.Id == profileId);
        Assert.True(profile.IsActive);
        Assert.Equal("Before Migration", profile.Name);
    }

    [PostgresFact]
    public async Task Provisioning_a_new_account_commits_account_profile_and_audit_together_or_not_at_all()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (_, designation) = await SeedAsync(database);
        var email = PortalApiFactory.UniqueEmail("pg-new-staff");
        var notifications = new RecordingNotifications();
        await FailStaffAuditAsync(database, "staff.%");

        await using (var provider = Services(database, notifications))
        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => scope.ServiceProvider.GetRequiredService<StaffProfileService>()
                .CreateAsync(new StaffInput(email, "PG New Staff", "01700000000", designation, true), CancellationToken.None));
        }

        await using (var verify = database.CreateRuntimeContext())
        {
            Assert.Equal(0, await verify.Users.CountAsync(u => u.Email == email));
            Assert.Equal(0, await verify.StaffProfiles.CountAsync(p => p.Name == "PG New Staff"));
            Assert.Equal(0, await verify.AuditEvents.CountAsync(e => e.EventType == "account.created"));
        }

        Assert.Empty(notifications.Requests);

        await using (var ddl = await database.OpenAsync(database.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(ddl, "DROP TRIGGER fail_staff_audit ON \"AuditEvents\"");
        }

        await using (var provider = Services(database, notifications))
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<StaffProfileService>()
                .CreateAsync(new StaffInput(email, "PG New Staff", "01700000000", designation, true), CancellationToken.None);
            Assert.Equal(StaffOutcome.Created, result.Outcome);
        }

        await using (var verify = database.CreateRuntimeContext())
        {
            var user = await verify.Users.SingleAsync(u => u.Email == email);
            Assert.Equal(TeacherAccountStatus.PendingCredentialSetup, user.AccountStatus);
            var profile = await verify.StaffProfiles.SingleAsync(p => p.UserId == user.Id);
            Assert.True(profile.IsActive);
            Assert.Equal(1, await verify.AuditEvents.CountAsync(e => e.EventType == "account.created" && e.EntityId == user.Id));
            Assert.Equal(1, await verify.AuditEvents.CountAsync(e => e.EventType == "staff.profile.created" && e.EntityId == profile.Id.ToString()));
            Assert.Equal(user.Id, Assert.Single(notifications.Requests).RecipientUserIds.Single());
        }
    }

    [PostgresFact]
    public async Task Deactivation_commits_profile_account_sessions_and_audit_together_or_not_at_all()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        var (keeper, designation) = await SeedAsync(database);
        var (userId, _) = await SeedAsync(database);
        Guid profileId;
        await using (var db = database.CreateRuntimeContext())
        {
            var profile = StaffProfile.Create(userId, "PG Leaving", "01700000000", designation);
            db.StaffProfiles.Add(profile);
            db.AuthSessions.Add(new Domain.Sessions.AuthSession(userId, Guid.NewGuid().ToString("N"), PortalRoles.Admin, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync();
            profileId = profile.Id;
        }

        Assert.NotEqual(keeper, userId);
        await FailStaffAuditAsync(database, "staff.profile.deactivated");
        await using (var provider = Services(database))
        await using (var scope = provider.CreateAsyncScope())
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                scope.ServiceProvider.GetRequiredService<StaffLifecycleService>().DeactivateAsync(profileId, CancellationToken.None));
        }

        await using (var verify = database.CreateRuntimeContext())
        {
            Assert.True((await verify.StaffProfiles.SingleAsync(p => p.Id == profileId)).IsActive);
            Assert.Equal(TeacherAccountStatus.Active, (await verify.Users.SingleAsync(u => u.Id == userId)).AccountStatus);
            Assert.Equal(1, await verify.AuthSessions.CountAsync(s => s.UserId == userId && s.RevokedAt == null));
        }

        await using (var ddl = await database.OpenAsync(database.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(ddl, "DROP TRIGGER fail_staff_audit ON \"AuditEvents\"");
        }

        await using (var provider = Services(database))
        await using (var scope = provider.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<StaffLifecycleService>().DeactivateAsync(profileId, CancellationToken.None);
            Assert.Equal(StaffOutcome.Ok, result.Outcome);
        }

        await using (var verify = database.CreateRuntimeContext())
        {
            Assert.False((await verify.StaffProfiles.SingleAsync(p => p.Id == profileId)).IsActive);
            Assert.Equal(TeacherAccountStatus.Deactivated, (await verify.Users.SingleAsync(u => u.Id == userId)).AccountStatus);
            Assert.Equal(0, await verify.AuthSessions.CountAsync(s => s.UserId == userId && s.RevokedAt == null));
            Assert.Equal(1, await verify.AuditEvents.CountAsync(e => e.EventType == "staff.profile.deactivated" && e.EntityId == profileId.ToString()));
        }
    }

    /// <summary>Stages business events into the same unit of work, as the application's recorder does, without a request.</summary>
    private sealed class DirectRecorder(IAuditEventStore store) : IAuditEventRecorder
    {
        public void Stage(AuditEventRequest request) =>
            store.Add(AuditEventFactory.Create(request, new NoRequest(), DateTime.UtcNow, strict: true));

        public Task RecordSecurityAsync(AuditEventRequest request) => Task.CompletedTask;
    }

    private sealed class NoRequest : IAuditRequestContext
    {
        public string? ActorUserId => null;

        public string? CorrelationId => null;

        public string? Source => null;
    }

    private sealed class NoActors : IAuditActorDirectory
    {
        public Task<IReadOnlyDictionary<string, string>> GetEmailsAsync(IReadOnlyCollection<string> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());
    }

    /// <summary>Records each setup request instead of storing it; this suite checks only that it follows the commit.</summary>
    private sealed class RecordingNotifications : INotificationRequestService
    {
        public List<Application.Notifications.NotificationEventRequest> Requests { get; } = [];

        public Task<NotificationRequestResult> RequestAsync(Application.Notifications.NotificationEventRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new NotificationRequestResult(NotificationRequestStatus.Accepted, Accepted: 1));
        }
    }
}
