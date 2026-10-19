using System.Net;
using System.Text.Json;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Students;

/// <summary>
/// Academic history takes its school or college, university, institution, board, and group from the Configuration catalogs, keeps the
/// catalog name as a snapshot that later renames never rewrite, still accepts a typed name when the paper form's value is not listed,
/// and checks the institution type for each field.
/// </summary>
public sealed class StudentAcademicHistoryCatalogTests : StudentTestBase
{
    private sealed record Catalog(Guid School, Guid College, Guid University, Guid ClosedUniversity, Guid Board, Guid Group);

    private async Task<Catalog> CatalogAsync()
    {
        T Named<T>(T record, string name, bool active = true) where T : ConfigurationRecord
        {
            record.Rename(name);
            record.IsActive = active;
            return record;
        }

        var school = Named(new EducationalInstitution { Type = EducationalInstitutionType.School }, "Motijheel Model High School");
        var college = Named(new EducationalInstitution { Type = EducationalInstitutionType.College }, "Notre Dame College");
        var university = Named(new EducationalInstitution { Type = EducationalInstitutionType.University }, "University of Dhaka");
        var closed = Named(new EducationalInstitution { Type = EducationalInstitutionType.University }, "Closed University", active: false);
        var board = Named(new EducationBoard(), "Dhaka");
        var group = Named(new EducationGroup(), "Science");
        await Host.WithDbAsync(async db =>
        {
            db.AddRange(school, college, university, closed, board, group);
            await db.SaveChangesAsync();
        });
        return new Catalog(school.Id, college.Id, university.Id, closed.Id, board.Id, group.Id);
    }

    private static List<JsonElement> History(JsonElement admission) =>
        admission.GetProperty("profile").GetProperty("academicHistory").EnumerateArray().ToList();

    [Fact]
    public async Task Catalog_choices_are_stored_by_id_with_the_catalog_name_and_typed_names_are_kept_when_not_listed()
    {
        var refs = await RefsAsync();
        var catalog = await CatalogAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("catalog"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[]
        {
            new { category = "SscOLevel", schoolOrCollegeId = catalog.School, schoolOrCollege = "ignored text", boardId = catalog.Board, groupId = catalog.Group, year = "2013" },
            new { category = "BachelorGraduation", universityId = catalog.University, subject = "CSE" },
            new { category = "HscALevel", schoolOrCollege = "Unlisted College, Sylhet", board = "Edexcel", group = "Science (A)" },
        };

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        var history = History(body.GetProperty("admission"));
        Assert.Equal(catalog.School, history[0].GetProperty("schoolOrCollegeId").GetGuid());
        Assert.Equal("Motijheel Model High School", history[0].GetProperty("schoolOrCollege").GetString());
        Assert.Equal(catalog.Board, history[0].GetProperty("boardId").GetGuid());
        Assert.Equal("Dhaka", history[0].GetProperty("board").GetString());
        Assert.Equal("Science", history[0].GetProperty("group").GetString());
        Assert.Equal("University of Dhaka", history[1].GetProperty("university").GetString());
        Assert.Equal(JsonValueKind.Null, history[2].GetProperty("schoolOrCollegeId").ValueKind);
        Assert.Equal("Unlisted College, Sylhet", history[2].GetProperty("schoolOrCollege").GetString());
        Assert.Equal("Edexcel", history[2].GetProperty("board").GetString());
    }

    [Fact]
    public async Task Renaming_or_deactivating_a_catalog_record_later_does_not_change_saved_history()
    {
        var refs = await RefsAsync();
        var catalog = await CatalogAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("snapshot"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[] { new { category = "BachelorGraduation", universityId = catalog.University } };
        var admission = await AdmitAsync(admin, form);
        await Host.WithDbAsync(async db =>
        {
            var university = await db.EducationalInstitutions.SingleAsync(i => i.Id == catalog.University);
            university.Rename("Dhaka University (renamed)");
            university.IsActive = false;
            await db.SaveChangesAsync();
        });

        var (_, current) = await GetAdmission(admin, admission.Id);
        var update = FormOf(current);
        update["classRoll"] = "11";
        var (status, updated) = await PutAdmission(admin, admission.Id, update);

        Assert.Equal("University of Dhaka", History(current)[0].GetProperty("university").GetString());
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("University of Dhaka", History(updated)[0].GetProperty("university").GetString());
        Assert.Equal(catalog.University, History(updated)[0].GetProperty("universityId").GetGuid());
    }

    [Fact]
    public async Task A_choice_must_exist_be_active_and_have_the_right_institution_type()
    {
        var refs = await RefsAsync();
        var catalog = await CatalogAsync();
        using var admin = await AdminAsync();

        async Task<JsonElement> Attempt(object entry)
        {
            var form = Form(Unique("bad"), refs.Pgdit, refs.PgditBatch);
            form["academicHistory"] = new[] { entry };
            var (status, problem) = await CreateAdmission(admin, form);
            Assert.Equal(HttpStatusCode.BadRequest, status);
            return problem;
        }

        Assert.True(HasError(await Attempt(new { category = "SscOLevel", schoolOrCollegeId = catalog.University }), "academicHistory[0].schoolOrCollegeId"));
        Assert.True(HasError(await Attempt(new { category = "Master", universityId = catalog.College }), "academicHistory[0].universityId"));
        Assert.True(HasError(await Attempt(new { category = "Master", universityId = catalog.ClosedUniversity }), "academicHistory[0].universityId"));
        Assert.True(HasError(await Attempt(new { category = "Other", institutionId = Guid.NewGuid() }), "academicHistory[0].institutionId"));
        Assert.True(HasError(await Attempt(new { category = "SscOLevel", boardId = catalog.Group }), "academicHistory[0].boardId"));
        Assert.Equal(0, (await CountsAsync()).Profiles);
    }

    [Fact]
    public async Task Any_institution_type_may_be_the_institution_of_a_PGDIT_or_Other_entry()
    {
        var refs = await RefsAsync();
        var catalog = await CatalogAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("any"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[] { new { category = "Other", institutionId = catalog.College } };

        var (status, body) = await CreateAdmission(admin, form);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Notre Dame College", History(body.GetProperty("admission"))[0].GetProperty("institution").GetString());
    }

    [Theory]
    [InlineData("institutions")]
    [InlineData("education-boards")]
    [InlineData("education-groups")]
    public async Task A_catalog_record_used_by_academic_history_cannot_be_deleted(string route)
    {
        var refs = await RefsAsync();
        var catalog = await CatalogAsync();
        using var admin = await AdminAsync();
        var form = Form(Unique("used"), refs.Pgdit, refs.PgditBatch);
        form["academicHistory"] = new object[] { new { category = "SscOLevel", schoolOrCollegeId = catalog.School, boardId = catalog.Board, groupId = catalog.Group } };
        await AdmitAsync(admin, form);
        var id = route switch { "institutions" => catalog.School, "education-boards" => catalog.Board, _ => catalog.Group };

        var (status, problem) = await SendAsync(admin, HttpMethod.Delete, $"/api/admin/configuration/{route}/{id}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("Deactivate it instead", problem.GetProperty("detail").GetString());
    }
}
