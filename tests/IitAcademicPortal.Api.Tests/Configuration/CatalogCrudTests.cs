using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>The eight independent catalogs: lifecycle, validation, normalized uniqueness, search, and paging.</summary>
public sealed class CatalogCrudTests : ConfigurationTestBase
{
    public static TheoryData<string> All => new()
    {
        "institutions", "education-boards", "education-groups", "examination-types",
        "degrees", "semesters", "academic-sessions", "designations", "halls",
    };

    public static TheoryData<string> Coded => new() { "institutions", "education-boards", "degrees", "designations", "halls" };

    public static TheoryData<string> NameOnly => new() { "education-groups", "examination-types", "semesters", "academic-sessions" };

    private static object Body(string route, string name, string? code = null, bool? active = null)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        if (code is not null)
        {
            body["code"] = code;
        }

        if (active is not null)
        {
            body["isActive"] = active;
        }

        if (route == "institutions")
        {
            body["type"] = "College";
        }
        else if (route == "degrees")
        {
            body["creditsToBeCompleted"] = 120m;
            body["minimumCgpaForCompletion"] = 2.50m;
        }

        return body;
    }

    [Theory]
    [MemberData(nameof(All))]
    public async Task A_catalog_can_be_created_read_listed_updated_deactivated_and_deleted(string route)
    {
        using var admin = await AdminAsync();

        var (created, json) = await Post(admin, route, Body(route, "  Alpha  "));
        Assert.Equal(HttpStatusCode.Created, created);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal("Alpha", json.GetProperty("name").GetString());
        Assert.True(json.GetProperty("isActive").GetBoolean());
        Assert.False(json.TryGetProperty("normalizedName", out _));

        var (read, item) = await Get(admin, $"{route}/{id}");
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal("Alpha", item.GetProperty("name").GetString());

        Assert.Contains("Alpha", Names((await Get(admin, route)).Body));

        var (updated, renamed) = await Put(admin, route, id, Body(route, "Beta"));
        Assert.Equal(HttpStatusCode.OK, updated);
        Assert.Equal("Beta", renamed.GetProperty("name").GetString());

        var (deactivated, off) = await Put(admin, route, id, Body(route, "Beta", active: false));
        Assert.Equal(HttpStatusCode.OK, deactivated);
        Assert.False(off.GetProperty("isActive").GetBoolean());
        Assert.DoesNotContain("Beta", Names((await Get(admin, route)).Body));
        Assert.Contains("Beta", Names((await Get(admin, route, "?status=inactive")).Body));
        Assert.Contains("Beta", Names((await Get(admin, route, "?status=all")).Body));

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, route, id)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"{route}/{id}")).Status);
    }

    [Theory]
    [MemberData(nameof(All))]
    public async Task A_duplicate_name_is_rejected_after_normalizing_case_and_whitespace(string route)
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, route, Body(route, "Science"));

        var (status, problem) = await Post(admin, route, Body(route, "  sCIENCE "));
        var (spaced, spacedProblem) = await Post(admin, route, Body(route, "Sci\t ence".Replace("\t", "")));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "name"));
        Assert.Equal(HttpStatusCode.Created, spaced);
        Assert.False(HasError(spacedProblem, "name"));
        Assert.Single((await Get(admin, route, "?search=science")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Repeated_internal_whitespace_does_not_make_a_name_different()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "education-groups", new { name = "Business Studies" });

        var (status, problem) = await Post(admin, "education-groups", new { name = "business    studies" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "name"));
    }

    [Theory]
    [MemberData(nameof(Coded))]
    public async Task A_duplicate_code_is_rejected_after_normalizing_case_but_missing_codes_never_collide(string route)
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, route, Body(route, "One", code: "cse-01"));

        var (status, problem) = await Post(admin, route, Body(route, "Two", code: " CSE-01 "));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "code"));

        // No code, or a whitespace-only code, is "absent": any number of records may leave it out.
        await CreateAsync(admin, route, Body(route, "Three"));
        await CreateAsync(admin, route, Body(route, "Four", code: "   "));
        await CreateAsync(admin, route, Body(route, "Five", code: ""));
    }

    [Theory]
    [MemberData(nameof(NameOnly))]
    public async Task A_catalog_without_codes_rejects_a_code(string route)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, route, new { name = "Spring", code = "SP" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "code"));
    }

    [Fact]
    public async Task The_same_name_is_allowed_in_different_catalogs()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Shared Name" });

        await CreateAsync(admin, "semesters", new { name = "shared name" });
        await CreateAsync(admin, "education-groups", new { name = "Shared Name" });
        await CreateAsync(admin, "academic-sessions", new { name = "Shared Name" });
        await CreateAsync(admin, "institutions", new { name = "Shared Name", type = "School" });
    }

    [Fact]
    public async Task An_institution_needs_one_of_the_three_fixed_types()
    {
        using var admin = await AdminAsync();

        var (missing, missingProblem) = await Post(admin, "institutions", new { name = "Dhaka College" });
        var (invalid, invalidProblem) = await Post(admin, "institutions", new { name = "Dhaka College", type = "Hospital" });
        var (numeric, _) = await Post(admin, "institutions", new { name = "Dhaka College", type = "7" });

        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.True(HasError(missingProblem, "type"));
        Assert.Equal(HttpStatusCode.BadRequest, invalid);
        Assert.True(HasError(invalidProblem, "type"));
        Assert.Equal(HttpStatusCode.BadRequest, numeric);

        var number = 0;
        foreach (var type in new[] { "School", "College", "University", "university" })
        {
            var (status, json) = await Post(admin, "institutions", new { name = $"Institution {++number}", type, address = " 1 Main Road " });
            Assert.Equal(HttpStatusCode.Created, status);
            Assert.Equal("1 Main Road", json.GetProperty("address").GetString());
        }
    }

    [Fact]
    public async Task A_type_or_address_is_rejected_where_the_resource_has_no_such_field()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "degrees", new { name = "BSc", type = "School", address = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "type"));
        Assert.True(HasError(problem, "address"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_name_is_required(string name)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "degrees", new { name });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("Enter a name.", FirstError(problem, "name"));
    }

    [Fact]
    public async Task Overlong_values_are_rejected_with_field_errors()
    {
        using var admin = await AdminAsync();

        var (_, problem) = await Post(admin, "degrees", new { name = new string('x', 201), code = new string('c', 51) });

        Assert.True(HasError(problem, "name"));
        Assert.True(HasError(problem, "code"));
    }

    [Fact]
    public async Task A_rejected_request_stores_nothing()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Keep" });

        await Post(admin, "degrees", new { name = "keep" });
        await Post(admin, "degrees", new { name = "" });

        Assert.Single((await Get(admin, "degrees", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Updating_a_record_to_another_records_name_is_rejected_but_keeping_its_own_is_allowed()
    {
        using var admin = await AdminAsync();
        var first = await CreateAsync(admin, "semesters", new { name = "Spring" });
        await CreateAsync(admin, "semesters", new { name = "Summer" });

        Assert.Equal(HttpStatusCode.BadRequest, (await Put(admin, "semesters", first, new { name = "summer" })).Status);
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, "semesters", first, new { name = "SPRING" })).Status);
    }

    [Fact]
    public async Task Search_matches_names_and_codes_without_regard_to_case_and_spacing()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Bachelor of IT", code = "BIT" });
        await CreateAsync(admin, "degrees", new { name = "Master of IT", code = "MIT" });
        await CreateAsync(admin, "designations", new { name = "Professor", code = "BIT-X" });

        Assert.Equal(["Bachelor of IT", "Master of IT"], Names((await Get(admin, "degrees", "?search=  of   it")).Body));
        Assert.Equal(["Bachelor of IT"], Names((await Get(admin, "degrees", "?search=bit")).Body));
        Assert.Equal(["Professor"], Names((await Get(admin, "designations", "?search=bit")).Body));
        Assert.Empty(Names((await Get(admin, "degrees", "?search=nothing")).Body));
    }

    [Fact]
    public async Task Lists_are_ordered_and_pages_follow_cursors_without_gaps_or_repeats()
    {
        using var admin = await AdminAsync();
        foreach (var name in new[] { "echo", "alpha", "delta", "bravo", "charlie" })
        {
            await CreateAsync(admin, "semesters", new { name });
        }

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var query = $"?pageSize=2{(cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor))}";
            var (status, page) = await Get(admin, "semesters", query);
            Assert.Equal(HttpStatusCode.OK, status);
            seen.AddRange(Names(page));
            cursor = page.GetProperty("nextCursor").ValueKind == JsonValueKind.String ? page.GetProperty("nextCursor").GetString() : null;
            pages++;
        }
        while (cursor is not null && pages < 10);

        Assert.Equal(["alpha", "bravo", "charlie", "delta", "echo"], seen);
        Assert.Equal(3, pages);
    }

    [Theory]
    [InlineData("?status=sometimes", "status")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?cursor=garbage", "cursor")]
    public async Task Invalid_list_options_are_rejected_with_a_field_error(string query, string field)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Get(admin, "degrees", query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field));
    }

    [Fact]
    public async Task Unknown_records_and_resources_are_not_found()
    {
        using var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"degrees/{Guid.NewGuid()}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Put(admin, "degrees", Guid.NewGuid(), new { name = "x" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(admin, "degrees", Guid.NewGuid())).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "no-such-catalog")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "divisions")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, "degrees/not-a-guid")).Status);
    }

    [Fact]
    public async Task Creating_a_catalog_record_creates_no_student_teacher_or_batch_records()
    {
        using var admin = await AdminAsync();

        await CreateAsync(admin, "degrees", new { name = "BSc" });

        Assert.Empty((await Get(admin, "batches", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_catalog_record_can_be_reactivated()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, "semesters", new { name = "Fall", isActive = false });

        Assert.DoesNotContain("Fall", Names((await Get(admin, "semesters")).Body));
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, "semesters", id, new { name = "Fall", isActive = true })).Status);
        Assert.Contains("Fall", Names((await Get(admin, "semesters")).Body));
    }

    [Fact]
    public async Task A_numbered_page_returns_its_slice_with_the_total_and_page_count()
    {
        using var admin = await AdminAsync();
        foreach (var name in new[] { "g", "c", "a", "e", "b", "f", "d" })
        {
            await CreateAsync(admin, "semesters", new { name });
        }

        var (status, second) = await Get(admin, "semesters", "?pageSize=3&page=2");
        var (_, last) = await Get(admin, "semesters", "?pageSize=3&page=3");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["d", "e", "f"], Names(second));
        Assert.Equal(7, second.GetProperty("totalCount").GetInt64());
        Assert.Equal(3, second.GetProperty("totalPages").GetInt32());
        Assert.Equal(2, second.GetProperty("page").GetInt32());
        Assert.Equal(["g"], Names(last));
    }

    [Fact]
    public async Task The_total_counts_only_what_the_search_and_status_match()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Bachelor of IT", code = "BIT" });
        await CreateAsync(admin, "degrees", new { name = "Master of IT" });
        await CreateAsync(admin, "degrees", new { name = "Retired degree", isActive = false });

        var active = (await Get(admin, "degrees", "?page=1")).Body;
        var matching = (await Get(admin, "degrees", "?page=1&search=bit")).Body;
        var all = (await Get(admin, "degrees", "?page=1&status=all")).Body;

        Assert.Equal(2, active.GetProperty("totalCount").GetInt64());
        Assert.Equal(1, matching.GetProperty("totalCount").GetInt64());
        Assert.Equal(3, all.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_page_past_the_end_is_empty_but_still_reports_the_total()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "semesters", new { name = "Only" });

        var (_, page) = await Get(admin, "semesters", "?page=9");

        Assert.Empty(Names(page));
        Assert.Equal(1, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(1, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task An_empty_list_reports_zero_pages()
    {
        using var admin = await AdminAsync();

        var (_, page) = await Get(admin, "semesters", "?page=1");

        Assert.Equal(0, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(0, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task A_parent_filter_limits_the_numbered_page_and_its_total()
    {
        using var admin = await AdminAsync();
        var a = await CreateAsync(admin, "geography/divisions", new { name = "A" });
        var b = await CreateAsync(admin, "geography/divisions", new { name = "B" });
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await CreateAsync(admin, "geography/districts", new { name, divisionId = a });
        }

        await CreateAsync(admin, "geography/districts", new { name = "Elsewhere", divisionId = b });

        var (_, page) = await Get(admin, "geography/districts", $"?page=1&pageSize=2&divisionId={a}");

        Assert.Equal(["One", "Three"], Names(page));
        Assert.Equal(3, page.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, page.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task Cursor_paging_does_not_count_and_omits_the_total_fields()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "semesters", new { name = "Spring" });

        var (_, page) = await Get(admin, "semesters");

        Assert.False(page.TryGetProperty("totalCount", out _));
        Assert.False(page.TryGetProperty("totalPages", out _));
        Assert.False(page.TryGetProperty("page", out _));
    }

    [Theory]
    [InlineData("?page=0")]
    [InlineData("?page=-2")]
    public async Task An_invalid_page_number_is_rejected(string query)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Get(admin, "degrees", query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "page"));
    }

    [Fact]
    public async Task A_page_number_and_a_cursor_cannot_be_combined()
    {
        using var admin = await AdminAsync();
        foreach (var name in new[] { "a", "b", "c" })
        {
            await CreateAsync(admin, "semesters", new { name });
        }

        var (_, first) = await Get(admin, "semesters", "?pageSize=1");
        var cursor = first.GetProperty("nextCursor").GetString();

        var (status, problem) = await Get(admin, "semesters", $"?pageSize=1&page=2&cursor={Uri.EscapeDataString(cursor!)}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "page"));
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Every_page_size_the_screens_offer_is_honored_with_the_right_total_and_page_count(int pageSize)
    {
        await Factory.WithDbAsync(async db =>
        {
            for (var i = 0; i < 105; i++)
            {
                var semester = new IitAcademicPortal.Domain.Configuration.Semester();
                semester.Rename($"Size {i:000}");
                db.Semesters.Add(semester);
            }

            await db.SaveChangesAsync();
        });
        using var admin = await AdminAsync();
        var pages = (105 + pageSize - 1) / pageSize;

        var (_, first) = await Get(admin, "semesters", $"?pageSize={pageSize}&page=1");
        var (_, last) = await Get(admin, "semesters", $"?pageSize={pageSize}&page={pages}");

        Assert.Equal(pageSize, first.GetProperty("items").GetArrayLength());
        Assert.Equal(105, first.GetProperty("totalCount").GetInt64());
        Assert.Equal(pages, first.GetProperty("totalPages").GetInt32());
        Assert.Equal(105 - (pages - 1) * pageSize, last.GetProperty("items").GetArrayLength());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(500)]
    public async Task A_page_size_outside_one_to_one_hundred_is_rejected_in_numbered_mode_too(int pageSize)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Get(admin, "semesters", $"?page=1&pageSize={pageSize}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "pageSize"));
    }

    [Fact]
    public async Task A_hall_has_an_optional_code_and_address_and_depends_on_nothing()
    {
        using var admin = await AdminAsync();

        var (status, json) = await Post(admin, "halls", new { name = "  Fazlul Huq Hall ", code = " fh ", address = "  University of Dhaka, Dhaka-1000 " });
        var (bare, _) = await Post(admin, "halls", new { name = "Another Hall" });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Fazlul Huq Hall", json.GetProperty("name").GetString());
        Assert.Equal("fh", json.GetProperty("code").GetString());
        Assert.Equal("University of Dhaka, Dhaka-1000", json.GetProperty("address").GetString());
        Assert.Equal(HttpStatusCode.Created, bare);
        Assert.False((await Get(admin, "halls/" + json.GetProperty("id").GetGuid())).Body.TryGetProperty("type", out _));
        Assert.Equal(["Another Hall", "Fazlul Huq Hall"], Names((await Get(admin, "halls")).Body));
    }

    [Fact]
    public async Task A_hall_address_is_limited_and_a_blank_one_is_absent()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "halls", new { name = "Long Address Hall", address = new string('x', 501) });
        var (_, blank) = await Post(admin, "halls", new { name = "Blank Address Hall", address = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "address"));
        Assert.False(blank.TryGetProperty("address", out _));
    }

    [Fact]
    public async Task A_delete_is_soft_the_row_stays_marked_and_the_record_disappears_everywhere()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, "halls", new { name = "Soft Hall", code = "SH", address = "Somewhere" });

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, "halls", id)).Status);

        Assert.Equal(HttpStatusCode.NotFound, (await Get(admin, $"halls/{id}")).Status);
        Assert.DoesNotContain("Soft Hall", Names((await Get(admin, "halls", "?status=all")).Body));
        Assert.Equal(0, (await Get(admin, "halls", "?page=1&status=all&search=soft")).Body.GetProperty("totalCount").GetInt64());
        // A deleted record cannot be edited or deleted again, and a second delete does not touch the row.
        Assert.Equal(HttpStatusCode.NotFound, (await Put(admin, "halls", id, new { name = "Back again" })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await Delete(admin, "halls", id)).Status);

        await Factory.WithDbAsync(async db =>
        {
            var row = await db.Halls.IgnoreQueryFilters().SingleAsync(h => h.Id == id);
            Assert.True(row.IsDeleted);
            Assert.Equal(AdminUserId, row.DeletedByUserId);
            Assert.InRange(row.DeletedAtUtc!.Value, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal("Soft Hall", row.Name);
            Assert.Equal("Somewhere", row.Address);
        });
    }

    [Theory]
    [MemberData(nameof(All))]
    public async Task A_deleted_records_name_can_be_created_again_in_every_catalog(string route)
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, route, Body(route, "Recurring"));
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, route, id)).Status);

        var again = await CreateAsync(admin, route, Body(route, "  RECURRING "));

        Assert.NotEqual(id, again);
        Assert.Equal(["RECURRING"], Names((await Get(admin, route, "?status=all")).Body));
        // The live record is protected again.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, route, Body(route, "recurring"))).Status);
    }

    [Theory]
    [MemberData(nameof(Coded))]
    public async Task A_deleted_records_code_can_be_used_again(string route)
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, route, Body(route, "First", code: "CODE-1"));
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, route, Body(route, "Second", code: "code-1"))).Status);
        await Delete(admin, route, id);

        Assert.Equal(HttpStatusCode.Created, (await Post(admin, route, Body(route, "Second", code: "code-1"))).Status);
    }

    [Fact]
    public async Task Deleting_does_not_change_the_other_records_or_the_totals_of_the_rest()
    {
        using var admin = await AdminAsync();
        foreach (var name in new[] { "a", "b", "c", "d" })
        {
            await CreateAsync(admin, "semesters", new { name });
        }

        var (_, all) = await Get(admin, "semesters", "?pageSize=1&page=2");
        var b = all.GetProperty("items")[0].GetProperty("id").GetGuid();
        await Delete(admin, "semesters", b);

        var (_, page) = await Get(admin, "semesters", "?page=1");
        Assert.Equal(["a", "c", "d"], Names(page));
        Assert.Equal(3, page.GetProperty("totalCount").GetInt64());
    }

    [Fact]
    public async Task A_degree_requires_positive_credits_to_be_completed_and_round_trips_the_value()
    {
        using var admin = await AdminAsync();

        var (missing, missingProblem) = await Post(admin, "degrees", new { name = "Missing Credits" });
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.True(HasError(missingProblem, "creditsToBeCompleted"));

        var (created, item) = await Post(admin, "degrees", new { name = "Bachelor of IT", creditsToBeCompleted = 120.50m, minimumCgpaForCompletion = 2.50m });
        Assert.Equal(HttpStatusCode.Created, created);
        Assert.Equal(120.50m, item.GetProperty("creditsToBeCompleted").GetDecimal());
        var id = item.GetProperty("id").GetGuid();

        var (read, loaded) = await Get(admin, $"degrees/{id}");
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal(120.50m, loaded.GetProperty("creditsToBeCompleted").GetDecimal());

        var (updated, changed) = await Put(admin, "degrees", id, new { name = "Bachelor of IT", creditsToBeCompleted = 40.25m, minimumCgpaForCompletion = 2.50m });
        Assert.Equal(HttpStatusCode.OK, updated);
        Assert.Equal(40.25m, changed.GetProperty("creditsToBeCompleted").GetDecimal());
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(1.234d)]
    public async Task A_degree_rejects_non_positive_or_over_precision_credit_totals_without_writes(double credits)
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Existing Degree", creditsToBeCompleted = 120m, minimumCgpaForCompletion = 2.50m });

        var (status, problem) = await Post(admin, "degrees", new { name = $"Invalid Degree {credits}", creditsToBeCompleted = credits });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "creditsToBeCompleted"));
        Assert.Single((await Get(admin, "degrees", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_degree_requires_a_minimum_cgpa_for_completion_and_round_trips_it()
    {
        using var admin = await AdminAsync();

        var (missing, missingProblem) = await Post(admin, "degrees", new { name = "No CGPA", creditsToBeCompleted = 120m });
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.Equal("Enter the minimum CGPA for completion.", FirstError(missingProblem, "minimumCgpaForCompletion"));

        var (created, item) = await Post(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m, minimumCgpaForCompletion = 2.75m });
        Assert.Equal(HttpStatusCode.Created, created);
        Assert.Equal(2.75m, item.GetProperty("minimumCgpaForCompletion").GetDecimal());
        var id = item.GetProperty("id").GetGuid();
        Assert.Equal(2.75m, (await Get(admin, $"degrees/{id}")).Body.GetProperty("minimumCgpaForCompletion").GetDecimal());

        var (updated, changed) = await Put(admin, "degrees", id, new { name = "EMIT", creditsToBeCompleted = 48m, minimumCgpaForCompletion = 4.00m });
        Assert.Equal(HttpStatusCode.OK, updated);
        Assert.Equal(4.00m, changed.GetProperty("minimumCgpaForCompletion").GetDecimal());

        // An update must state it too: leaving it out does not keep the old value.
        var (omitted, omittedProblem) = await Put(admin, "degrees", id, new { name = "EMIT", creditsToBeCompleted = 48m });
        Assert.Equal(HttpStatusCode.BadRequest, omitted);
        Assert.True(HasError(omittedProblem, "minimumCgpaForCompletion"));
        Assert.Equal(4.00m, (await Get(admin, $"degrees/{id}")).Body.GetProperty("minimumCgpaForCompletion").GetDecimal());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("4.01")]
    [InlineData("5")]
    [InlineData("2.555")]
    public async Task A_degree_rejects_a_minimum_cgpa_outside_the_four_point_scale_without_writes(string cgpa)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "degrees", new { name = $"Bad CGPA {cgpa}", creditsToBeCompleted = 120m, minimumCgpaForCompletion = decimal.Parse(cgpa, System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("at most 4.00", FirstError(problem, "minimumCgpaForCompletion"));
        Assert.Empty((await Get(admin, "degrees", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("4")]
    public async Task A_degree_accepts_the_ends_of_the_cgpa_range(string cgpa)
    {
        using var admin = await AdminAsync();
        var value = decimal.Parse(cgpa, System.Globalization.CultureInfo.InvariantCulture);

        var (status, item) = await Post(admin, "degrees", new { name = $"Edge {cgpa}", creditsToBeCompleted = 120m, minimumCgpaForCompletion = value });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(value, item.GetProperty("minimumCgpaForCompletion").GetDecimal());
    }

    [Fact]
    public async Task A_non_degree_catalog_rejects_a_minimum_cgpa()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "semesters", new { name = "Spring", minimumCgpaForCompletion = 2.50m });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "minimumCgpaForCompletion"));
        Assert.False((await Get(admin, "semesters", "?status=all")).Body.GetProperty("items").EnumerateArray().Any());
    }

    [Fact]
    public async Task A_non_degree_catalog_rejects_credits_to_be_completed()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "semesters", new { name = "Spring", creditsToBeCompleted = 30 });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "creditsToBeCompleted"));
        Assert.Empty((await Get(admin, "semesters", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task An_institution_list_filters_by_type()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "institutions", new { name = "Dhaka College", type = "College" });
        await CreateAsync(admin, "institutions", new { name = "Motijheel School", type = "School" });

        var colleges = (await Get(admin, "institutions", "?type=college")).Body;
        var (status, problem) = await Get(admin, "institutions", "?type=Elective");

        Assert.Equal(["Dhaka College"], Names(colleges));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "type"));
    }
}
