using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>The seed import: validate-only never writes, an invalid document writes nothing, a valid one is all-or-nothing.</summary>
public sealed class GeographyImportTests : ConfigurationTestBase
{
    private const string Validate = $"{Root}/geography/import/validate";
    private const string Import = $"{Root}/geography/import";

    // A small synthetic document. It is test data only: the approved owner file is a separate release input.
    private static object Seed(object[]? divisions = null) => new
    {
        source = new { citation = "Synthetic test fixture", version = "test-1" },
        divisions = divisions ?? new object[]
        {
            new
            {
                name = "Alpha",
                districts = new object[]
                {
                    new { name = "Alpha One", upazilas = new object[] { new { name = "North" }, new { name = "South" } } },
                    new { name = "Alpha Two", upazilas = new object[] { new { name = "North" } } },
                },
            },
            new { name = "Beta", districts = new object[] { new { name = "Beta One", upazilas = Array.Empty<object>() } } },
        },
    };

    private async Task<(int Divisions, int Districts, int Upazilas)> CountsAsync()
    {
        (int, int, int) counts = default;
        await Factory.WithDbAsync(async db => counts = (await db.Divisions.CountAsync(), await db.Districts.CountAsync(), await db.Upazilas.CountAsync()));
        return counts;
    }

    [Fact]
    public async Task Validation_reports_the_source_and_counts_without_writing_anything()
    {
        using var admin = await AdminAsync();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Validate, Seed());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(report.GetProperty("isValid").GetBoolean());
        Assert.True(report.GetProperty("canImport").GetBoolean());
        Assert.Equal("Synthetic test fixture", report.GetProperty("sourceCitation").GetString());
        Assert.Equal("test-1", report.GetProperty("sourceVersion").GetString());
        Assert.Equal(2, report.GetProperty("divisions").GetInt32());
        Assert.Equal(3, report.GetProperty("districts").GetInt32());
        Assert.Equal(3, report.GetProperty("upazilas").GetInt32());
        Assert.Equal((0, 0, 0), await CountsAsync());
        Assert.Empty(await Factory.AuditEventsAsync("configuration.geography.imported"));
    }

    [Fact]
    public async Task A_valid_document_is_imported_with_every_parent_link_correct_and_one_audit_event()
    {
        using var admin = await AdminAsync();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, Seed());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, report.GetProperty("districts").GetInt32());
        Assert.Equal((2, 3, 3), await CountsAsync());

        var (_, alpha) = await Get(admin, "geography/divisions", "?search=alpha");
        var alphaId = alpha.GetProperty("items")[0].GetProperty("id").GetGuid();
        var districts = (await Get(admin, "geography/districts", $"?divisionId={alphaId}")).Body;
        Assert.Equal(["Alpha One", "Alpha Two"], Names(districts));
        var alphaOne = districts.GetProperty("items")[0].GetProperty("id").GetGuid();
        Assert.Equal(["North", "South"], Names((await Get(admin, "geography/upazilas", $"?districtId={alphaOne}")).Body));
        Assert.Equal(["Beta One"], Names((await Get(admin, "geography/districts", "?search=beta")).Body));

        var audit = Assert.Single(await Factory.AuditEventsAsync("configuration.geography.imported"));
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal("test-1", metadata.RootElement.GetProperty("sourceVersion").GetString());
        Assert.Equal(2, metadata.RootElement.GetProperty("divisions").GetInt32());
        Assert.Equal(3, metadata.RootElement.GetProperty("upazilas").GetInt32());
    }

    [Fact]
    public async Task An_invalid_document_is_rejected_with_a_report_and_writes_nothing()
    {
        using var admin = await AdminAsync();
        var bad = Seed(
        [
            new { name = "Alpha", districts = new object[] { new { name = "One", upazilas = new object[] { new { name = "X" } } } } },
            new { name = " ALPHA ", districts = Array.Empty<object>() },
        ]);

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, bad);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(report.GetProperty("isValid").GetBoolean());
        Assert.Contains(report.GetProperty("issues").EnumerateArray(), i => i.GetProperty("path").GetString() == "divisions[1].name");
        Assert.Equal((0, 0, 0), await CountsAsync());
        Assert.Empty(await Factory.AuditEventsAsync("configuration.geography.imported"));
    }

    [Fact]
    public async Task One_bad_row_deep_in_the_hierarchy_stops_the_whole_import()
    {
        using var admin = await AdminAsync();
        var bad = Seed(
        [
            new
            {
                name = "Alpha",
                districts = new object[]
                {
                    new { name = "One", upazilas = new object[] { new { name = "Fine" }, new { name = "fine" } } },
                },
            },
            new { name = "Beta", districts = Array.Empty<object>() },
        ]);

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, bad);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(report.GetProperty("issues").EnumerateArray(), i => i.GetProperty("path").GetString() == "divisions[0].districts[0].upazilas[1].name");
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Theory]
    [InlineData("""{ "divisions": [ { "name": "A", "districts": [] } ] }""", "source")]
    [InlineData("""{ "source": { "citation": "c" }, "divisions": [ { "name": "A", "districts": [] } ] }""", "source.version")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [] }""", "divisions")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "", "districts": [] } ] }""", "divisions[0].name")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A" } ] }""", "divisions[0].districts")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "code": "10", "districts": [] } ] }""", "divisions[0].code")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "districts": [ { "name": "D", "upazilas": [ { "name": "U", "code": "1" } ] } ] } ] }""", "divisions[0].districts[0].upazilas[0].code")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [], "extra": 1 }""", "$.extra")]
    [InlineData("""[ 1, 2 ]""", "$")]
    public async Task Documents_that_break_the_schema_are_reported_at_the_offending_path(string document, string path)
    {
        using var admin = await AdminAsync();
        var body = JsonDocument.Parse(document).RootElement.Clone();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Validate, body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(report.GetProperty("issues").EnumerateArray(), i => i.GetProperty("path").GetString() == path);
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task An_import_into_tables_that_already_hold_rows_is_refused_and_changes_nothing()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "geography/divisions", new { name = "Admin managed" });

        var (validate, validation) = await SendAsync(admin, HttpMethod.Post, Validate, Seed());
        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, Seed());

        Assert.Equal(HttpStatusCode.OK, validate);
        Assert.False(validation.GetProperty("canImport").GetBoolean());
        Assert.False(validation.GetProperty("targetEmpty").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(report.GetProperty("targetEmpty").GetBoolean());
        Assert.Equal((1, 0, 0), await CountsAsync());
        Assert.Equal(["Admin managed"], Names((await Get(admin, "geography/divisions", "?status=all")).Body));
    }

    [Fact]
    public async Task Importing_the_same_document_twice_reports_a_conflict_instead_of_overwriting()
    {
        using var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Post, Import, Seed())).Status);

        var (status, _) = await SendAsync(admin, HttpMethod.Post, Import, Seed());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal((2, 3, 3), await CountsAsync());
        Assert.Single(await Factory.AuditEventsAsync("configuration.geography.imported"));
    }

    [Fact]
    public async Task An_audit_failure_rolls_the_whole_import_back()
    {
        using var admin = await AdminAsync();
        // A bulk event whose metadata is over the size limit cannot be staged, so no rows may be written.
        var longCitation = new string('x', 5000) + " " + new string('y', 5000);
        var seed = new
        {
            source = new { citation = "ok", version = longCitation },
            divisions = new object[] { new { name = "Alpha", districts = Array.Empty<object>() } },
        };

        var (status, _) = await SendAsync(admin, HttpMethod.Post, Import, seed);

        Assert.True(status is HttpStatusCode.InternalServerError or HttpStatusCode.BadRequest, status.ToString());
        Assert.Equal((0, 0, 0), await CountsAsync());
    }

    [Fact]
    public async Task Tables_that_hold_only_deleted_rows_do_not_block_an_import()
    {
        using var admin = await AdminAsync();
        var leftover = await CreateAsync(admin, "geography/divisions", new { name = "Alpha" });
        await Delete(admin, "geography/divisions", leftover);

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, Seed());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(report.GetProperty("targetEmpty").GetBoolean());
        Assert.Equal(2, (await Get(admin, "geography/divisions", "?page=1")).Body.GetProperty("totalCount").GetInt64());
    }

    private async Task<int> CityCorporationCountAsync()
    {
        var count = 0;
        await Factory.WithDbAsync(async db => count = await db.CityCorporations.CountAsync());
        return count;
    }

    // Synthetic test data only. Alpha One has two City Corporations, Alpha Two one, and Beta One omits the list.
    private static object SeedWithCityCorporations(object[]? alphaOneCities = null) => Seed(
    [
        new
        {
            name = "Alpha",
            districts = new object[]
            {
                new
                {
                    name = "Alpha One",
                    upazilas = new object[] { new { name = "North" } },
                    cityCorporations = alphaOneCities ?? new object[] { new { name = "Alpha Metro" }, new { name = "North" } },
                },
                new { name = "Alpha Two", upazilas = Array.Empty<object>(), cityCorporations = new object[] { new { name = "Alpha Metro" } } },
            },
        },
        new { name = "Beta", districts = new object[] { new { name = "Beta One", upazilas = Array.Empty<object>() } } },
    ]);

    [Fact]
    public async Task Validation_counts_city_corporations_and_a_document_without_them_reports_zero()
    {
        using var admin = await AdminAsync();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Validate, SeedWithCityCorporations());
        var (_, without) = await SendAsync(admin, HttpMethod.Post, Validate, Seed());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, report.GetProperty("cityCorporations").GetInt32());
        Assert.Equal(0, without.GetProperty("cityCorporations").GetInt32());
        Assert.Equal(0, await CityCorporationCountAsync());
    }

    [Fact]
    public async Task City_corporations_are_imported_under_their_districts_and_counted_in_the_audit_event()
    {
        using var admin = await AdminAsync();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, SeedWithCityCorporations());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, report.GetProperty("cityCorporations").GetInt32());
        Assert.Equal(3, await CityCorporationCountAsync());
        var districts = (await Get(admin, "geography/districts", "?status=all")).Body.GetProperty("items").EnumerateArray()
            .ToDictionary(d => d.GetProperty("name").GetString()!, d => d.GetProperty("id").GetGuid());
        Assert.Equal(["Alpha Metro", "North"], Names((await Get(admin, "geography/city-corporations", $"?districtId={districts["Alpha One"]}")).Body));
        Assert.Equal(["Alpha Metro"], Names((await Get(admin, "geography/city-corporations", $"?districtId={districts["Alpha Two"]}")).Body));
        Assert.Empty(Names((await Get(admin, "geography/city-corporations", $"?districtId={districts["Beta One"]}")).Body));

        var audit = Assert.Single(await Factory.AuditEventsAsync("configuration.geography.imported"));
        using var metadata = JsonDocument.Parse(audit.MetadataJson!);
        Assert.Equal(3, metadata.RootElement.GetProperty("cityCorporations").GetInt32());
    }

    [Fact]
    public async Task A_duplicate_city_corporation_name_within_a_district_stops_the_whole_import()
    {
        using var admin = await AdminAsync();
        var bad = SeedWithCityCorporations([new { name = "Alpha Metro" }, new { name = " alpha  METRO " }]);

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, bad);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(report.GetProperty("issues").EnumerateArray(),
            i => i.GetProperty("path").GetString() == "divisions[0].districts[0].cityCorporations[1].name");
        Assert.Equal((0, 0, 0), await CountsAsync());
        Assert.Equal(0, await CityCorporationCountAsync());
        Assert.Empty(await Factory.AuditEventsAsync("configuration.geography.imported"));
    }

    [Theory]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "districts": [ { "name": "D", "upazilas": [], "cityCorporations": [ { "name": "C", "code": "1" } ] } ] } ] }""", "divisions[0].districts[0].cityCorporations[0].code")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "districts": [ { "name": "D", "upazilas": [], "cityCorporations": [ { "name": " " } ] } ] } ] }""", "divisions[0].districts[0].cityCorporations[0].name")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "districts": [ { "name": "D", "upazilas": [], "cityCorporations": [ "C" ] } ] } ] }""", "divisions[0].districts[0].cityCorporations[0]")]
    [InlineData("""{ "source": { "citation": "c", "version": "v" }, "divisions": [ { "name": "A", "districts": [ { "name": "D", "upazilas": [], "cityCorporations": { "name": "C" } } ] } ] }""", "divisions[0].districts[0].cityCorporations")]
    public async Task Bad_city_corporation_entries_are_reported_at_their_path(string document, string path)
    {
        using var admin = await AdminAsync();

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Validate, JsonDocument.Parse(document).RootElement.Clone());

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(report.GetProperty("issues").EnumerateArray(), i => i.GetProperty("path").GetString() == path);
    }

    [Fact]
    public async Task Importing_city_corporations_twice_is_refused_and_keeps_the_first_import()
    {
        using var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(admin, HttpMethod.Post, Import, SeedWithCityCorporations())).Status);

        var (status, report) = await SendAsync(admin, HttpMethod.Post, Import, SeedWithCityCorporations());

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.False(report.GetProperty("targetEmpty").GetBoolean());
        Assert.Equal(3, await CityCorporationCountAsync());
    }
}
