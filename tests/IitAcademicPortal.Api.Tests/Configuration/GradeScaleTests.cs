using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// The global Grade scale: list and detail reads, and the full-scale replacement that is its only write. Every accepted scale has
/// exactly one active band for each percentage from 0 through 100; a rejected replacement changes no band and records no audit event.
/// The SQLite test host has no migration seed, so each test loads the ten initial bands through the replacement itself.
/// </summary>
public sealed class GradeScaleTests : ConfigurationTestBase
{
    private const string Grades = "grades";
    private const string Scale = $"{Root}/grades/scale";

    /// <summary>The ten initial bands from the specification, highest first.</summary>
    public static readonly (string Letter, decimal Lower, decimal Upper, bool Inclusive, decimal Numeric, string Comment)[] Initial =
    [
        ("A+", 80, 100, true, 4.00m, "Excellent"),
        ("A", 75, 80, false, 3.75m, "Better"),
        ("A-", 70, 75, false, 3.50m, "Good"),
        ("B+", 65, 70, false, 3.25m, "Above Average"),
        ("B", 60, 65, false, 3.00m, "Average"),
        ("B-", 55, 60, false, 2.75m, "Below Average"),
        ("C+", 50, 55, false, 2.50m, "Satisfactory"),
        ("C", 45, 50, false, 2.25m, "Not satisfactory"),
        ("D", 40, 45, false, 2.00m, "Pass"),
        ("F", 0, 40, false, 0.00m, "Fail"),
    ];

    private static JsonObject Band((string Letter, decimal Lower, decimal Upper, bool Inclusive, decimal Numeric, string Comment) b, Guid? id = null, bool active = true)
    {
        var band = new JsonObject
        {
            ["letterGrade"] = b.Letter,
            ["marksLowerBound"] = b.Lower,
            ["marksUpperBound"] = b.Upper,
            ["upperBoundInclusive"] = b.Inclusive,
            ["numericGrade"] = b.Numeric,
            ["comment"] = b.Comment,
            ["isActive"] = active,
        };
        if (id is { } value)
        {
            band["id"] = value;
        }

        return band;
    }

    private static object Request(IEnumerable<JsonObject> bands) => new JsonObject { ["bands"] = new JsonArray([.. bands]) };

    private static Task<(HttpStatusCode Status, JsonElement Body)> Replace(PortalClient client, IEnumerable<JsonObject> bands) =>
        SendAsync(client, HttpMethod.Put, Scale, Request(bands));

    /// <summary>Loads the ten initial bands and returns the stored bands as editable request items, with their ids, in list order.</summary>
    private async Task<List<JsonObject>> SeedAsync(PortalClient admin)
    {
        var (status, body) = await Replace(admin, Initial.Select(b => Band(b)));
        Assert.True(status == HttpStatusCode.OK, $"Seeding the scale returned {status}: {body}");
        return await CurrentAsync(admin);
    }

    private static async Task<List<JsonObject>> CurrentAsync(PortalClient admin, string query = "?status=all")
    {
        var (_, page) = await Get(admin, Grades, query);
        return page.GetProperty("items").EnumerateArray().Select(item => new JsonObject
        {
            ["id"] = item.GetProperty("id").GetGuid(),
            ["letterGrade"] = item.GetProperty("letterGrade").GetString(),
            ["marksLowerBound"] = item.GetProperty("marksLowerBound").GetDecimal(),
            ["marksUpperBound"] = item.GetProperty("marksUpperBound").GetDecimal(),
            ["upperBoundInclusive"] = item.GetProperty("upperBoundInclusive").GetBoolean(),
            ["numericGrade"] = item.GetProperty("numericGrade").GetDecimal(),
            ["comment"] = item.GetProperty("comment").GetString(),
            ["isActive"] = item.GetProperty("isActive").GetBoolean(),
        }).ToList();
    }

    private static List<string> Letters(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("letterGrade").GetString()!).ToList();

    private async Task<(int Live, int Total)> RowCountsAsync()
    {
        (int, int) counts = default;
        await Factory.WithDbAsync(async db => counts = (await db.Grades.CountAsync(), await db.Grades.IgnoreQueryFilters().CountAsync()));
        return counts;
    }

    private async Task<List<Domain.Auditing.AuditEvent>> GradeEventsAsync() =>
        (await Factory.AuditEventsAsync()).Where(e => e.EventType.StartsWith("configuration.grade.", StringComparison.Ordinal)).ToList();

    /// <summary>The single active band a mark falls in, using inclusive lower and exclusive upper bounds except an inclusive top.</summary>
    private static string? BandFor(IEnumerable<JsonObject> bands, decimal mark) =>
        bands.Where(b => (bool)b["isActive"]!
                && mark >= (decimal)b["marksLowerBound"]!
                && (mark < (decimal)b["marksUpperBound"]! || ((bool)b["upperBoundInclusive"]! && mark == (decimal)b["marksUpperBound"]!)))
            .Select(b => (string)b["letterGrade"]!)
            .SingleOrDefault();

    [Fact]
    public async Task The_ten_initial_bands_are_stored_and_listed_highest_first_with_their_values()
    {
        using var admin = await AdminAsync();
        var (status, replaced) = await Replace(admin, Initial.Select(b => Band(b)));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Initial.Select(b => b.Letter).ToList(), Letters(replaced));
        var (_, page) = await Get(admin, Grades);
        Assert.Equal(Initial.Select(b => b.Letter).ToList(), Letters(page));
        Assert.True(page.GetProperty("nextCursor").ValueKind == JsonValueKind.Null);

        var items = page.GetProperty("items").EnumerateArray().ToList();
        for (var i = 0; i < Initial.Length; i++)
        {
            Assert.Equal(Initial[i].Lower, items[i].GetProperty("marksLowerBound").GetDecimal());
            Assert.Equal(Initial[i].Upper, items[i].GetProperty("marksUpperBound").GetDecimal());
            Assert.Equal(Initial[i].Inclusive, items[i].GetProperty("upperBoundInclusive").GetBoolean());
            Assert.Equal(Initial[i].Numeric, items[i].GetProperty("numericGrade").GetDecimal());
            Assert.Equal(Initial[i].Comment, items[i].GetProperty("comment").GetString());
            Assert.True(items[i].GetProperty("isActive").GetBoolean());
            Assert.False(items[i].TryGetProperty("degreeId", out _));
            Assert.False(items[i].TryGetProperty("name", out _));
        }

        var id = items[0].GetProperty("id").GetGuid();
        var (read, detail) = await Get(admin, $"{Grades}/{id}");
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal("A+", detail.GetProperty("letterGrade").GetString());
        Assert.Equal(AdminEmail, detail.GetProperty("createdBy").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"{Grades}/{Guid.NewGuid()}")).Status);
        Assert.Equal(10, (await GradeEventsAsync()).Count(e => e.EventType == "configuration.grade.created"));
    }

    [Theory]
    [InlineData("0", "F")]
    [InlineData("39.99", "F")]
    [InlineData("40", "D")]
    [InlineData("44.99", "D")]
    [InlineData("45", "C")]
    [InlineData("50", "C+")]
    [InlineData("74.999", "A-")]
    [InlineData("75", "A")]
    [InlineData("79.99", "A")]
    [InlineData("80", "A+")]
    [InlineData("100", "A+")]
    public async Task Every_percentage_falls_in_exactly_one_band_with_inclusive_lower_bounds(string mark, string letter)
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);

        Assert.Equal(letter, BandFor(bands, decimal.Parse(mark, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Lists_search_letter_grades_and_filter_by_status()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        // Split F into an inactive old F and two active bands that still cover 0-40.
        var f = bands.Single(b => (string)b["letterGrade"]! == "F");
        f["isActive"] = false;
        bands.Add(Band(("E", 20, 40, false, 1.00m, "Weak")));
        bands.Add(Band(("F2", 0, 20, false, 0.00m, "Fail")));

        Assert.Equal(HttpStatusCode.OK, (await Replace(admin, bands)).Status);

        Assert.Equal(["B+", "B", "B-"], Letters((await Get(admin, Grades, "?search=b")).Body));
        Assert.Equal(["F"], Letters((await Get(admin, Grades, "?status=inactive")).Body));
        Assert.Equal(11, Letters((await Get(admin, Grades)).Body).Count);
        Assert.Equal(12, Letters((await Get(admin, Grades, "?status=all")).Body).Count);
        Assert.Equal(HttpStatusCode.BadRequest, (await Get(admin, Grades, "?status=sometimes")).Status);
    }

    [Fact]
    public async Task A_valid_replacement_updates_creates_deactivates_and_soft_deletes_with_one_event_each()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        var before = (await GradeEventsAsync()).Count;
        var byLetter = bands.ToDictionary(b => (string)b["letterGrade"]!);

        // Update: A+ comment and numeric grade. Deactivate C. Omit D (soft delete). Cover 40-50 with a new "C/D" band.
        byLetter["A+"]["comment"] = "Outstanding";
        byLetter["C"]["isActive"] = false;
        var replacement = bands.Where(b => (string)b["letterGrade"]! != "D").ToList();
        replacement.Add(Band(("CD", 40, 50, false, 2.10m, "Pass")));

        var (status, body) = await Replace(admin, replacement);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("CD", Letters(body));
        Assert.DoesNotContain("D", Letters((await Get(admin, Grades, "?status=all")).Body));
        Assert.Equal("Outstanding", (await Get(admin, $"{Grades}/{byLetter["A+"]["id"]!.GetValue<Guid>()}")).Body.GetProperty("comment").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"{Grades}/{byLetter["D"]["id"]!.GetValue<Guid>()}")).Status);
        Assert.False((await Get(admin, $"{Grades}/{byLetter["C"]["id"]!.GetValue<Guid>()}")).Body.GetProperty("isActive").GetBoolean());
        Assert.Equal((10, 11), await RowCountsAsync());

        var events = (await GradeEventsAsync()).Skip(before).Select(e => e.EventType).OrderBy(e => e).ToList();
        Assert.Equal(
            ["configuration.grade.created", "configuration.grade.deactivated", "configuration.grade.deleted", "configuration.grade.updated"],
            events);
        var updated = (await GradeEventsAsync()).Single(e => e.EventType == "configuration.grade.updated");
        Assert.Equal(byLetter["A+"]["id"]!.GetValue<Guid>().ToString(), updated.EntityId);
        Assert.Equal("Grade", updated.EntityType);
        Assert.Contains("Outstanding", updated.ChangesJson);
    }

    [Fact]
    public async Task Resubmitting_the_same_scale_changes_nothing_and_records_nothing()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        var before = (await GradeEventsAsync()).Count;

        var (status, _) = await Replace(admin, bands);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(before, (await GradeEventsAsync()).Count);
    }

    public static TheoryData<string, string> InvalidScales => new()
    {
        { "gap", "bands" },
        { "overlap", "bands" },
        { "no-zero", "bands" },
        { "top-not-inclusive", "bands[0].upperBoundInclusive" },
        { "inclusive-below-100", "bands[1].upperBoundInclusive" },
        { "below-0", "bands[9].marksLowerBound" },
        { "above-100", "bands[0].marksUpperBound" },
        { "lower-not-below-upper", "bands[1].marksUpperBound" },
        { "numeric-above-4", "bands[0].numericGrade" },
        { "numeric-negative", "bands[9].numericGrade" },
        { "numeric-three-decimals", "bands[1].numericGrade" },
        { "duplicate-letter", "bands[1].letterGrade" },
        { "blank-letter", "bands[1].letterGrade" },
        { "blank-comment", "bands[1].comment" },
        { "missing-numeric", "bands[1].numericGrade" },
        { "unknown-id", "bands[1].id" },
        { "repeated-id", "bands[1].id" },
        { "no-active-band", "bands" },
        { "empty", "bands" },
    };

    [Theory]
    [MemberData(nameof(InvalidScales))]
    public async Task An_invalid_replacement_is_refused_whole_and_changes_no_band_or_audit_history(string problem, string field)
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        var before = (await GradeEventsAsync()).Count;
        var snapshot = (await Get(admin, Grades, "?status=all")).Body.GetRawText();

        // Also change a valid band, to prove nothing of a refused replacement is kept.
        bands[2]["comment"] = "Changed in a refused replacement";
        switch (problem)
        {
            case "gap": bands[1]["marksLowerBound"] = 76m; break;
            case "overlap": bands[1]["marksLowerBound"] = 74m; break;
            case "no-zero": bands[9]["marksLowerBound"] = 1m; break;
            case "top-not-inclusive": bands[0]["upperBoundInclusive"] = false; break;
            case "inclusive-below-100": bands[1]["upperBoundInclusive"] = true; break;
            case "below-0": bands[9]["marksLowerBound"] = -1m; break;
            case "above-100": bands[0]["marksUpperBound"] = 100.5m; break;
            case "lower-not-below-upper": bands[1]["marksUpperBound"] = 75m; break;
            case "numeric-above-4": bands[0]["numericGrade"] = 4.01m; break;
            case "numeric-negative": bands[9]["numericGrade"] = -0.5m; break;
            case "numeric-three-decimals": bands[1]["numericGrade"] = 3.755m; break;
            case "duplicate-letter": bands[1]["letterGrade"] = " a+ "; break;
            case "blank-letter": bands[1]["letterGrade"] = " "; break;
            case "blank-comment": bands[1]["comment"] = ""; break;
            case "missing-numeric": bands[1].Remove("numericGrade"); break;
            case "unknown-id": bands[1]["id"] = Guid.NewGuid(); break;
            case "repeated-id": bands[1]["id"] = bands[0]["id"]!.DeepClone(); break;
            case "no-active-band": bands.ForEach(b => b["isActive"] = false); break;
            case "empty": bands.Clear(); break;
        }

        var (status, body) = await Replace(admin, bands);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(body, field), $"Expected an error on {field}: {body}");
        Assert.Equal(snapshot, (await Get(admin, Grades, "?status=all")).Body.GetRawText());
        Assert.Equal(before, (await GradeEventsAsync()).Count);
    }

    [Fact]
    public async Task A_gap_is_explained_with_the_percentages_it_leaves_uncovered()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        bands[1]["marksLowerBound"] = 76m;

        var (_, body) = await Replace(admin, bands);

        Assert.Contains("75", FirstError(body, "bands"));
        Assert.Contains("76", FirstError(body, "bands"));
        Assert.True(HasError(body, "bands[1].marksLowerBound"));
    }

    [Fact]
    public async Task An_inactive_band_may_overlap_the_active_scale_and_deleted_letters_are_free_again()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        bands.Add(Band(("Z", 10, 90, false, 1.00m, "Old experiment"), active: false));
        Assert.Equal(HttpStatusCode.OK, (await Replace(admin, bands)).Status);

        // Drop D (soft delete), then reuse its letter for a new band.
        var current = await CurrentAsync(admin);
        var withoutD = current.Where(b => (string)b["letterGrade"]! != "D").ToList();
        withoutD.Single(b => (string)b["letterGrade"]! == "C")["marksLowerBound"] = 40m;
        Assert.Equal(HttpStatusCode.OK, (await Replace(admin, withoutD)).Status);
        var again = await CurrentAsync(admin);
        again.Single(b => (string)b["letterGrade"]! == "C")["marksLowerBound"] = 42.5m;
        again.Add(Band(("D", 40, 42.5m, false, 2.00m, "Pass")));

        var (status, body) = await Replace(admin, again);

        Assert.True(status == HttpStatusCode.OK, body.ToString());
        Assert.Equal("D", BandFor(await CurrentAsync(admin), 42.4m));
        Assert.Equal("C", BandFor(await CurrentAsync(admin), 42.5m));
    }

    [Fact]
    public async Task The_scale_has_no_degree_and_refuses_unknown_fields()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        bands[0]["degreeId"] = Guid.NewGuid();

        var (status, _) = await Replace(admin, bands);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Excellent", (await CurrentAsync(admin))[0]["comment"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("POST", "")]
    [InlineData("PUT", "/00000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/00000000-0000-0000-0000-000000000001")]
    public async Task Single_band_writes_are_not_allowed_and_point_to_the_scale_route(string method, string suffix)
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, new HttpMethod(method), $"{Root}/grades{suffix}", Band(Initial[0]));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, status);
        Assert.Contains("grades/scale", body.GetProperty("detail").GetString());
        Assert.Equal((0, 0), await RowCountsAsync());
    }

    [Fact]
    public async Task Only_an_active_admin_session_with_the_anti_forgery_token_can_read_or_replace_the_scale()
    {
        using var teacher = await SignedInAsync(PortalRoles.Teacher);
        using var anonymous = Factory.CreatePortalClient();
        using var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await Get(teacher, Grades)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await Replace(teacher, Initial.Select(b => Band(b)))).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Get(anonymous, Grades)).Status);
        using var noToken = await admin.SendAsync(HttpMethod.Put, Scale, Request(Initial.Select(b => Band(b))), withAntiforgery: false);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        Assert.Equal((0, 0), await RowCountsAsync());
        Assert.Empty(await GradeEventsAsync());
    }

    [Fact]
    public async Task An_audit_failure_rolls_the_whole_replacement_back()
    {
        using var admin = await AdminAsync();
        var bands = await SeedAsync(admin);
        var snapshot = (await Get(admin, Grades, "?status=all")).Body.GetRawText();
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON \"AuditEvents\" WHEN NEW.\"EventType\" LIKE 'configuration.grade.%' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;"));
        bands[0]["comment"] = "Must not persist";
        bands.Add(Band(("Z", 10, 90, false, 1.00m, "Must not persist"), active: false));

        var (status, _) = await Replace(admin, bands);

        Assert.True(status is HttpStatusCode.InternalServerError or HttpStatusCode.BadRequest, status.ToString());
        Assert.Equal(snapshot, (await Get(admin, Grades, "?status=all")).Body.GetRawText());
        Assert.Equal((10, 10), await RowCountsAsync());
    }
}
