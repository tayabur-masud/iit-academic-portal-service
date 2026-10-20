using IitAcademicPortal.Api.Tests.Infrastructure;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The created and last-modified columns as PostgreSQL has them: present on every configuration table, with required times that
/// default to the database clock, so a row added by a SQL seed script still gets a time and, having no signed-in account, no
/// "by". Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class AuditedEntityPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly string[] Tables =
    [
        "EducationalInstitutions", "EducationBoards", "EducationGroups", "ExaminationTypes", "Degrees", "Semesters", "AcademicSessions",
        "Designations", "Halls", "Batches", "Divisions", "Districts", "Upazilas", "CityCorporations", "Grades",
    ];

    private PostgresTestDatabase Db => fixture.Database!;

    [PostgresFact]
    public async Task Every_audited_table_has_required_default_times_and_optional_bounded_account_columns()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        foreach (var table in Tables)
        {
            await using var command = new NpgsqlCommand(
                "SELECT column_name, is_nullable, column_default, data_type, character_maximum_length FROM information_schema.columns " +
                "WHERE table_schema = 'public' AND table_name = @table AND column_name IN ('CreatedAtUtc','CreatedByUserId','LastModifiedAtUtc','LastModifiedByUserId')",
                connection);
            command.Parameters.AddWithValue("table", table);
            var columns = new Dictionary<string, (string Nullable, string? Default, string Type, int? Length)>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    columns[reader.GetString(0)] = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4));
                }
            }

            Assert.True(columns.Count == 4, $"{table} has {columns.Count} of the 4 audit columns.");
            foreach (var time in new[] { "CreatedAtUtc", "LastModifiedAtUtc" })
            {
                Assert.Equal("NO", columns[time].Nullable);
                Assert.Equal("timestamp with time zone", columns[time].Type);
                Assert.Equal("now()", columns[time].Default);
            }

            foreach (var by in new[] { "CreatedByUserId", "LastModifiedByUserId" })
            {
                Assert.Equal("YES", columns[by].Nullable);
                Assert.Equal(450, columns[by].Length);
            }
        }
    }

    [PostgresFact]
    public async Task A_row_added_by_sql_gets_the_database_time_and_no_account()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        var id = Guid.NewGuid();
        await PostgresTestDatabase.ExecuteAsync(connection,
            $"INSERT INTO \"Designations\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{id}', 'Seeded by SQL', 'SEEDED BY SQL', true)");

        await using var command = new NpgsqlCommand(
            "SELECT \"CreatedAtUtc\", \"LastModifiedAtUtc\", \"CreatedByUserId\", \"LastModifiedByUserId\" FROM \"Designations\" WHERE \"Id\" = @id", connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        var (created, modified) = (reader.GetDateTime(0), reader.GetDateTime(1));
        Assert.InRange(created, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
        Assert.Equal(created, modified);
        Assert.True(reader.IsDBNull(2));
        Assert.True(reader.IsDBNull(3));
    }
}
