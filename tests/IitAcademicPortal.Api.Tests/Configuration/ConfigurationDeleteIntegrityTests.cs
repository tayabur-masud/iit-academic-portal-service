using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// Configuration records are soft deleted: the row stays and is marked, deleted rows are hidden everywhere, and the unique
/// indexes ignore them so a name or code can be used again. The restrictive foreign keys still stop a physical delete of
/// anything that is referenced. Verified against a real PostgreSQL database. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class ConfigurationDeleteIntegrityTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private static T Named<T>(string name, T record) where T : ConfigurationRecord
    {
        record.Rename(name);
        if (record is Degree degree)
        {
            degree.SetCreditsToBeCompleted(120m);
        }
        return record;
    }

    private async Task SaveAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        await new ConfigurationStore(db).SaveAsync(CancellationToken.None);
    }

    private async Task<ConfigurationConflictKind> ConflictAsync(params ConfigurationRecord[] records)
    {
        await using var db = Db.CreateRuntimeContext();
        db.AddRange(records);
        var error = await Assert.ThrowsAsync<ConfigurationConflictException>(() => new ConfigurationStore(db).SaveAsync(CancellationToken.None));
        return error.Kind;
    }

    /// <summary>Marks a record deleted exactly as the application does, as the restricted runtime role.</summary>
    private async Task SoftDeleteAsync<T>(Guid id) where T : ConfigurationRecord
    {
        await using var db = Db.CreateRuntimeContext();
        var record = await db.Set<T>().SingleAsync(e => e.Id == id);
        record.MarkDeleted(DateTime.UtcNow, "admin-1");
        await db.SaveChangesAsync();
    }

    private async Task<List<T>> IncludingDeletedAsync<T>(Func<T, bool> where) where T : ConfigurationRecord
    {
        await using var db = Db.CreateRuntimeContext();
        return (await db.Set<T>().IgnoreQueryFilters().AsNoTracking().ToListAsync()).Where(where).ToList();
    }

    [PostgresFact]
    public async Task The_database_refuses_to_physically_delete_a_record_that_others_refer_to()
    {
        var (degree, semester, session) = (Named(Unique("Deg"), new Degree()), Named(Unique("Sem"), new Semester()), Named(Unique("Ses"), new AcademicSession()));
        var division = Named(Unique("Div"), new Division());
        await SaveAsync(degree, semester, session, division);
        var district = Named(Unique("Dist"), new District { DivisionId = division.Id });
        await SaveAsync(Named(Unique("Batch"), new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id }), district);
        await SaveAsync(Named(Unique("Upa"), new Upazila { DistrictId = district.Id }));

        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var (table, id) in new[] { ("Degrees", degree.Id), ("Semesters", semester.Id), ("AcademicSessions", session.Id), ("Divisions", division.Id), ("Districts", district.Id) })
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => PostgresTestDatabase.ExecuteAsync(connection, $"DELETE FROM \"{table}\" WHERE \"Id\" = '{id}'"));
            Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        }
    }

    [PostgresFact]
    public async Task A_deleted_record_stays_in_the_table_marked_with_when_and_by_whom_and_is_hidden_from_queries()
    {
        var hall = Named(Unique("Hall"), new Hall());
        await SaveAsync(hall);

        await SoftDeleteAsync<Hall>(hall.Id);

        await using var db = Db.CreateRuntimeContext();
        Assert.False(await db.Halls.AnyAsync(h => h.Id == hall.Id));
        var stored = Assert.Single(await IncludingDeletedAsync<Hall>(h => h.Id == hall.Id));
        Assert.True(stored.IsDeleted);
        Assert.Equal("admin-1", stored.DeletedByUserId);
        Assert.InRange(stored.DeletedAtUtc!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(DateTimeKind.Utc, stored.DeletedAtUtc.Value.Kind);
    }

    [PostgresFact]
    public async Task A_deleted_records_name_and_code_can_be_used_again_but_a_live_duplicate_is_still_refused()
    {
        var name = Unique("Reusable");
        var code = Unique("code").ToUpperInvariant().Replace(" ", "-");
        var first = Named(name, new Degree());
        first.SetCode(code);
        await SaveAsync(first);
        await SoftDeleteAsync<Degree>(first.Id);

        var second = Named(name, new Degree());
        second.SetCode(code);
        await SaveAsync(second);

        var third = Named(name.ToLowerInvariant(), new Degree());
        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(third));
        var withCode = Named(Unique("Other"), new Degree());
        withCode.SetCode(code.ToLowerInvariant());
        Assert.Equal(ConfigurationConflictKind.Duplicate, await ConflictAsync(withCode));
        Assert.Equal(2, (await IncludingDeletedAsync<Degree>(d => d.NormalizedName == name.ToUpperInvariant())).Count);
    }

    [PostgresFact]
    public async Task The_scoped_names_of_batches_districts_and_upazilas_are_free_again_after_a_delete()
    {
        var (semester, session, degree) = (Named(Unique("Sem"), new Semester()), Named(Unique("Ses"), new AcademicSession()), Named(Unique("Deg"), new Degree()));
        var division = Named(Unique("Div"), new Division());
        await SaveAsync(semester, session, degree, division);
        Batch Batch(string name) => Named(name, new Batch { DegreeId = degree.Id, StartingSemesterId = semester.Id, AcademicSessionId = session.Id });
        var batchName = Unique("Batch");
        var district = Named(Unique("Dist"), new District { DivisionId = division.Id });
        await SaveAsync(Batch(batchName), district);
        var upazila = Named(Unique("Upa"), new Upazila { DistrictId = district.Id });
        await SaveAsync(upazila);
        var districtName = district.Name;

        await SoftDeleteAsync<Upazila>(upazila.Id);
        await SaveAsync(Named(upazila.Name, new Upazila { DistrictId = district.Id }));
        var oldBatch = Assert.Single(await IncludingDeletedAsync<Batch>(b => b.Name == batchName));
        await SoftDeleteAsync<Batch>(oldBatch.Id);
        await SaveAsync(Batch(batchName));
        await SoftDeleteAsync<District>(district.Id);
        await SaveAsync(Named(districtName, new District { DivisionId = division.Id }));
    }

    [PostgresFact]
    public async Task The_unique_indexes_apply_only_to_live_rows()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using var command = new NpgsqlCommand(
            "SELECT tablename || ':' || indexdef FROM pg_indexes WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%' " +
            "AND indexname NOT LIKE 'PK%' AND tablename IN ('Halls','Degrees','Batches','Districts','Upazilas','Divisions','Semesters','AcademicSessions','Designations','EducationBoards','EducationGroups','ExaminationTypes','EducationalInstitutions')",
            connection);
        var unique = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                unique.Add(reader.GetString(0));
            }
        }

        Assert.NotEmpty(unique);
        Assert.All(unique, definition => Assert.Contains("\"IsDeleted\" = false", definition));
        // 10 catalogs with a globally unique name, 5 unique codes, and 3 names unique within a parent (batches, districts, upazilas).
        Assert.Equal(10 + 5 + 3, unique.Count);
    }
}
