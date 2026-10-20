using System.Net;
using System.Text.Json;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// US1 business rules for the course catalog: required fields, credit totals and optional parts, global code uniqueness with shared
/// names allowed, prerequisite validity (same Degree, active, not self, no cycle), Degree changes that would break a prerequisite,
/// and deactivation blocked by active dependents. Every rejection changes nothing.
/// </summary>
public sealed class CourseCatalogTests : ConfigurationTestBase
{
    private const string Courses = "courses";

    private async Task<Guid> DegreeAsync(Infrastructure.PortalClient admin, string name) =>
        await CreateAsync(admin, "degrees", new { name, creditsToBeCompleted = 120m, minimumCgpaForCompletion = 2.50m });

    private static Dictionary<string, object?> Course(
        Guid degreeId, string code, string name = "Course", decimal? creditHours = 3m, decimal? theory = null, decimal? lab = null,
        string? type = "Mandatory", Guid? prerequisite = null, bool? isActive = null) => new()
        {
            ["name"] = name,
            ["code"] = code,
            ["creditHours"] = creditHours,
            ["theoryCreditHours"] = theory,
            ["labCreditHours"] = lab,
            ["type"] = type,
            ["degreeId"] = degreeId,
            ["prerequisiteCourseId"] = prerequisite,
            ["isActive"] = isActive,
        };

    private static decimal? Dec(string? value) =>
        value is null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private async Task<string> SnapshotAsync()
    {
        var rows = new List<string>();
        await Factory.WithDbAsync(async db => rows.AddRange(await db.Courses.IgnoreQueryFilters().AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => $"{c.Code}|{c.Name}|{c.DegreeId}|{c.PrerequisiteCourseId}|{c.IsActive}|{c.CreditHours}|{c.TheoryCreditHours}|{c.LabCreditHours}|{c.Type}")
            .ToListAsync()));
        return string.Join('\n', rows);
    }

    [Fact]
    public async Task A_valid_course_is_created_active_with_its_fields_and_two_courses_may_share_a_name()
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "EMIT");

        var (status, body) = await Post(admin, Courses, Course(degree, " cse-101 ", name: "Programming", creditHours: 3.5m, theory: 2.5m, lab: 1m));
        var (same, _) = await Post(admin, Courses, Course(degree, "CSE-102", name: "programming"));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("cse-101", body.GetProperty("code").GetString());
        Assert.Equal(3.5m, body.GetProperty("creditHours").GetDecimal());
        Assert.Equal(2.5m, body.GetProperty("theoryCreditHours").GetDecimal());
        Assert.Equal(1m, body.GetProperty("labCreditHours").GetDecimal());
        Assert.Equal("Mandatory", body.GetProperty("type").GetString());
        Assert.Equal(degree, body.GetProperty("degreeId").GetGuid());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.False(body.TryGetProperty("prerequisiteCourseId", out _));
        Assert.Equal(HttpStatusCode.Created, same);
    }

    [Theory]
    [InlineData("code", null)]
    [InlineData("code", "  ")]
    [InlineData("type", null)]
    [InlineData("type", "Optional")]
    [InlineData("type", "1")]
    [InlineData("creditHours", null)]
    [InlineData("degreeId", null)]
    [InlineData("name", "")]
    public async Task A_missing_or_invalid_required_field_is_a_field_keyed_400(string field, string? value)
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "Degree A");
        var body = Course(degree, "REQ-1");
        body[field] = value;

        var (status, problem) = await Post(admin, Courses, body);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field), problem.ToString());
        Assert.Equal(string.Empty, await SnapshotAsync());
    }

    [Theory]
    [InlineData("0", null, null, "creditHours")]
    [InlineData("-1", null, null, "creditHours")]
    [InlineData("3.125", null, null, "creditHours")]
    [InlineData("3", "-1", null, "theoryCreditHours")]
    [InlineData("3", "3.5", null, "theoryCreditHours")]
    [InlineData("3", null, "4", "labCreditHours")]
    [InlineData("3", "1.005", null, "theoryCreditHours")]
    [InlineData("3", "2", "2", "labCreditHours")]
    [InlineData("3", "1", "1", "labCreditHours")]
    public async Task Invalid_credit_values_are_rejected_on_the_right_field(string total, string? theory, string? lab, string field)
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "Degree B");

        var (status, problem) = await Post(admin, Courses, Course(degree, "CR-1", creditHours: Dec(total), theory: Dec(theory), lab: Dec(lab)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field), problem.ToString());
    }

    [Theory]
    [InlineData("3", "3", null)]
    [InlineData("3", null, "0")]
    [InlineData("3", "0", "3")]
    [InlineData("4.5", "3", "1.5")]
    public async Task One_part_alone_or_two_parts_summing_to_the_total_are_valid(string total, string? theory, string? lab)
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "Degree C");

        var (status, _) = await Post(admin, Courses, Course(degree, "OK-1", creditHours: Dec(total), theory: Dec(theory), lab: Dec(lab)));

        Assert.Equal(HttpStatusCode.Created, status);
    }

    [Fact]
    public async Task A_code_is_unique_across_every_degree_including_inactive_courses()
    {
        using var admin = await AdminAsync();
        var emit = await DegreeAsync(admin, "EMIT");
        var pgdit = await DegreeAsync(admin, "PGDIT");
        var existing = await CreateAsync(admin, Courses, Course(emit, "SHARED-1"));
        await Put(admin, Courses, existing, Course(emit, "SHARED-1", isActive: false));
        var before = await SnapshotAsync();

        var (otherDegree, problem) = await Post(admin, Courses, Course(pgdit, "shared-1"));

        Assert.Equal(HttpStatusCode.BadRequest, otherDegree);
        Assert.Contains("code already exists", FirstError(problem, "code"));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task A_prerequisite_must_be_an_active_course_of_the_same_degree()
    {
        using var admin = await AdminAsync();
        var emit = await DegreeAsync(admin, "EMIT");
        var pgdit = await DegreeAsync(admin, "PGDIT");
        var basics = await CreateAsync(admin, Courses, Course(emit, "EM-100"));
        var otherDegree = await CreateAsync(admin, Courses, Course(pgdit, "PG-100"));
        var retired = await CreateAsync(admin, Courses, Course(emit, "EM-099", isActive: false));

        var (valid, body) = await Post(admin, Courses, Course(emit, "EM-200", prerequisite: basics));
        var (cross, crossProblem) = await Post(admin, Courses, Course(emit, "EM-201", prerequisite: otherDegree));
        var (inactive, inactiveProblem) = await Post(admin, Courses, Course(emit, "EM-202", prerequisite: retired));
        var (missing, missingProblem) = await Post(admin, Courses, Course(emit, "EM-203", prerequisite: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Created, valid);
        Assert.Equal(basics, body.GetProperty("prerequisiteCourseId").GetGuid());
        Assert.Equal(HttpStatusCode.BadRequest, cross);
        Assert.Contains("same degree", FirstError(crossProblem, "prerequisiteCourseId"));
        Assert.Equal(HttpStatusCode.BadRequest, inactive);
        Assert.Contains("inactive", FirstError(inactiveProblem, "prerequisiteCourseId"));
        Assert.Equal(HttpStatusCode.BadRequest, missing);
        Assert.True(HasError(missingProblem, "prerequisiteCourseId"));
    }

    [Fact]
    public async Task A_course_cannot_be_its_own_prerequisite_or_close_a_cycle_and_nothing_changes()
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "EMIT");
        var a = await CreateAsync(admin, Courses, Course(degree, "A-1"));
        var b = await CreateAsync(admin, Courses, Course(degree, "B-1", prerequisite: a));
        var c = await CreateAsync(admin, Courses, Course(degree, "C-1", prerequisite: b));
        var before = await SnapshotAsync();

        var (self, selfProblem) = await Put(admin, Courses, a, Course(degree, "A-1", prerequisite: a));
        var (cycle, cycleProblem) = await Put(admin, Courses, a, Course(degree, "A-1", prerequisite: c));

        Assert.Equal(HttpStatusCode.BadRequest, self);
        Assert.Contains("its own prerequisite", FirstError(selfProblem, "prerequisiteCourseId"));
        Assert.Equal(HttpStatusCode.BadRequest, cycle);
        Assert.Contains("cycle", FirstError(cycleProblem, "prerequisiteCourseId"));
        Assert.Equal(before, await SnapshotAsync());
    }

    [Fact]
    public async Task A_degree_change_that_would_break_a_prerequisite_is_rejected_without_changing_any_course()
    {
        using var admin = await AdminAsync();
        var emit = await DegreeAsync(admin, "EMIT");
        var pgdit = await DegreeAsync(admin, "PGDIT");
        var basics = await CreateAsync(admin, Courses, Course(emit, "EM-100"));
        var advanced = await CreateAsync(admin, Courses, Course(emit, "EM-200", prerequisite: basics));
        var before = await SnapshotAsync();

        // Its own prerequisite would be in another Degree.
        var (own, ownProblem) = await Put(admin, Courses, advanced, Course(pgdit, "EM-200", prerequisite: basics));
        // An active course depending on it would be left in another Degree.
        var (dependent, dependentProblem) = await Put(admin, Courses, basics, Course(pgdit, "EM-100"));

        Assert.Equal(HttpStatusCode.BadRequest, own);
        Assert.True(HasError(ownProblem, "prerequisiteCourseId"));
        Assert.Equal(HttpStatusCode.BadRequest, dependent);
        Assert.Contains("prerequisite", FirstError(dependentProblem, "degreeId"));
        Assert.Equal(before, await SnapshotAsync());

        // Once the dependent no longer needs it, the Degree can change.
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, Courses, advanced, Course(emit, "EM-200"))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, Courses, basics, Course(pgdit, "EM-100"))).Status);
    }

    [Fact]
    public async Task A_course_an_active_course_depends_on_cannot_be_deactivated_until_the_dependency_ends()
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "EMIT");
        var basics = await CreateAsync(admin, Courses, Course(degree, "EM-100"));
        var advanced = await CreateAsync(admin, Courses, Course(degree, "EM-200", prerequisite: basics));
        var before = await SnapshotAsync();

        var (status, problem) = await Put(admin, Courses, basics, Course(degree, "EM-100", isActive: false));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("cannot be deactivated", problem.GetProperty("detail").GetString());
        Assert.Equal(before, await SnapshotAsync());
        Assert.Empty(await Factory.AuditEventsAsync("configuration.course.deactivated"));

        // An inactive dependent no longer blocks it.
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, Courses, advanced, Course(degree, "EM-200", prerequisite: basics, isActive: false))).Status);
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, Courses, basics, Course(degree, "EM-100", isActive: false))).Status);
    }

    [Fact]
    public async Task Reactivating_a_course_needs_its_prerequisite_to_be_active_again()
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "EMIT");
        var basics = await CreateAsync(admin, Courses, Course(degree, "EM-100"));
        var advanced = await CreateAsync(admin, Courses, Course(degree, "EM-200", prerequisite: basics));
        await Put(admin, Courses, advanced, Course(degree, "EM-200", prerequisite: basics, isActive: false));
        await Put(admin, Courses, basics, Course(degree, "EM-100", isActive: false));

        var (status, problem) = await Put(admin, Courses, advanced, Course(degree, "EM-200", prerequisite: basics, isActive: true));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("inactive", FirstError(problem, "prerequisiteCourseId"));
    }

    [Fact]
    public async Task A_degree_with_courses_cannot_be_deleted()
    {
        using var admin = await AdminAsync();
        var degree = await DegreeAsync(admin, "EMIT");
        await CreateAsync(admin, Courses, Course(degree, "EM-100"));

        var (status, _) = await Delete(admin, "degrees", degree);

        Assert.Equal(HttpStatusCode.Conflict, status);
    }
}
