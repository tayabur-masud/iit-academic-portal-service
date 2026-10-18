using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>Batches need an active Degree, starting Semester, and Academic Session; names are unique per Degree.</summary>
public sealed class BatchTests : ConfigurationTestBase
{
    private sealed record Refs(Guid Degree, Guid Semester, Guid Session);

    private static async Task<Refs> RefsAsync(PortalClient admin, string suffix = "") => new(
        await CreateAsync(admin, "degrees", new { name = "Degree" + suffix }),
        await CreateAsync(admin, "semesters", new { name = "Semester" + suffix }),
        await CreateAsync(admin, "academic-sessions", new { name = "Session" + suffix }));

    private static object Batch(string name, Refs r, Guid? degree = null, Guid? session = null, bool? active = null) => new
    {
        name,
        degreeId = degree ?? r.Degree,
        startingSemesterId = r.Semester,
        academicSessionId = session ?? r.Session,
        isActive = active,
    };

    [Fact]
    public async Task A_batch_can_be_created_read_updated_deactivated_and_deleted()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        var (created, json) = await Post(admin, "batches", Batch("  Batch 12 ", r));
        Assert.Equal(HttpStatusCode.Created, created);
        var id = json.GetProperty("id").GetGuid();
        Assert.Equal("Batch 12", json.GetProperty("name").GetString());
        Assert.Equal(r.Degree, json.GetProperty("degreeId").GetGuid());
        Assert.Equal(r.Semester, json.GetProperty("startingSemesterId").GetGuid());
        Assert.Equal(r.Session, json.GetProperty("academicSessionId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await Get(admin, $"batches/{id}")).Status);
        Assert.Contains("Batch 12", Names((await Get(admin, "batches")).Body));
        Assert.Equal("Batch 13", (await Put(admin, "batches", id, Batch("Batch 13", r))).Body.GetProperty("name").GetString());
        Assert.False((await Put(admin, "batches", id, Batch("Batch 13", r, active: false))).Body.GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, "batches", id)).Status);
    }

    [Fact]
    public async Task Every_reference_is_required()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Post(admin, "batches", new { name = "Batch 1" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "degreeId"));
        Assert.True(HasError(problem, "startingSemesterId"));
        Assert.True(HasError(problem, "academicSessionId"));
    }

    [Fact]
    public async Task A_batch_needs_a_name()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        var (status, problem) = await Post(admin, "batches", Batch("  ", r));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "name"));
    }

    [Fact]
    public async Task A_reference_that_does_not_exist_is_rejected_and_nothing_is_stored()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        var (status, problem) = await Post(admin, "batches", Batch("Batch 1", r, degree: Guid.NewGuid(), session: Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("does not exist", FirstError(problem, "degreeId"));
        Assert.Contains("does not exist", FirstError(problem, "academicSessionId"));
        Assert.Empty((await Get(admin, "batches", "?status=all")).Body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task A_reference_to_another_kind_of_record_is_not_found_as_that_kind()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        // A Semester identifier supplied as the Degree does not identify a Degree.
        var (status, problem) = await Post(admin, "batches", Batch("Batch 1", r, degree: r.Semester));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "degreeId"));
    }

    [Fact]
    public async Task An_inactive_reference_cannot_be_chosen_for_a_new_batch()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        await Put(admin, "academic-sessions", r.Session, new { name = "Session", isActive = false });
        await Put(admin, "degrees", r.Degree, new { name = "Degree", isActive = false, creditsToBeCompleted = 120m });

        var (status, problem) = await Post(admin, "batches", Batch("Batch 1", r));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("inactive", FirstError(problem, "degreeId"));
        Assert.Contains("inactive", FirstError(problem, "academicSessionId"));
    }

    [Fact]
    public async Task A_deactivated_reference_stays_on_existing_batches_and_they_can_still_be_edited()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var batch = await CreateAsync(admin, "batches", Batch("Batch 1", r));

        await Put(admin, "academic-sessions", r.Session, new { name = "Session", isActive = false });

        var (read, json) = await Get(admin, $"batches/{batch}");
        Assert.Equal(HttpStatusCode.OK, read);
        Assert.Equal(r.Session, json.GetProperty("academicSessionId").GetGuid());
        // The unchanged, now inactive Session does not block a rename; choosing it for a different Batch would.
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, "batches", batch, Batch("Batch 1 renamed", r))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "batches", Batch("Batch 2", r))).Status);
        // Deactivating the Session did not deactivate the Batch.
        Assert.True((await Get(admin, $"batches/{batch}")).Body.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Moving_a_batch_to_an_inactive_session_is_rejected()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var other = await CreateAsync(admin, "academic-sessions", new { name = "Other session", isActive = false });
        var batch = await CreateAsync(admin, "batches", Batch("Batch 1", r));

        var (status, problem) = await Put(admin, "batches", batch, Batch("Batch 1", r, session: other));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "academicSessionId"));
    }

    [Fact]
    public async Task The_name_is_unique_within_a_degree_whatever_the_session_and_may_be_reused_by_another_degree()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var otherDegree = await CreateAsync(admin, "degrees", new { name = "Other degree" });
        var otherSession = await CreateAsync(admin, "academic-sessions", new { name = "Other session" });
        await CreateAsync(admin, "batches", Batch("Batch 1", r));

        // Same Degree: rejected even with a different Session, and after normalizing case and spacing.
        var (status, problem) = await Post(admin, "batches", Batch("  BATCH   1", r, session: otherSession));
        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("already exists", FirstError(problem, "name"));

        // A different Degree may use the same name, in the same or another Session.
        await CreateAsync(admin, "batches", Batch("Batch 1", r, degree: otherDegree));
        await CreateAsync(admin, "batches", Batch("Batch 2", r, degree: otherDegree, session: otherSession));
        // Within that other Degree the name is now taken too.
        Assert.Equal(HttpStatusCode.BadRequest, (await Post(admin, "batches", Batch("batch 2", r, degree: otherDegree))).Status);
    }

    [Fact]
    public async Task Many_batches_may_share_one_session()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var second = await CreateAsync(admin, "degrees", new { name = "Second degree" });

        await CreateAsync(admin, "batches", Batch("Batch A", r));
        await CreateAsync(admin, "batches", Batch("Batch B", r));
        await CreateAsync(admin, "batches", Batch("Batch A", r, degree: second));

        Assert.Equal(3, (await Get(admin, "batches")).Body.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Moving_a_batch_to_another_degree_checks_the_name_there()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var other = await CreateAsync(admin, "degrees", new { name = "Other degree" });
        await CreateAsync(admin, "batches", Batch("Batch 1", r, degree: other));
        var mine = await CreateAsync(admin, "batches", Batch("Batch 1", r));

        var (status, _) = await Put(admin, "batches", mine, Batch("Batch 1", r, degree: other));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Referenced_degrees_semesters_and_sessions_cannot_be_deleted_but_unreferenced_ones_can()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var spare = await RefsAsync(admin, " spare");
        var batch = await CreateAsync(admin, "batches", Batch("Batch 1", r));

        foreach (var (route, id) in new[] { ("degrees", r.Degree), ("semesters", r.Semester), ("academic-sessions", r.Session) })
        {
            var (status, problem) = await Delete(admin, route, id);
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.Contains("Deactivate", problem.GetProperty("detail").GetString());
            Assert.Equal(HttpStatusCode.OK, (await Get(admin, $"{route}/{id}")).Status);
        }

        foreach (var (route, id) in new[] { ("degrees", spare.Degree), ("semesters", spare.Semester), ("academic-sessions", spare.Session) })
        {
            Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, route, id)).Status);
        }

        // Once the Batch is gone, its references are free to delete.
        await Delete(admin, "batches", batch);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, "degrees", r.Degree)).Status);
    }

    [Fact]
    public async Task Fields_a_batch_does_not_use_are_rejected()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        var (status, problem) = await Post(admin, "batches", new
        {
            name = "Batch 1", degreeId = r.Degree, startingSemesterId = r.Semester, academicSessionId = r.Session,
            code = "B1", divisionId = Guid.NewGuid(),
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "code"));
        Assert.True(HasError(problem, "divisionId"));
    }

    [Fact]
    public async Task Creating_a_batch_assigns_no_students_and_advances_no_semester()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);

        var id = await CreateAsync(admin, "batches", Batch("Batch 1", r));

        // The Semester is only the starting point; it is unchanged and nothing else was created.
        Assert.Equal("Semester", (await Get(admin, $"semesters/{r.Semester}")).Body.GetProperty("name").GetString());
        Assert.Single((await Get(admin, "batches", "?status=all")).Body.GetProperty("items").EnumerateArray());
        Assert.NotEqual(Guid.Empty, id);
    }

    [Fact]
    public async Task A_deleted_batchs_name_can_be_used_again_in_its_degree_and_it_frees_its_references_to_delete()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var batch = await CreateAsync(admin, "batches", Batch("Batch 1", r));
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, "degrees", r.Degree)).Status);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, "batches", batch)).Status);

        await CreateAsync(admin, "batches", Batch("Batch 1", r));
        // The new live batch still blocks deleting the degree; the deleted one no longer does.
        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, "degrees", r.Degree)).Status);
    }

    [Fact]
    public async Task A_deleted_degree_semester_or_session_cannot_be_chosen_for_a_batch()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        await Delete(admin, "degrees", r.Degree);
        await Delete(admin, "semesters", r.Semester);
        await Delete(admin, "academic-sessions", r.Session);

        var (status, problem) = await Post(admin, "batches", Batch("Batch 1", r));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("does not exist", FirstError(problem, "degreeId"));
        Assert.Contains("does not exist", FirstError(problem, "startingSemesterId"));
        Assert.Contains("does not exist", FirstError(problem, "academicSessionId"));
    }

    [Fact]
    public async Task A_deactivated_dependent_still_blocks_the_delete_but_a_deleted_one_does_not()
    {
        using var admin = await AdminAsync();
        var r = await RefsAsync(admin);
        var batch = await CreateAsync(admin, "batches", Batch("Batch 1", r));
        await Put(admin, "batches", batch, Batch("Batch 1", r, active: false));

        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, "academic-sessions", r.Session)).Status);

        await Delete(admin, "batches", batch);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(admin, "academic-sessions", r.Session)).Status);
    }
}
