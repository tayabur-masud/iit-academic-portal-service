using System.Net;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>Division, District, Upazila, and City Corporation: parent integrity, name scopes, no codes, and reference-safe deletes.</summary>
public sealed class GeographyTests : ConfigurationTestBase
{
    private const string Divisions = "geography/divisions";
    private const string Districts = "geography/districts";
    private const string Upazilas = "geography/upazilas";
    private const string CityCorporations = "geography/city-corporations";

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

    [Fact]
    public async Task A_deleted_district_or_upazila_name_is_free_again_and_a_deleted_division_cannot_be_a_parent()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var district = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var upazila = await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = district });

        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, Divisions, division)).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, Upazilas, upazila)).Status);
        await CreateAsync(admin, Upazilas, new { name = "Sadar", districtId = district });
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, Districts, district)).Status);

        var other = await CreateAsync(admin, Divisions, new { name = "B" });
        await Delete(admin, Divisions, other);
        var (status, problem) = await Post(admin, Districts, new { name = "Orphan", divisionId = other });
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("does not exist", FirstError(problem, "divisionId"));
    }

    [Fact]
    public async Task A_city_corporation_is_created_read_updated_and_listed_by_district()
    {
        using var admin = await AdminAsync();
        var dhaka = await CreateAsync(admin, Divisions, new { name = "Dhaka" });
        var gazipur = await CreateAsync(admin, Districts, new { name = "Gazipur", divisionId = dhaka });
        var narayanganj = await CreateAsync(admin, Districts, new { name = "Narayanganj", divisionId = dhaka });
        var city = await CreateAsync(admin, CityCorporations, new { name = "Gazipur City Corporation", districtId = gazipur });
        await CreateAsync(admin, CityCorporations, new { name = "Narayanganj City Corporation", districtId = narayanganj });

        var (_, found) = await Get(admin, $"{CityCorporations}/{city}");
        var (renamed, updated) = await Put(admin, CityCorporations, city, new { name = "Gazipur City", districtId = gazipur });

        Assert.Equal(gazipur, found.GetProperty("districtId").GetGuid());
        Assert.True(found.GetProperty("isActive").GetBoolean());
        Assert.False(found.TryGetProperty("code", out _));
        Assert.Equal(HttpStatusCode.OK, renamed);
        Assert.Equal("Gazipur City", updated.GetProperty("name").GetString());
        Assert.Equal(["Gazipur City"], Names((await Get(admin, CityCorporations, $"?districtId={gazipur}")).Body));
        Assert.Equal(["Narayanganj City Corporation"], Names((await Get(admin, CityCorporations, "?search=narayan")).Body));
        Assert.Empty(Names((await Get(admin, CityCorporations, $"?districtId={Guid.NewGuid()}")).Body));
        // City Corporations are a separate District child: they never appear among the Upazilas.
        Assert.Empty(Names((await Get(admin, Upazilas, $"?districtId={gazipur}")).Body));
    }

    [Fact]
    public async Task A_city_corporation_needs_an_existing_active_district_and_has_no_code()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var active = await CreateAsync(admin, Districts, new { name = "Open", divisionId = division });
        var inactive = await CreateAsync(admin, Districts, new { name = "Closed", divisionId = division, isActive = false });

        var missing = await Post(admin, CityCorporations, new { name = "No district" });
        var unknown = await Post(admin, CityCorporations, new { name = "Ghost district", districtId = Guid.NewGuid() });
        var wrongKind = await Post(admin, CityCorporations, new { name = "Wrong kind", districtId = division });
        var closed = await Post(admin, CityCorporations, new { name = "Closed district", districtId = inactive });
        var coded = await Post(admin, CityCorporations, new { name = "Coded", districtId = active, code = "CC-1" });
        var divisionField = await Post(admin, CityCorporations, new { name = "Division field", districtId = active, divisionId = division });

        Assert.All(new[] { missing, unknown, wrongKind, closed, coded, divisionField }, r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.True(HasError(missing.Body, "districtId"));
        Assert.Contains("does not exist", FirstError(unknown.Body, "districtId"));
        Assert.True(HasError(wrongKind.Body, "districtId"));
        Assert.True(HasError(closed.Body, "districtId"));
        Assert.True(HasError(coded.Body, "code"));
        Assert.True(HasError(divisionField.Body, "divisionId"));
        Assert.Empty((await Get(admin, CityCorporations, "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task City_corporation_names_are_unique_within_a_district_but_may_repeat_across_districts_and_upazilas()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var one = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var two = await CreateAsync(admin, Districts, new { name = "Two", divisionId = division });
        await CreateAsync(admin, CityCorporations, new { name = "Metro City", districtId = one });
        // An Upazila of the same name in the same District does not conflict: uniqueness is per entity type.
        await CreateAsync(admin, Upazilas, new { name = "Metro City", districtId = one });

        var (status, problem) = await Post(admin, CityCorporations, new { name = "  metro   CITY ", districtId = one });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("A city corporation with this name already exists in this district.", FirstError(problem, "name"));
        await CreateAsync(admin, CityCorporations, new { name = "Metro City", districtId = two });
    }

    [Fact]
    public async Task Moving_a_city_corporation_needs_an_active_district_but_an_unchanged_inactive_district_may_stay()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var home = await CreateAsync(admin, Districts, new { name = "Home", divisionId = division });
        var closed = await CreateAsync(admin, Districts, new { name = "Closed", divisionId = division });
        var other = await CreateAsync(admin, Districts, new { name = "Other", divisionId = division });
        await CreateAsync(admin, CityCorporations, new { name = "Taken", districtId = other });
        var city = await CreateAsync(admin, CityCorporations, new { name = "Taken", districtId = home });
        await Put(admin, Districts, closed, new { name = "Closed", divisionId = division, isActive = false });

        var (toClosed, closedProblem) = await Put(admin, CityCorporations, city, new { name = "Taken", districtId = closed });
        var (toTaken, takenProblem) = await Put(admin, CityCorporations, city, new { name = "Taken", districtId = other });

        Assert.Equal(HttpStatusCode.BadRequest, toClosed);
        Assert.True(HasError(closedProblem, "districtId"));
        Assert.Equal(HttpStatusCode.BadRequest, toTaken);
        Assert.True(HasError(takenProblem, "name"));
        Assert.Equal(home, (await Get(admin, $"{CityCorporations}/{city}")).Body.GetProperty("districtId").GetGuid());

        // Its own District going inactive does not stop a rename that keeps that District.
        await Put(admin, Districts, home, new { name = "Home", divisionId = division, isActive = false });
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, CityCorporations, city, new { name = "Renamed", districtId = home })).Status);
    }

    [Fact]
    public async Task A_city_corporation_can_be_deactivated_and_deleted_and_blocks_deleting_its_district_while_it_lives()
    {
        using var admin = await AdminAsync();
        var division = await CreateAsync(admin, Divisions, new { name = "A" });
        var district = await CreateAsync(admin, Districts, new { name = "One", divisionId = division });
        var city = await CreateAsync(admin, CityCorporations, new { name = "City", districtId = district });

        var (deactivated, item) = await Put(admin, CityCorporations, city, new { name = "City", districtId = district, isActive = false });
        Assert.Equal(HttpStatusCode.OK, deactivated);
        Assert.False(item.GetProperty("isActive").GetBoolean());
        Assert.Empty(Names((await Get(admin, CityCorporations)).Body));
        Assert.Equal(["City"], Names((await Get(admin, CityCorporations, "?status=inactive")).Body));

        // An inactive City Corporation still blocks deleting its District; a deleted one does not.
        var (blocked, problem) = await Delete(admin, Districts, district);
        Assert.Equal(HttpStatusCode.Conflict, blocked);
        Assert.Contains("Deactivate", problem.GetProperty("detail").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, CityCorporations, city)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"{CityCorporations}/{city}")).Status);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, Districts, district)).Status);
    }
}
