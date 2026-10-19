using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The saved hall seed script (database/postgresql/seed-halls.sql), run against a real PostgreSQL database built with the
/// real migrations: the same fixed identifiers on every new database, safe to run again, and consistent with the
/// application's own name rules. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class HallSeedScriptTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private const string DefaultAddress = "University of Dhaka, Dhaka-1000, Bangladesh";

    private PostgresTestDatabase Db => fixture.Database!;

    private static readonly (string Id, string Name)[] Known =
    [
        ("c701857b-ebcd-5510-b493-b51fa840dc0f", "Salimullah Muslim Hall"),
        ("54f2bdaa-0ff5-5d83-8a0b-8f4e81e0a397", "Dr. Muhammad Shahidullah Hall"),
        ("3cee167a-a69a-5713-b61b-085781526e53", "Jagannath Hall"),
    ];

    private async Task ResetAsync()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        // CASCADE: student admissions may refer to halls, and PostgreSQL refuses to truncate a referenced table even when the
        // referencing table is empty (it is in this test's own database).
        await PostgresTestDatabase.ExecuteAsync(connection, "TRUNCATE TABLE \"Halls\" CASCADE");
    }

    private async Task RunScriptAsync()
    {
        var script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "postgresql", "seed-halls.sql"));
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<Hall>> HallsAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        return await db.Halls.AsNoTracking().OrderBy(h => h.Name).ToListAsync();
    }

    [PostgresFact]
    public async Task It_seeds_the_halls_with_fixed_ids_the_default_address_and_no_codes()
    {
        await ResetAsync();

        await RunScriptAsync();

        var halls = await HallsAsync();
        Assert.Equal(23, halls.Count);
        Assert.All(halls, h =>
        {
            Assert.Equal(DefaultAddress, h.Address);
            Assert.Null(h.Code);
            Assert.Null(h.NormalizedCode);
            Assert.True(h.IsActive);
        });
        foreach (var (id, name) in Known)
        {
            Assert.Equal(Guid.Parse(id), Assert.Single(halls, h => h.Name == name).Id);
        }
    }

    [PostgresFact]
    public async Task The_normalized_names_match_the_rule_the_application_uses()
    {
        await ResetAsync();

        await RunScriptAsync();

        var halls = await HallsAsync();
        Assert.All(halls, h => Assert.Equal(NameNormalizer.Normalize(h.Name), h.NormalizedName));
        Assert.Equal(halls.Count, halls.Select(h => h.NormalizedName).Distinct().Count());
        Assert.Contains(halls, h => h.Name == "Dr. Qudrat-E-Khuda Hostel");
        Assert.Contains(halls, h => h.Name == "IBA Hostel");
        Assert.Contains(halls, h => h.Name == "Nabab Foyzunnessa Chowdhurani Chhatrinibash");
    }

    [PostgresFact]
    public async Task Running_it_again_changes_nothing_and_keeps_what_an_admin_edited()
    {
        await ResetAsync();
        await RunScriptAsync();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection,
                "UPDATE \"Halls\" SET \"Address\" = 'Edited address', \"IsActive\" = false, \"Code\" = 'JH', \"NormalizedCode\" = 'JH' " +
                "WHERE \"Id\" = '3cee167a-a69a-5713-b61b-085781526e53'");
        }

        await RunScriptAsync();

        var halls = await HallsAsync();
        Assert.Equal(23, halls.Count);
        var edited = Assert.Single(halls, h => h.Id == Guid.Parse("3cee167a-a69a-5713-b61b-085781526e53"));
        Assert.Equal("Edited address", edited.Address);
        Assert.False(edited.IsActive);
        Assert.Equal("JH", edited.Code);
    }

    [PostgresFact]
    public async Task A_hall_an_admin_already_created_with_the_same_name_is_left_alone_and_the_rest_are_still_added()
    {
        await ResetAsync();
        var existing = Guid.NewGuid();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection,
                $"INSERT INTO \"Halls\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{existing}', 'jagannath   hall', 'JAGANNATH HALL', true)");
        }

        await RunScriptAsync();

        var halls = await HallsAsync();
        Assert.Equal(23, halls.Count);
        var jagannath = Assert.Single(halls, h => h.NormalizedName == "JAGANNATH HALL");
        Assert.Equal(existing, jagannath.Id);
        Assert.Equal("jagannath   hall", jagannath.Name);
    }

    [PostgresFact]
    public async Task A_seeded_hall_an_admin_deleted_stays_deleted_when_the_script_runs_again()
    {
        await ResetAsync();
        await RunScriptAsync();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection,
                "UPDATE \"Halls\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now(), \"DeletedByUserId\" = 'admin-1' WHERE \"Id\" = '3cee167a-a69a-5713-b61b-085781526e53'");
        }

        await RunScriptAsync();

        Assert.Equal(22, (await HallsAsync()).Count);
        await using var db = Db.CreateRuntimeContext();
        var all = await db.Halls.IgnoreQueryFilters().AsNoTracking().ToListAsync();
        Assert.Equal(23, all.Count);
        Assert.True(Assert.Single(all, h => h.Name == "Jagannath Hall").IsDeleted);
    }
}
