using System.Net;
using System.Text.Json;
using IitAcademicPortal.Domain.Configuration;
using IitAcademicPortal.Domain.Students;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// US1 HTTP contract of <c>/api/admin/configuration/courses</c>: list filters (search, status, Degree), the read shape, clearing the
/// optional prerequisite, no DELETE, and that course changes never touch student admissions or academic history.
/// </summary>
public sealed class CourseCatalogApiTests : ConfigurationTestBase
{
    private const string Courses = "courses";

    private static object Course(Guid degreeId, string code, string name, Guid? prerequisite = null, bool isActive = true, string type = "Mandatory") =>
        new { name, code, creditHours = 3m, type, degreeId, prerequisiteCourseId = prerequisite, isActive };

    private static List<string> Codes(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToList();

    [Fact]
    public async Task The_list_searches_name_and_code_filters_by_status_and_degree_and_pages()
    {
        using var admin = await AdminAsync();
        var emit = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        var pgdit = await CreateAsync(admin, "degrees", new { name = "PGDIT", creditsToBeCompleted = 36m });
        await CreateAsync(admin, Courses, Course(emit, "EM-101", "Networking"));
        await CreateAsync(admin, Courses, Course(emit, "EM-102", "Databases"));
        await CreateAsync(admin, Courses, Course(pgdit, "PG-101", "Networking Basics"));
        await CreateAsync(admin, Courses, Course(emit, "EM-900", "Retired", isActive: false));

        var (_, byName) = await Get(admin, Courses, "?search=network");
        var (_, byCode) = await Get(admin, Courses, "?search=em-102");
        var (_, ofEmit) = await Get(admin, Courses, $"?degreeId={emit}");
        var (_, inactive) = await Get(admin, Courses, "?status=inactive");
        var (_, all) = await Get(admin, Courses, $"?status=all&degreeId={emit}");
        var (_, paged) = await Get(admin, Courses, "?page=1&pageSize=2");

        Assert.Equal(["EM-101", "PG-101"], Codes(byName));
        Assert.Equal(["EM-102"], Codes(byCode));
        Assert.Equal(["EM-101", "EM-102"], Codes(ofEmit));
        Assert.Equal(["EM-900"], Codes(inactive));
        Assert.Equal(3, Codes(all).Count);
        Assert.Equal(["EM-101", "EM-102"], Codes(paged));
        Assert.Equal(3, paged.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, paged.GetProperty("totalPages").GetInt32());
    }

    [Fact]
    public async Task The_list_filters_by_type_with_the_other_filters_and_refuses_an_unknown_type()
    {
        using var admin = await AdminAsync();
        var emit = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        var pgdit = await CreateAsync(admin, "degrees", new { name = "PGDIT", creditsToBeCompleted = 36m });
        await CreateAsync(admin, Courses, Course(emit, "EM-101", "Networking"));
        await CreateAsync(admin, Courses, Course(emit, "EM-201", "Cloud", type: "Elective"));
        await CreateAsync(admin, Courses, Course(pgdit, "PG-201", "Security", type: "Elective"));

        var (_, electives) = await Get(admin, Courses, "?type=elective");
        var (_, emitElectives) = await Get(admin, Courses, $"?type=Elective&degreeId={emit}");
        var (_, mandatory) = await Get(admin, Courses, "?type=Mandatory&pageSize=1&page=1");
        var (unknown, unknownProblem) = await Get(admin, Courses, "?type=Optional");
        var (numeric, numericProblem) = await Get(admin, Courses, "?type=1");
        var (untyped, untypedProblem) = await Get(admin, "halls", "?type=Mandatory");

        Assert.Equal(["EM-201", "PG-201"], Codes(electives));
        Assert.Equal(["EM-201"], Codes(emitElectives));
        Assert.Equal(["EM-101"], Codes(mandatory));
        Assert.Equal(1, mandatory.GetProperty("totalCount").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, unknown);
        Assert.True(HasError(unknownProblem, "type"));
        Assert.Equal(HttpStatusCode.BadRequest, numeric);
        Assert.True(HasError(numericProblem, "type"));
        Assert.Equal(HttpStatusCode.BadRequest, untyped);
        Assert.True(HasError(untypedProblem, "type"));
    }

    [Fact]
    public async Task The_list_is_ordered_by_code_in_both_paging_modes()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        // Names in the opposite order to codes, and a repeated name, so a name order would show.
        await CreateAsync(admin, Courses, Course(degree, "EM-103", "Algorithms"));
        await CreateAsync(admin, Courses, Course(degree, "EM-101", "Systems"));
        await CreateAsync(admin, Courses, Course(degree, "EM-102", "Systems"));

        var cursorCodes = new List<string>();
        var query = "?pageSize=1";
        while (true)
        {
            var (_, page) = await Get(admin, Courses, query);
            cursorCodes.AddRange(Codes(page));
            if (!page.TryGetProperty("nextCursor", out var next) || next.ValueKind != JsonValueKind.String)
            {
                break;
            }

            query = $"?pageSize=1&cursor={next.GetString()}";
        }

        var numbered = new List<string>();
        for (var number = 1; number <= 3; number++)
        {
            var (_, page) = await Get(admin, Courses, $"?page={number}&pageSize=1");
            numbered.AddRange(Codes(page));
        }

        Assert.Equal(["EM-101", "EM-102", "EM-103"], cursorCodes);
        Assert.Equal(["EM-101", "EM-102", "EM-103"], numbered);
    }

    [Fact]
    public async Task A_course_reads_with_its_fields_and_audit_facts_and_the_prerequisite_can_be_cleared()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        var basics = await CreateAsync(admin, Courses, Course(degree, "EM-100", "Basics"));
        var advanced = await CreateAsync(admin, Courses, new { name = "Advanced", code = "EM-200", creditHours = 4m, theoryCreditHours = 3m, type = "Elective", degreeId = degree, prerequisiteCourseId = basics });

        var (status, body) = await Get(admin, $"{Courses}/{advanced}");
        var (cleared, clearedBody) = await Put(admin, Courses, advanced, Course(degree, "EM-200", "Advanced"));
        var (missing, _) = await Get(admin, $"{Courses}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("EM-200", body.GetProperty("code").GetString());
        Assert.Equal(4m, body.GetProperty("creditHours").GetDecimal());
        Assert.Equal(3m, body.GetProperty("theoryCreditHours").GetDecimal());
        Assert.False(body.TryGetProperty("labCreditHours", out _));
        Assert.Equal("Elective", body.GetProperty("type").GetString());
        Assert.Equal(basics, body.GetProperty("prerequisiteCourseId").GetGuid());
        Assert.Equal(AdminUserId, body.GetProperty("createdByUserId").GetString());
        Assert.Equal(HttpStatusCode.OK, cleared);
        Assert.False(clearedBody.TryGetProperty("prerequisiteCourseId", out _));
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    [Fact]
    public async Task Course_fields_are_refused_on_other_catalogs_and_other_catalog_fields_on_a_course()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });

        var (onDegree, degreeProblem) = await Post(admin, "degrees", new { name = "X", creditsToBeCompleted = 10m, creditHours = 3m });
        var (onCourse, courseProblem) = await Post(admin, Courses, new { name = "Y", code = "Y-1", creditHours = 3m, type = "Mandatory", degreeId = degree, address = "Somewhere" });

        Assert.Equal(HttpStatusCode.BadRequest, onDegree);
        Assert.True(HasError(degreeProblem, "creditHours"));
        Assert.Equal(HttpStatusCode.BadRequest, onCourse);
        Assert.True(HasError(courseProblem, "address"));
    }

    [Fact]
    public async Task There_is_no_delete_for_a_course_and_the_course_stays()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        var course = await CreateAsync(admin, Courses, Course(degree, "EM-100", "Basics"));

        using var response = await admin.SendAsync(HttpMethod.Delete, $"{Root}/{Courses}/{course}");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal("GET, POST, PUT", string.Join(", ", response.Content.Headers.Allow.Count > 0 ? response.Content.Headers.Allow : response.Headers.GetValues("Allow")));
        Assert.Contains("Deactivate it instead", (await Infrastructure.PortalClient.ReadJsonAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Get(admin, $"{Courses}/{course}")).Status);
        Assert.Empty(await Factory.AuditEventsAsync("configuration.course.deleted"));
    }

    [Fact]
    public async Task Course_changes_never_touch_student_admissions_or_academic_history()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "EMIT", creditsToBeCompleted = 48m });
        string before = string.Empty;
        await Factory.WithDbAsync(async db =>
        {
            var userId = (await db.Users.FirstAsync()).Id;
            var semester = new Semester();
            semester.Rename("Spring");
            var session = new AcademicSession();
            session.Rename("2026-2027");
            db.AddRange(semester, session);
            await db.SaveChangesAsync();
            var batch = new Batch { DegreeId = degree, StartingSemesterId = semester.Id, AcademicSessionId = session.Id };
            batch.Rename("Batch 1");
            db.Add(batch);
            await db.SaveChangesAsync();
            var profile = StudentProfile.Create(userId);
            profile.ApplicantName = "Seeded Student";
            profile.ApplicantMobile = "01700000000";
            var history = StudentAcademicHistory.Create(profile.Id);
            history.Category = AcademicHistoryCategory.Master;
            history.University = "DU";
            profile.AcademicHistory.Add(history);
            db.Add(profile);
            var admission = StudentAdmissionRecord.Create(profile.Id);
            admission.DegreeId = degree;
            admission.BatchId = batch.Id;
            admission.AdmissionSerialNumber = "S";
            admission.AdmissionExamRoll = "R";
            db.Add(admission);
            await db.SaveChangesAsync();
        });

        async Task<string> StudentRowsAsync()
        {
            var rows = string.Empty;
            await Factory.WithDbAsync(async db =>
            {
                var admissions = await db.StudentAdmissions.AsNoTracking().Select(a => $"{a.Id}|{a.DegreeId}|{a.BatchId}|{a.LastModifiedAtUtc:O}").ToListAsync();
                var history = await db.StudentAcademicHistoryEntries.AsNoTracking().Select(h => $"{h.Id}|{h.University}|{h.LastModifiedAtUtc:O}").ToListAsync();
                rows = string.Join('\n', admissions.Concat(history));
            });
            return rows;
        }

        before = await StudentRowsAsync();
        var course = await CreateAsync(admin, Courses, Course(degree, "EM-100", "Basics"));
        await Put(admin, Courses, course, Course(degree, "EM-100", "Basics Renamed"));
        await Put(admin, Courses, course, Course(degree, "EM-100", "Basics Renamed", isActive: false));

        Assert.Equal(before, await StudentRowsAsync());
    }
}
