using System.Net;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>Division, District, and Upazila: parent integrity, name scopes, no codes, and reference-safe deletes.</summary>
public sealed class GeographyTests : ConfigurationTestBase
{
    private const string Divisions = "geography/divisions";
    private const string Districts = "geography/districts";
    private const string Upazilas = "geography/upazilas";

    [Fact]
    public async Task The_whole_hierarchy_can_be_created_read_updated_and_listed_by_parent()
    {
        using var admin = await AdminAsync();
        var dhaka = await CreateAsync(admin, Divisions, new { name = "Dhaka" });
        var district = await CreateAsync(admin, Districts, new { name = "Gazipur", divisionId = dhaka });
        var upazila = await CreateAsync(admin, Upazilas, new { name = "Kaliakair", districtId = district });

        var (_, found) = await Get(admin, $"{Upazilas}/{upazila}");
        Assert.Equal(district, found.GetProperty("districtId").GetGuid());
        Assert.Equal("Kaliakoir", (await Put(admin, Upazilas, upazila, new { name = "Kaliakoir", districtId = district })).Body.GetProperty("name").GetString());
        Assert.Equal(["Gazipur"], Names((await Get(admin, Districts, $"?divisionId={dhaka}")).Body));
        Assert.Equal(["Kaliakoir"], Names((await Get(admin, Upazilas, $"?districtId={district}")).Body));
    }

    [Fact]
    public async Task Geographic_records_have_no_code_fields()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "Dhaka" });

        var (status, problem) = await Post(admin, Divisions, new { name = "Khulna", code = "40" });
        var (districtStatus, districtProblem) = await Post(admin, Districts, new { name = "Gazipur", divisionId = division, code = "33" });
        var (_, item) = await Get(admin, $"{Divisions}/{division}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "code"));
        Assert.Equal(HttpStatusCode.BadRequest, districtStatus);
        Assert.True(HasError(districtProblem, "code"));
        Assert.False(item.TryGetProperty("code", out _));
    }

    [Fact]
    public async Task Division_names_are_unique_globally_after_normalizing()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, Divisions, new { name = "Dhaka" });

        var (status, problem) = await Post(admin, Divisions, new { name = "  dHAKA " });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "name"));
    }

    [Fact]
    public async Task District_names_are_unique_within_a_division_but_may_repeat_across_divisions()
    {
        using var admin = await AdminAsync();
        var a = await CreateAsync(admin, Divisions, new { name = "A" });
        var b = await CreateAsync(admin, Divisions, new { name = "B" });
        await CreateAsync(admin, Districts, new { name = "Central", divisionId = a });

        var (status, problem) = await Post(admin, Districts, new { name = " CENTRAL", divisionId = a });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("already exists", FirstError(problem, "name"));

        await CreateAsync(admin, Districts, new { name = "Central", divisionId = b });
    }

    [Fact]
    public async Task Upazila_names_are_unique_within_a_district_but_may_repeat_across_districts()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var one = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var two = await CreateAsync(admin, Districts, new { name = "Two", divisionId = division });
        await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = one });

        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, Upazilas, new { name = "sadar", districtId = one })).Status);
        await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = two });
    }

    [Fact]
    public async Task A_missing_nonexistent_or_wrong_kind_of_parent_is_rejected_and_nothing_is_stored()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var district = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });

        var missing = await Post(admin, Districts, new { name = "No parent" });
        var unknown = await Post(admin, Districts, new { name = "Ghost parent", divisionId = Guid.NewGuid() });
        // A District identifier is not a Division, and a Division identifier is not a District.
        var wrongKind = await Post(admin, Districts, new { name = "Wrong kind", divisionId = district });
        var upazilaWrongKind = await Post(admin, Upazilas, new { name = "Wrong kind", districtId = division });

        Assert.All(new[] { missing, unknown, wrongKind, upazilaWrongKind }, r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.True(HasError(missing.Body, "divisionId"));
        Assert.Contains("does not exist", FirstError(unknown.Body, "divisionId"));
        Assert.True(HasError(upazilaWrongKind.Body, "districtId"));
        Assert.Equal(["One"], Names((await Get(admin, Districts, "?status=all")).Body));
        Assert.Empty((await Get(admin, Upazilas, "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_cross_branch_filter_never_returns_rows_from_another_branch()
    {
        using var admin = await AdminAsync();
        var a = await CreateAsync(admin, Divisions, new { name = "A" });
        var b = await CreateAsync(admin, Divisions, new { name = "B" });
        await CreateAsync(admin, Districts, new { name = "In A", divisionId = a });
        await CreateAsync(admin, Districts, new { name = "In B", divisionId = b });

        Assert.Equal(["In A"], Names((await Get(admin, Districts, $"?divisionId={a}")).Body));
        Assert.Equal(["In B"], Names((await Get(admin, Districts, $"?divisionId={b}")).Body));
        Assert.Empty(Names((await Get(admin, Districts, $"?divisionId={Guid.NewGuid()}")).Body));
    }

    [Fact]
    public async Task Moving_a_district_to_another_division_checks_the_name_there()
    {
        using var admin = await AdminAsync();
        var a = await CreateAsync(admin, Divisions, new { name = "A" });
        var b = await CreateAsync(admin, Divisions, new { name = "B" });
        await CreateAsync(admin, Districts, new { name = "Same", divisionId = b });
        var mine = await CreateAsync(admin, Districts, new { name = "Same", divisionId = a });

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(admin, Districts, mine, new { name = "Same", divisionId = b })).Status);
    }

    [Fact]
    public async Task Deactivating_a_parent_keeps_its_children_visible_blocks_new_children_and_does_not_cascade()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var district = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var upazila = await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = district });

        await Put(admin, Divisions, division, new { name = "A", isActive = false });
        await Put(admin, Districts, district, new { name = "One", divisionId = division, isActive = false });

        // Children are unchanged and still visible; the inactive parents stay identifiable.
        Assert.True((await Get(admin, $"{Upazilas}/{upazila}")).Body.GetProperty("isActive").GetBoolean());
        Assert.False((await Get(admin, $"{Districts}/{district}")).Body.GetProperty("isActive").GetBoolean());
        Assert.Equal(["One"], Names((await Get(admin, Districts, "?status=all")).Body));
        // New children under an inactive parent are rejected.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, Districts, new { name = "Two", divisionId = division })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, Upazilas, new { name = "Other", districtId = district })).Status);
        // An existing child can still be renamed under its inactive parent.
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, Districts, district, new { name = "One renamed", divisionId = division, isActive = false })).Status);
    }

    [Fact]
    public async Task A_division_or_district_with_dependents_cannot_be_deleted_but_leaves_can()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var district = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var upazila = await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = district });

        var (divisionStatus, divisionProblem) = await Delete(admin, Divisions, division);
        var (districtStatus, _) = await Delete(admin, Districts, district);

        Assert.Equal(HttpStatusCode.Conflict, divisionStatus);
        Assert.Contains("Deactivate", divisionProblem.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Conflict, districtStatus);
        Assert.Equal(HttpStatusCode.OK, (await Get(admin, $"{Upazilas}/{upazila}")).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, Upazilas, upazila)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, Districts, district)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, Divisions, division)).Status);
    }

    [Fact]
    public async Task The_geography_routes_serve_only_geography()
    {
        using var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "geography/degrees")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "geography/batches")).Status);
    }
}
