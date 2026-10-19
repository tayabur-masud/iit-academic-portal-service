using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// Every successful configuration change records one allowlisted business event, saved in the same transaction;
/// rejected requests record nothing, and a failing audit write rolls the change back.
/// </summary>
public sealed class ConfigurationAuditTransactionTests : ConfigurationTestBase
{
    public static TheoryData<string, string, string> Catalogs => new()
    {
        { "institutions", "institution", "Educational institution" },
        { "education-boards", "education-board", "Education board" },
        { "education-groups", "education-group", "Education group" },
        { "examination-types", "examination-type", "Examination type" },
        { "degrees", "degree", "Degree" },
        { "semesters", "semester", "Semester" },
        { "academic-sessions", "academic-session", "Academic session" },
        { "designations", "designation", "Designation" },
        { "halls", "hall", "Hall" },
    };

    private static object Body(string route, string name, bool? active = null) => route switch
    {
        "institutions" => new { name, type = "College", isActive = active },
        "degrees" => new { name, isActive = active, creditsToBeCompleted = 120m },
        _ => new { name, isActive = active },
    };

    private async Task<List<AuditEvent>> EventsAsync(string prefix) =>
        (await Factory.AuditEventsAsync()).Where(e => e.EventType.StartsWith(prefix, StringComparison.Ordinal)).ToList();

    private static Dictionary<string, JsonElement> Changes(AuditEvent e)
    {
        using var json = JsonDocument.Parse(e.ChangesJson ?? "[]");
        return json.RootElement.EnumerateArray().ToDictionary(c => c.GetProperty("fieldName").GetString()!, c => c.Clone());
    }

    [Theory]
    [MemberData(nameof(Catalogs))]
    public async Task Each_lifecycle_step_records_one_business_event_with_only_allowlisted_changes(string route, string key, string display)
    {
        using var admin = await AdminAsync();

        var id = await CreateAsync(admin, route, Body(route, "First"));
        await Put(admin, route, id, Body(route, "Second"));
        await Put(admin, route, id, Body(route, "Second", active: false));
        await Delete(admin, route, id);

        var events = await EventsAsync($"configuration.{key}.");
        Assert.Equal(
            [$"configuration.{key}.created", $"configuration.{key}.updated", $"configuration.{key}.deactivated", $"configuration.{key}.deleted"],
            events.OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.EventType).Select(e => e.EventType).OrderBy(t => Array.IndexOf(
                ["created", "updated", "deactivated", "deleted"], t.Split('.')[^1])).ToList());
        Assert.All(events, e =>
        {
            Assert.Equal(AuditEventCategory.Business, e.Category);
            Assert.Equal(AuditOutcome.Success, e.Outcome);
            Assert.NotNull(e.ActorUserId);
            Assert.Equal(display, e.EntityType);
            Assert.Equal(id.ToString(), e.EntityId);
            Assert.DoesNotContain("normalized", e.ChangesJson ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        });

        var created = Changes(events.Single(e => e.EventType.EndsWith(".created")));
        Assert.Equal("First", created["name"].GetProperty("newValue").GetString());
        var updated = Changes(events.Single(e => e.EventType.EndsWith(".updated")));
        Assert.Equal(["name"], updated.Keys);
        Assert.Equal("First", updated["name"].GetProperty("oldValue").GetString());
        Assert.Equal("Second", updated["name"].GetProperty("newValue").GetString());
        var deactivated = Changes(events.Single(e => e.EventType.EndsWith(".deactivated")));
        Assert.Equal(["isActive"], deactivated.Keys);
        Assert.True(deactivated["isActive"].GetProperty("oldValue").GetBoolean());
        Assert.False(deactivated["isActive"].GetProperty("newValue").GetBoolean());
        var deleted = Changes(events.Single(e => e.EventType.EndsWith(".deleted")));
        Assert.Equal("Second", deleted["name"].GetProperty("oldValue").GetString());
    }

    [Fact]
    public async Task Batch_and_geography_changes_are_recorded_with_their_reference_fields()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "BSc" });
        var semester = await CreateAsync(admin, "semesters", new { name = "Spring" });
        var session = await CreateAsync(admin, "academic-sessions", new { name = "2026" });
        var batch = await CreateAsync(admin, "batches", new { name = "B1", degreeId = degree, startingSemesterId = semester, academicSessionId = session });
        var division = await CreateAsync(admin, "geography/divisions", new { name = "Dhaka" });
        var district = await CreateAsync(admin, "geography/districts", new { name = "Gazipur", divisionId = division });
        await CreateAsync(admin, "geography/upazilas", new { name = "Sadar", districtId = district });

        var batchEvent = Assert.Single(await EventsAsync("configuration.batch."));
        var batchChanges = Changes(batchEvent);
        Assert.Equal(degree.ToString(), batchChanges["degreeId"].GetProperty("newValue").GetString());
        Assert.Equal(session.ToString(), batchChanges["academicSessionId"].GetProperty("newValue").GetString());
        Assert.Equal(batch.ToString(), batchEvent.EntityId);
        Assert.Equal(division.ToString(), Changes(Assert.Single(await EventsAsync("configuration.district.")))["divisionId"].GetProperty("newValue").GetString());
        Assert.Equal(district.ToString(), Changes(Assert.Single(await EventsAsync("configuration.upazila.")))["districtId"].GetProperty("newValue").GetString());
        Assert.Single(await EventsAsync("configuration.division."));
    }

    [Fact]
    public async Task The_actor_of_a_configuration_event_is_the_signed_in_admin_and_a_client_cannot_name_one()
    {
        using var admin = await AdminAsync();

        // The request model has no actor field and unknown fields are rejected, so a client cannot supply one.
        var (rejected, _) = await Post(admin, "degrees", new { name = "Spoofed", actorUserId = "someone-else" });
        Assert.Equal(HttpStatusCode.BadRequest, rejected);
        Assert.Empty(await EventsAsync("configuration.degree.created"));

        await CreateAsync(admin, "degrees", new { name = "Attributed" });

        var created = Assert.Single(await EventsAsync("configuration.degree.created"));
        Assert.Equal(AdminUserId, created.ActorUserId);
        Assert.False(string.IsNullOrWhiteSpace(created.CorrelationId));
        Assert.Equal(PortalApiFactory.ClientAddress, created.Source);
    }

    [Fact]
    public async Task Rejected_requests_record_nothing()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, "degrees", new { name = "Keep" });
        var before = (await Factory.AuditEventsAsync()).Count(e => e.EventType.StartsWith("configuration.", StringComparison.Ordinal));

        await Post(admin, "degrees", new { name = "keep" });
        await Post(admin, "degrees", new { name = "" });
        await Put(admin, "degrees", id, new { name = "" });
        await Put(admin, "degrees", Guid.NewGuid(), new { name = "x" });
        await Delete(admin, "degrees", Guid.NewGuid());
        await Post(admin, "batches", new { name = "No refs" });

        Assert.Equal(before, (await Factory.AuditEventsAsync()).Count(e => e.EventType.StartsWith("configuration.", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task An_update_that_changes_nothing_records_nothing()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, "degrees", new { name = "Same" });

        Assert.Equal(HttpStatusCode.OK, (await Put(admin, "degrees", id, new { name = "Same", creditsToBeCompleted = 120m })).Status);

        Assert.Empty(await EventsAsync("configuration.degree.updated"));
    }

    [Fact]
    public async Task A_delete_blocked_by_a_reference_records_nothing_and_changes_nothing()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "BSc" });
        var semester = await CreateAsync(admin, "semesters", new { name = "Spring" });
        var session = await CreateAsync(admin, "academic-sessions", new { name = "2026" });
        await CreateAsync(admin, "batches", new { name = "B1", degreeId = degree, startingSemesterId = semester, academicSessionId = session });

        Assert.Equal(HttpStatusCode.Conflict, (await Delete(admin, "degrees", degree)).Status);

        Assert.Empty(await EventsAsync("configuration.degree.deleted"));
        Assert.Equal(HttpStatusCode.OK, (await Get(admin, $"degrees/{degree}")).Status);
    }

    [Fact]
    public async Task When_the_audit_write_fails_a_create_an_update_and_a_delete_all_roll_back()
    {
        using var admin = await AdminAsync();
        var keep = await CreateAsync(admin, "degrees", new { name = "Original" });
        var victim = await CreateAsync(admin, "semesters", new { name = "Victim" });
        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON \"AuditEvents\" WHEN NEW.\"EventType\" LIKE 'configuration.%' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;"));

        var created = await Post(admin, "degrees", new { name = "Must not persist", creditsToBeCompleted = 120m });
        var updated = await Put(admin, "degrees", keep, new { name = "Renamed", creditsToBeCompleted = 120m });
        var deleted = await Delete(admin, "semesters", victim);

        Assert.Equal(HttpStatusCode.InternalServerError, created.Status);
        Assert.Equal(HttpStatusCode.InternalServerError, updated.Status);
        Assert.Equal(HttpStatusCode.InternalServerError, deleted.Status);
        await Factory.WithDbAsync(async db =>
        {
            Assert.Equal(["Original"], await db.Degrees.Select(d => d.Name).ToListAsync());
            Assert.Equal(["Victim"], await db.Semesters.Select(s => s.Name).ToListAsync());
        });
    }

    [Fact]
    public async Task Course_create_update_and_deactivation_record_their_fields_and_roll_back_when_the_audit_write_fails()
    {
        using var admin = await AdminAsync();
        var degree = await CreateAsync(admin, "degrees", new { name = "Audited Degree", creditsToBeCompleted = 120m });
        var course = await CreateAsync(admin, "courses", new { name = "Algorithms", code = "CSE-201", creditHours = 3m, theoryCreditHours = 2m, labCreditHours = 1m, type = "Mandatory", degreeId = degree });
        Assert.Equal(HttpStatusCode.OK, (await Put(admin, "courses", course, new { name = "Algorithms", code = "CSE-201", creditHours = 4m, type = "Elective", degreeId = degree, isActive = false })).Status);

        var created = Assert.Single(await EventsAsync("configuration.course.created"));
        var deactivated = Assert.Single(await EventsAsync("configuration.course.deactivated"));
        Assert.Equal("Course", created.EntityType);
        Assert.Contains("\"creditHours\"", created.ChangesJson);
        Assert.Contains("\"theoryCreditHours\"", created.ChangesJson);
        Assert.Contains("Mandatory", created.ChangesJson);
        Assert.Contains("Elective", deactivated.ChangesJson);

        await Factory.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "CREATE TRIGGER fail_audit BEFORE INSERT ON \"AuditEvents\" WHEN NEW.\"EventType\" LIKE 'configuration.%' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;"));

        var rejectedCreate = await Post(admin, "courses", new { name = "Must not persist", code = "CSE-999", creditHours = 3m, type = "Mandatory", degreeId = degree });
        var rejectedUpdate = await Put(admin, "courses", course, new { name = "Renamed", code = "CSE-201", creditHours = 4m, type = "Elective", degreeId = degree, isActive = true });

        Assert.Equal(HttpStatusCode.InternalServerError, rejectedCreate.Status);
        Assert.Equal(HttpStatusCode.InternalServerError, rejectedUpdate.Status);
        await Factory.WithDbAsync(async db =>
        {
            var stored = await db.Courses.SingleAsync();
            Assert.Equal("Algorithms", stored.Name);
            Assert.False(stored.IsActive);
        });
    }

    [Fact]
    public async Task Configuration_events_are_reviewable_by_an_admin_through_the_audit_api()
    {
        using var admin = await AdminAsync();
        await CreateAsync(admin, "degrees", new { name = "Reviewable" });
        await Factory.FlushAuditAsync();

        using var response = await admin.GetAsync("/api/audit-events?eventType=configuration.degree.created");
        var page = await PortalClient.ReadJsonAsync(response);

        var item = Assert.Single(page.GetProperty("items").EnumerateArray());
        Assert.Equal("business", item.GetProperty("category").GetString());
        Assert.Equal("Degree", item.GetProperty("entityType").GetString());
        Assert.Equal("Reviewable", item.GetProperty("changes")[0].GetProperty("newValue").GetString());
    }
}
