using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The saved geography seed scripts (seed-divisions.sql, seed-districts.sql, seed-upazilas.sql, seed-city-corporations.sql) run against a real PostgreSQL
/// database built with the real migrations: complete, correctly parented, the same fixed identifiers on every new database,
/// safe to run again, and consistent with the application's name rules. Set AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class GeographySeedScriptTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private PostgresTestDatabase Db => fixture.Database!;

    private async Task ResetAsync()
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        // CASCADE: student addresses may refer to upazilas, and PostgreSQL refuses to truncate a referenced table even when the
        // referencing table is empty (it is in this test's own database).
        await PostgresTestDatabase.ExecuteAsync(connection, "TRUNCATE TABLE \"Upazilas\", \"Districts\", \"Divisions\" CASCADE");
    }

    private async Task RunAsync(string script)
    {
        var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "postgresql", script));
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await using var command = new NpgsqlCommand(text, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task RunAllAsync()
    {
        await RunAsync("seed-divisions.sql");
        await RunAsync("seed-districts.sql");
        await RunAsync("seed-upazilas.sql");
    }

    private async Task<(List<Division> Divisions, List<District> Districts, List<Upazila> Upazilas)> ReadAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        return (
            await db.Divisions.AsNoTracking().ToListAsync(),
            await db.Districts.AsNoTracking().ToListAsync(),
            await db.Upazilas.AsNoTracking().ToListAsync());
    }

    [PostgresFact]
    public async Task The_scripts_load_the_whole_hierarchy_with_fixed_ids()
    {
        await ResetAsync();

        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAsync();
        Assert.Equal(8, divisions.Count);
        Assert.Equal(64, districts.Count);
        Assert.Equal(492, upazilas.Count);
        Assert.Equal(Guid.Parse("dd259409-a0db-53e5-8cf4-5716b39cd176"), Assert.Single(divisions, d => d.Name == "Dhaka").Id);
        Assert.Equal(Guid.Parse("60233582-651c-55d7-a9cd-0df2a60de33e"), Assert.Single(districts, d => d.Name == "Gazipur").Id);
        Assert.Equal(Guid.Parse("9aeb31cc-0977-50f5-8987-7ab6c9d0723a"), Assert.Single(upazilas, u => u.Name == "Kaliakior").Id);
        Assert.All(divisions.Cast<ConfigurationRecord>().Concat(districts).Concat(upazilas), r => Assert.True(r.IsActive));
    }

    [PostgresFact]
    public async Task Every_record_sits_under_its_own_parent()
    {
        await ResetAsync();
        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAsync();
        var division = divisions.ToDictionary(d => d.Id, d => d.Name);
        var district = districts.ToDictionary(d => d.Id);

        Assert.Equal("Dhaka", division[Assert.Single(districts, d => d.Name == "Gazipur").DivisionId]);
        Assert.Equal("Mymensingh", division[Assert.Single(districts, d => d.Name == "Jamalpur").DivisionId]);
        Assert.Equal("Rajshahi", division[Assert.Single(districts, d => d.Name == "Natore").DivisionId]);
        Assert.Equal(13, districts.Count(d => division[d.DivisionId] == "Dhaka"));
        Assert.Equal(4, districts.Count(d => division[d.DivisionId] == "Mymensingh"));

        string DistrictOf(Upazila u) => district[u.DistrictId].Name;
        Assert.Equal("Gazipur", DistrictOf(Assert.Single(upazilas, u => u.Name == "Kaliakior")));
        Assert.Equal("Narsingdi", DistrictOf(Assert.Single(upazilas, u => u.Name == "Raipura")));
        Assert.Equal("Cox's Bazar", DistrictOf(Assert.Single(upazilas, u => u.Name == "Cox's Bazar Sadar")));
        // Natore's two repeated rows in the source are left out, so it has its four distinct upazilas.
        Assert.Equal(["Bagatipara", "Baraigram", "Lalpur", "Natore Sadar"], upazilas
            .Where(u => DistrictOf(u) == "Natore").Select(u => u.Name).Order().ToList());
        Assert.All(upazilas, u => Assert.True(district.ContainsKey(u.DistrictId)));
    }

    [PostgresFact]
    public async Task The_normalized_names_match_the_application_rule_and_are_unique_in_their_scope()
    {
        await ResetAsync();
        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAsync();
        Assert.All(divisions.Cast<ConfigurationRecord>().Concat(districts).Concat(upazilas),
            r => Assert.Equal(NameNormalizer.Normalize(r.Name), r.NormalizedName));
        Assert.Equal(divisions.Count, divisions.Select(d => d.NormalizedName).Distinct().Count());
        Assert.Equal(districts.Count, districts.Select(d => (d.DivisionId, d.NormalizedName)).Distinct().Count());
        Assert.Equal(upazilas.Count, upazilas.Select(u => (u.DistrictId, u.NormalizedName)).Distinct().Count());
    }

    [PostgresFact]
    public async Task Running_the_scripts_again_changes_nothing_and_keeps_what_an_admin_edited()
    {
        await ResetAsync();
        await RunAllAsync();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection, "UPDATE \"Districts\" SET \"IsActive\" = false WHERE \"Id\" = '60233582-651c-55d7-a9cd-0df2a60de33e'");
            await PostgresTestDatabase.ExecuteAsync(connection, "UPDATE \"Upazilas\" SET \"Name\" = 'Kaliakoir', \"NormalizedName\" = 'KALIAKOIR' WHERE \"Id\" = '9aeb31cc-0977-50f5-8987-7ab6c9d0723a'");
        }

        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAsync();
        Assert.Equal((8, 64), (divisions.Count, districts.Count));
        Assert.False(Assert.Single(districts, d => d.Id == Guid.Parse("60233582-651c-55d7-a9cd-0df2a60de33e")).IsActive);
        Assert.Equal("Kaliakoir", Assert.Single(upazilas, u => u.Id == Guid.Parse("9aeb31cc-0977-50f5-8987-7ab6c9d0723a")).Name);
        // The renamed upazila is not re-added under its old name: its seeded row is still its own.
        Assert.Equal(492, upazilas.Count);
    }

    [PostgresFact]
    public async Task A_division_that_already_exists_under_another_id_is_used_as_the_parent()
    {
        await ResetAsync();
        var existing = Guid.NewGuid();
        await using (var connection = await Db.OpenAsync(Db.DdlConnectionString))
        {
            await PostgresTestDatabase.ExecuteAsync(connection,
                $"INSERT INTO \"Divisions\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{existing}', 'dhaka', 'DHAKA', true)");
        }

        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAsync();
        Assert.Equal(8, divisions.Count);
        Assert.Equal(existing, Assert.Single(divisions, d => d.NormalizedName == "DHAKA").Id);
        Assert.Equal(13, districts.Count(d => d.DivisionId == existing));
        Assert.Equal((64, 492), (districts.Count, upazilas.Count));
    }

    [PostgresFact]
    public async Task Districts_without_their_divisions_stop_with_an_error_and_add_nothing()
    {
        await ResetAsync();

        var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync("seed-districts.sql"));

        Assert.Contains("seed-divisions.sql", error.MessageText);
        var (_, districts, _) = await ReadAsync();
        Assert.Empty(districts);
    }

    [PostgresFact]
    public async Task Upazilas_without_their_districts_stop_with_an_error_and_add_nothing()
    {
        await ResetAsync();
        await RunAsync("seed-divisions.sql");

        var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync("seed-upazilas.sql"));

        Assert.Contains("seed-districts.sql", error.MessageText);
        var (_, _, upazilas) = await ReadAsync();
        Assert.Empty(upazilas);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = await Db.OpenAsync(Db.DdlConnectionString);
        await PostgresTestDatabase.ExecuteAsync(connection, sql);
    }

    private async Task<(List<Division> Divisions, List<District> Districts, List<Upazila> Upazilas)> ReadAllAsync()
    {
        await using var db = Db.CreateRuntimeContext();
        return (
            await db.Divisions.IgnoreQueryFilters().AsNoTracking().ToListAsync(),
            await db.Districts.IgnoreQueryFilters().AsNoTracking().ToListAsync(),
            await db.Upazilas.IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    private const string DhakaDivision = "dd259409-a0db-53e5-8cf4-5716b39cd176";

    [PostgresFact]
    public async Task Records_an_admin_deleted_stay_deleted_when_all_three_scripts_run_again()
    {
        await ResetAsync();
        await RunAllAsync();
        // Delete Dhaka bottom-up, as the portal would (a record with live dependents cannot be deleted).
        await ExecuteAsync(
            "UPDATE \"Upazilas\" SET \"IsDeleted\" = true WHERE \"DistrictId\" IN (SELECT \"Id\" FROM \"Districts\" WHERE \"DivisionId\" = '" + DhakaDivision + "');" +
            "UPDATE \"Districts\" SET \"IsDeleted\" = true WHERE \"DivisionId\" = '" + DhakaDivision + "';" +
            "UPDATE \"Divisions\" SET \"IsDeleted\" = true WHERE \"Id\" = '" + DhakaDivision + "';");

        await RunAllAsync();

        var (divisions, districts, upazilas) = await ReadAllAsync();
        Assert.Equal((8, 64, 492), (divisions.Count, districts.Count, upazilas.Count));
        Assert.Equal(1, divisions.Count(d => d.IsDeleted));
        Assert.Equal(13, districts.Count(d => d.IsDeleted));
        var (live, _, liveUpazilas) = await ReadAsync();
        Assert.Equal(7, live.Count);
        Assert.DoesNotContain(liveUpazilas, u => u.IsDeleted);
    }

    [PostgresFact]
    public async Task A_live_division_is_the_parent_even_when_a_deleted_one_has_the_same_name()
    {
        await ResetAsync();
        await RunAsync("seed-divisions.sql");
        var live = Guid.NewGuid();
        await ExecuteAsync(
            "UPDATE \"Divisions\" SET \"IsDeleted\" = true WHERE \"Id\" = '" + DhakaDivision + "';" +
            $"INSERT INTO \"Divisions\" (\"Id\", \"Name\", \"NormalizedName\", \"IsActive\") VALUES ('{live}', 'Dhaka', 'DHAKA', true);");

        await RunAsync("seed-districts.sql");
        await RunAsync("seed-upazilas.sql");

        var (_, districts, upazilas) = await ReadAllAsync();
        Assert.Equal(13, districts.Count(d => d.DivisionId == live));
        Assert.Equal(0, districts.Count(d => d.DivisionId == Guid.Parse(DhakaDivision)));
        Assert.Equal((64, 492), (districts.Count, upazilas.Count));
    }

    [PostgresFact]
    public async Task Districts_are_never_added_under_a_deleted_division()
    {
        await ResetAsync();
        await RunAsync("seed-divisions.sql");
        await ExecuteAsync("UPDATE \"Divisions\" SET \"IsDeleted\" = true WHERE \"Id\" = '" + DhakaDivision + "'");

        var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync("seed-districts.sql"));

        Assert.Contains("deleted", error.MessageText);
        var (_, districts, _) = await ReadAllAsync();
        Assert.Empty(districts);
    }

    private const string Gazipur = "60233582-651c-55d7-a9cd-0df2a60de33e";
    private const string GazipurCity = "5080fc9f-1036-5599-927e-c08c97321e73";

    private async Task<List<CityCorporation>> CityCorporationsAsync(bool includeDeleted = false)
    {
        await using var db = Db.CreateRuntimeContext();
        var query = includeDeleted ? db.CityCorporations.IgnoreQueryFilters() : db.CityCorporations;
        return await query.AsNoTracking().ToListAsync();
    }

    [PostgresFact]
    public async Task The_city_corporation_script_loads_the_thirteen_under_their_districts_with_fixed_ids()
    {
        await ResetAsync();
        await RunAllAsync();

        await RunAsync("seed-city-corporations.sql");

        var cities = await CityCorporationsAsync();
        var (_, districts, _) = await ReadAsync();
        var districtOf = districts.ToDictionary(d => d.Id, d => d.Name);
        Assert.Equal(13, cities.Count);
        Assert.Equal(
            [
                "Barishal City Corporation|Barishal", "Bogura City Corporation|Bogura", "Chattogram City Corporation|Chattogram",
                "Cumilla City Corporation|Cumilla", "Dhaka North City Corporation|Dhaka", "Dhaka South City Corporation|Dhaka",
                "Gazipur City Corporation|Gazipur", "Khulna City Corporation|Khulna", "Mymensingh City Corporation|Mymensingh",
                "Narayanganj City Corporation|Narayanganj", "Rajshahi City Corporation|Rajshahi", "Rangpur City Corporation|Rangpur",
                "Sylhet City Corporation|Sylhet",
            ],
            cities.Select(c => $"{c.Name}|{districtOf[c.DistrictId]}").Order(StringComparer.Ordinal));
        var gazipur = Assert.Single(cities, c => c.Name == "Gazipur City Corporation");
        Assert.Equal(Guid.Parse(GazipurCity), gazipur.Id);
        Assert.Equal(Guid.Parse(Gazipur), gazipur.DistrictId);
        Assert.All(cities, c => Assert.True(c.IsActive));
        Assert.All(cities, c => Assert.Equal(NameNormalizer.Normalize(c.Name), c.NormalizedName));
        Assert.All(cities, c => Assert.Null(c.CreatedByUserId));
    }

    [PostgresFact]
    public async Task Running_the_city_corporation_script_again_keeps_admin_edits_and_deletions()
    {
        await ResetAsync();
        await RunAllAsync();
        await RunAsync("seed-city-corporations.sql");
        await ExecuteAsync(
            $"UPDATE \"CityCorporations\" SET \"Name\" = 'Gazipur City', \"NormalizedName\" = 'GAZIPUR CITY', \"IsActive\" = false WHERE \"Id\" = '{GazipurCity}';" +
            "UPDATE \"CityCorporations\" SET \"IsDeleted\" = true, \"DeletedAtUtc\" = now() WHERE \"Name\" = 'Sylhet City Corporation';");

        await RunAsync("seed-city-corporations.sql");

        var all = await CityCorporationsAsync(includeDeleted: true);
        Assert.Equal(13, all.Count);
        var gazipur = Assert.Single(all, c => c.Id == Guid.Parse(GazipurCity));
        Assert.Equal("Gazipur City", gazipur.Name);
        Assert.False(gazipur.IsActive);
        Assert.True(Assert.Single(all, c => c.Name == "Sylhet City Corporation").IsDeleted);
        Assert.Equal(12, (await CityCorporationsAsync()).Count);
    }

    [PostgresFact]
    public async Task City_corporations_without_their_districts_stop_with_an_error_and_add_nothing()
    {
        await ResetAsync();
        await RunAsync("seed-divisions.sql");

        var error = await Assert.ThrowsAsync<PostgresException>(() => RunAsync("seed-city-corporations.sql"));

        Assert.Contains("seed-districts.sql", error.MessageText);
        Assert.Empty(await CityCorporationsAsync(includeDeleted: true));
    }
}
