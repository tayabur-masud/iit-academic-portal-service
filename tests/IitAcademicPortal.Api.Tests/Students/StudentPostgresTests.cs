using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Students;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// Student Management on a real PostgreSQL 18 database, as the restricted runtime role: one profile per account, many admissions per
/// profile but one per Degree and Batch, unique non-null registration numbers, restrictive Configuration and Upazila references, and
/// academic history as text owned by its profile. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class StudentPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private sealed record Seed(Guid Degree, Guid Batch, Guid OtherBatch, Guid Hall, Guid Upazila, Guid CityCorporation);

    private static T Named<T>(T record) where T : ConfigurationRecord
    {
        record.Rename("PG " + Guid.NewGuid().ToString("N")[..10]);
        return record;
    }

    private async Task<Seed> SeedAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        var degree = Named(new Degree());
        degree.SetCreditsToBeCompleted(36);
        degree.SetMinimumCgpaForCompletion(2.50m);
        var semester = Named(new Semester());
        var session = Named(new AcademicSession());
        var division = Named(new Division());
        db.AddRange(degree, semester, session, division);
        await db.SaveChangesAsync();
        var batch = Named(new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id });
        var other = Named(new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id });
        var hall = Named(new Hall());
        var district = Named(new District { DivisionId = division.Id });
        db.AddRange(batch, other, hall, district);
        await db.SaveChangesAsync();
        var upazila = Named(new Upazila { DistrictId = district.Id });
        var city = Named(new CityCorporation { DistrictId = district.Id });
        db.AddRange(upazila, city);
        await db.SaveChangesAsync();
        return new Seed(degree.Id, batch.Id, other.Id, hall.Id, upazila.Id, city.Id);
    }

    private async Task<string> AddUserAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        var email = PortalApiFactory.UniqueEmail("pg-student");
        var user = new PortalUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString(),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static StudentProfile NewProfile(string userId, Guid? upazila = null)
    {
        var profile = StudentProfile.Create(userId);
        profile.ApplicantName = "PG Student";
        profile.ApplicantMobile = "01700000000";
        profile.MailingAddressDetails = upazila is null ? null : "House 1";
        profile.MailingUpazilaId = upazila;
        return profile;
    }

    private static StudentAdmissionRecord NewAdmission(Guid profileId, Seed seed, Guid? batch = null, string? registration = null)
    {
        var admission = StudentAdmissionRecord.Create(profileId);
        admission.DegreeId = seed.Degree;
        admission.BatchId = batch ?? seed.Batch;
        admission.AdmissionSerialNumber = "S";
        admission.AdmissionExamRoll = "R";
        admission.SetRegistrationNumber(registration, registration?.ToUpperInvariant());
        return admission;
    }

    private async Task<StudentProfile> AddProfileAsync(Seed seed, bool withAddress = false)
    {
        var userId = await AddUserAsync();
        await using var db = Db.CreateRuntimeContext();
        var profile = NewProfile(userId, withAddress ? seed.Upazila : null);
        db.StudentProfiles.Add(profile);
        await db.SaveChangesAsync();
        return profile;
    }

    private static async Task<string?> SqlStateAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<DbUpdateException>(action);
        return (error.InnerException as PostgresException)?.SqlState;
    }

    [PostgresFact]
    public async Task An_account_has_at_most_one_student_profile()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed);

        await using var db = Db.CreateRuntimeContext();
        db.StudentProfiles.Add(NewProfile(profile.UserId));

        Assert.Equal("23505", await SqlStateAsync(() => db.SaveChangesAsync()));
    }

    [PostgresFact]
    public async Task A_profile_has_many_admissions_but_only_one_per_degree_and_batch()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed);
        await using (var db = Db.CreateRuntimeContext())
        {
            db.StudentAdmissions.AddRange(NewAdmission(profile.Id, seed), NewAdmission(profile.Id, seed, seed.OtherBatch));
            await db.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateRuntimeContext();
        duplicate.StudentAdmissions.Add(NewAdmission(profile.Id, seed));

        Assert.Equal("23505", await SqlStateAsync(() => duplicate.SaveChangesAsync()));
        await using var read = Db.CreateRuntimeContext();
        Assert.Equal(2, await read.StudentAdmissions.CountAsync(a => a.StudentProfileId == profile.Id));
    }

    [PostgresFact]
    public async Task A_registration_number_is_unique_when_present_and_many_admissions_may_have_none()
    {
        var seed = await SeedAsync();
        var first = await AddProfileAsync(seed);
        var second = await AddProfileAsync(seed);
        var third = await AddProfileAsync(seed);
        var registration = "REG-" + Guid.NewGuid().ToString("N")[..8];
        await using (var db = Db.CreateRuntimeContext())
        {
            db.StudentAdmissions.AddRange(NewAdmission(first.Id, seed, registration: registration), NewAdmission(second.Id, seed), NewAdmission(third.Id, seed));
            await db.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateRuntimeContext();
        duplicate.StudentAdmissions.Add(NewAdmission(second.Id, seed, seed.OtherBatch, registration.ToLowerInvariant()));

        Assert.Equal("23505", await SqlStateAsync(() => duplicate.SaveChangesAsync()));
    }

    [PostgresFact]
    public async Task Degree_batch_hall_and_upazila_references_are_restrictive()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed, withAddress: true);
        await using (var db = Db.CreateRuntimeContext())
        {
            var admission = NewAdmission(profile.Id, seed);
            admission.AssignedHallId = seed.Hall;
            db.StudentAdmissions.Add(admission);
            await db.SaveChangesAsync();
        }

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var (table, id) in new[] { ("Halls", seed.Hall), ("Batches", seed.Batch), ("Upazilas", seed.Upazila) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"{table}\" WHERE \"Id\" = '{id}'"));
            Assert.Contains(error.SqlState, new[] { "23001", "23503" });
        }

        await using var bad = Db.CreateRuntimeContext();
        var orphan = NewAdmission(profile.Id, seed, seed.OtherBatch);
        orphan.AssignedHallId = Guid.NewGuid();
        bad.StudentAdmissions.Add(orphan);
        Assert.Equal("23503", await SqlStateAsync(() => bad.SaveChangesAsync()));
    }

    [PostgresFact]
    public async Task Academic_history_belongs_to_its_profile_and_keeps_text_exactly()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed);
        await using (var db = Db.CreateRuntimeContext())
        {
            var tracked = await db.StudentProfiles.Include(p => p.AcademicHistory).SingleAsync(p => p.Id == profile.Id);
            var entry = StudentAcademicHistory.Create(tracked.Id);
            entry.Category = AcademicHistoryCategory.SscOLevel;
            entry.Board = "Dhaka Board (old)";
            entry.Cgpa = "5.00";
            entry.MarksPercentage = "086.50";
            tracked.AcademicHistory.Add(entry);
            await db.SaveChangesAsync();
        }

        await using var read = Db.CreateRuntimeContext();
        var stored = await read.StudentAcademicHistoryEntries.AsNoTracking().SingleAsync(h => h.StudentProfileId == profile.Id);
        Assert.Equal("Dhaka Board (old)", stored.Board);
        Assert.Equal("5.00", stored.Cgpa);
        Assert.Equal("086.50", stored.MarksPercentage);
        Assert.Equal(AcademicHistoryCategory.SscOLevel, stored.Category);

        await using var bad = Db.CreateRuntimeContext();
        bad.StudentAcademicHistoryEntries.Add(StudentAcademicHistory.Create(Guid.NewGuid()));
        Assert.Equal("23503", await SqlStateAsync(() => bad.SaveChangesAsync()));
    }

    [PostgresFact]
    public async Task Academic_history_catalog_references_are_restrictive()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed);
        var institution = Named(new EducationalInstitution { Type = EducationalInstitutionType.University });
        var board = Named(new EducationBoard());
        var group = Named(new EducationGroup());
        await using (var db = Db.CreateRuntimeContext())
        {
            db.AddRange(institution, board, group);
            await db.SaveChangesAsync();
            var tracked = await db.StudentProfiles.Include(p => p.AcademicHistory).SingleAsync(p => p.Id == profile.Id);
            var entry = StudentAcademicHistory.Create(tracked.Id);
            entry.Category = AcademicHistoryCategory.Master;
            entry.UniversityId = institution.Id;
            entry.University = institution.Name;
            entry.BoardId = board.Id;
            entry.GroupId = group.Id;
            tracked.AcademicHistory.Add(entry);
            await db.SaveChangesAsync();
        }

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var (table, id) in new[] { ("EducationalInstitutions", institution.Id), ("EducationBoards", board.Id), ("EducationGroups", group.Id) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"{table}\" WHERE \"Id\" = '{id}'"));
            Assert.Contains(error.SqlState, new[] { "23001", "23503" });
        }
    }

    [PostgresFact]
    public async Task The_runtime_role_can_write_and_read_every_student_table_with_audit_columns_filled()
    {
        var seed = await SeedAsync();
        var profile = await AddProfileAsync(seed, withAddress: true);
        await using (var db = Db.CreateRuntimeContext())
        {
            db.StudentAdmissions.Add(NewAdmission(profile.Id, seed));
            await db.SaveChangesAsync();
            var tracked = await db.StudentProfiles.SingleAsync(p => p.Id == profile.Id);
            tracked.ApplicantMobile = "01800000000";
            await db.SaveChangesAsync();
        }

        await using var read = Db.CreateRuntimeContext();
        var stored = await read.StudentProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id);
        Assert.Equal("01800000000", stored.ApplicantMobile);
        Assert.InRange(stored.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
        Assert.True(stored.LastModifiedAtUtc >= stored.CreatedAtUtc);
    }

    [PostgresFact]
    public async Task An_address_may_reference_a_city_corporation_but_never_both_localities_and_the_reference_is_restrictive()
    {
        var seed = await SeedAsync();
        var userId = await AddUserAsync();
        var profile = NewProfile(userId);
        profile.MailingAddressDetails = "Holding 7";
        profile.MailingCityCorporationId = seed.CityCorporation;
        profile.PermanentAddressDetails = "Village 2";
        profile.PermanentUpazilaId = seed.Upazila;
        await using (var db = Db.CreateRuntimeContext())
        {
            db.StudentProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var column in new[] { "Mailing", "Permanent" })
        {
            var both = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(ddl,
                $"UPDATE \"StudentProfiles\" SET \"{column}UpazilaId\" = '{seed.Upazila}', \"{column}CityCorporationId\" = '{seed.CityCorporation}' WHERE \"Id\" = '{profile.Id}'"));
            Assert.Equal(PostgresErrorCodes.CheckViolation, both.SqlState);
        }

        var delete = await Assert.ThrowsAsync<PostgresException>(() =>
            PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"CityCorporations\" WHERE \"Id\" = '{seed.CityCorporation}'"));
        Assert.Contains(delete.SqlState, new[] { "23001", "23503" });

        await using var command = new NpgsqlCommand(
            "SELECT conname || ':' || confdeltype::text FROM pg_constraint WHERE contype = 'f' AND conrelid = '\"StudentProfiles\"'::regclass " +
            "AND confrelid = '\"CityCorporations\"'::regclass ORDER BY conname", ddl);
        var keys = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                keys.Add(reader.GetString(0));
            }
        }

        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Matches(":[ra]$", k));
    }

    [PostgresFact]
    public async Task The_address_locality_migration_keeps_every_existing_upazila_reference()
    {
        await using var database = await PostgresTestDatabase.CreateAsync();
        Guid upazila, profile;
        await using (var ddl = database.CreateContext(database.DdlConnectionString))
        {
            var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(ddl);
            await migrator.MigrateAsync("20261020183156_CityCorporationsDegreeMinimumCgpaAndGrades");
            await using var connection = await database.OpenAsync(database.DdlConnectionString);
            var division = Guid.NewGuid();
            var district = Guid.NewGuid();
            upazila = Guid.NewGuid();
            profile = Guid.NewGuid();
            await PostgresTestDatabase.ExecuteAsync(connection,
                $"INSERT INTO \"Divisions\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{division}', 'D', 'D', true);" +
                $"INSERT INTO \"Districts\" (\"Id\", \"DivisionId\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{district}', '{division}', 'T', 'T', true);" +
                $"INSERT INTO \"Upazilas\" (\"Id\", \"DistrictId\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{upazila}', '{district}', 'U', 'U', true);" +
                "INSERT INTO \"AspNetUsers\" (\"Id\", \"UserName\", \"NormalizedUserName\", \"Email\", \"NormalizedEmail\", \"EmailConfirmed\", " +
                "\"PhoneNumberConfirmed\", \"TwoFactorEnabled\", \"LockoutEnabled\", \"AccessFailedCount\") " +
                "VALUES ('old-student', 'old@iit.test', 'OLD@IIT.TEST', 'old@iit.test', 'OLD@IIT.TEST', false, false, false, false, 0);" +
                $"INSERT INTO \"StudentProfiles\" (\"Id\", \"UserId\", \"ApplicantName\", \"ApplicantMobile\", \"MailingAddressDetails\", \"MailingUpazilaId\", " +
                $"\"PermanentAddressDetails\", \"PermanentUpazilaId\") VALUES ('{profile}', 'old-student', 'Old', '01700000000', 'House', '{upazila}', 'Village', '{upazila}');");
            await migrator.MigrateAsync();
        }

        await using var verify = await database.OpenAsync(database.DdlConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT coalesce(\"MailingUpazilaId\"::text, '-') || '|' || coalesce(\"PermanentUpazilaId\"::text, '-') || '|' || " +
            $"coalesce(\"MailingCityCorporationId\"::text, '-') || '|' || coalesce(\"PermanentCityCorporationId\"::text, '-') FROM \"StudentProfiles\" WHERE \"Id\" = '{profile}'", verify);
        Assert.Equal($"{upazila}|{upazila}|-|-", await command.ExecuteScalarAsync());
    }
}
