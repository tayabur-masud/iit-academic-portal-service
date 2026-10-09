using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Identity;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Filters, UTC boundaries, validation, keyset paging, actor display, and read recording.</summary>
public sealed class AuditEventSearchTests : IDisposable
{
    private static readonly DateTime T0 = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly PortalApiFactory factory = new();

    public void Dispose() => factory.Dispose();

    private static AuditEvent Make(
        DateTime at, string type = "test.search", AuditOutcome outcome = AuditOutcome.Success, AuditEventCategory category = AuditEventCategory.Security,
        string? actor = null, string? entityType = null, string? entityId = null, string? correlation = null, string? metadata = null, string? changes = null) =>
        new(Guid.NewGuid(), at, category, type, outcome, actor, entityType, entityId, correlation, null, metadata, changes);

    private async Task SeedAsync(params AuditEvent[] events) => await factory.WithDbAsync(async db =>
    {
        db.AuditEvents.AddRange(events);
        await db.SaveChangesAsync();
    });

    private async Task<PortalClient> AdminAsync()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await factory.CreateUserAsync(email, PortalRoles.Admin);
        var client = factory.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    private static async Task<(List<JsonElement> Items, string? Next)> PageAsync(PortalClient client, string query)
    {
        var response = await client.GetAsync("/api/audit-events" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await PortalClient.ReadJsonAsync(response);
        var next = json.TryGetProperty("nextCursor", out var cursor) && cursor.ValueKind == JsonValueKind.String ? cursor.GetString() : null;
        return (json.GetProperty("items").EnumerateArray().ToList(), next);
    }

    private static List<string> Ids(IEnumerable<JsonElement> items) => items.Select(i => i.GetProperty("eventId").GetString()!).ToList();

    [Fact]
    public async Task Each_filter_returns_only_matching_events_and_filters_combine_with_and()
    {
        var wanted = Make(T0, "a.one", AuditOutcome.Denied, AuditEventCategory.Security, "actor-x", "Course", "c-1", "corr-1");
        await SeedAsync(
            wanted,
            Make(T0, "a.one", AuditOutcome.Success, AuditEventCategory.Security, "actor-x", "Course", "c-1", "corr-1"),
            Make(T0, "a.two", AuditOutcome.Denied, AuditEventCategory.Business, "actor-y", "Course", "c-2", "corr-2"));
        using var client = await AdminAsync();

        Assert.Equal(2, (await PageAsync(client, "?actorUserId=actor-x")).Items.Count);
        Assert.Single((await PageAsync(client, "?category=business")).Items);
        Assert.Equal(2, (await PageAsync(client, "?eventType=a.one")).Items.Count);
        Assert.Equal(2, (await PageAsync(client, "?outcome=denied")).Items.Count);
        Assert.Equal(2, (await PageAsync(client, "?entityType=Course&entityId=c-1")).Items.Count);
        Assert.Single((await PageAsync(client, "?correlationId=corr-2")).Items);

        var combined = await PageAsync(client, "?actorUserId=actor-x&eventType=a.one&outcome=denied&category=security&entityType=Course&entityId=c-1&correlationId=corr-1");
        Assert.Equal(wanted.Id.ToString(), Assert.Single(combined.Items).GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task The_start_time_is_inclusive_and_the_end_time_is_exclusive_in_utc()
    {
        var first = Make(T0);
        var second = Make(T0.AddSeconds(1));
        var third = Make(T0.AddSeconds(2));
        await SeedAsync(first, second, third);
        using var client = await AdminAsync();

        var page = await PageAsync(client, $"?fromUtc={T0.AddSeconds(1):O}&toUtc={T0.AddSeconds(2):O}");

        Assert.Equal(second.Id.ToString(), Assert.Single(page.Items).GetProperty("eventId").GetString());
    }

    [Fact]
    public async Task A_time_without_an_offset_is_read_as_utc_and_an_offset_is_converted()
    {
        var at = Make(T0);
        await SeedAsync(at);
        using var client = await AdminAsync();

        Assert.Single((await PageAsync(client, "?fromUtc=2026-10-01T12:00:00&toUtc=2026-10-01T12:00:01")).Items);
        Assert.Single((await PageAsync(client, "?fromUtc=2026-10-01T18:00:00%2B06:00&toUtc=2026-10-01T18:00:01%2B06:00")).Items);
        Assert.Empty((await PageAsync(client, "?fromUtc=2026-10-01T12:00:00%2B06:00&toUtc=2026-10-01T12:00:01%2B06:00")).Items);
    }

    [Theory]
    [InlineData("?fromUtc=2026-10-02T00:00:00Z&toUtc=2026-10-01T00:00:00Z", "toUtc")]
    [InlineData("?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-10-01T00:00:00Z", "toUtc")]
    [InlineData("?fromUtc=not-a-date", "fromUtc")]
    [InlineData("?category=weird", "category")]
    [InlineData("?outcome=7", "outcome")]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?cursor=garbage", "cursor")]
    public async Task Invalid_filters_are_rejected_with_a_safe_validation_response_and_no_partial_results(string query, string field)
    {
        await SeedAsync(Make(T0, entityId: "must-not-leak-77"));
        using var client = await AdminAsync();

        var response = await client.GetAsync("/api/audit-events" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("must-not-leak-77", body);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty(field, out _), body);
        Assert.False(json.RootElement.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task Results_are_newest_first_with_a_default_page_of_25_and_a_cursor_for_the_rest()
    {
        await SeedAsync(Enumerable.Range(0, 30).Select(i => Make(T0.AddMinutes(i), entityId: i.ToString())).ToArray());
        using var client = await AdminAsync();

        var first = await PageAsync(client, "?eventType=test.search");

        Assert.Equal(25, first.Items.Count);
        Assert.NotNull(first.Next);
        var times = first.Items.Select(i => i.GetProperty("occurredAtUtc").GetDateTime()).ToList();
        Assert.Equal(times.OrderByDescending(t => t), times);
        Assert.Equal("29", first.Items[0].GetProperty("entityId").GetString());

        var rest = await PageAsync(client, $"?eventType=test.search&cursor={first.Next}");
        Assert.Equal(5, rest.Items.Count);
        Assert.Null(rest.Next);
    }

    [Fact]
    public async Task Paging_with_identical_timestamps_returns_every_event_exactly_once_in_a_stable_order()
    {
        var instant = T0;
        var events = Enumerable.Range(0, 10).Select(_ => Make(instant, "test.same")).ToArray();
        await SeedAsync(events);
        using var client = await AdminAsync();

        async Task<List<string>> TraverseAsync()
        {
            var seen = new List<string>();
            string? cursor = null;
            do
            {
                var page = await PageAsync(client, $"?eventType=test.same&pageSize=3{(cursor is null ? "" : "&cursor=" + cursor)}");
                seen.AddRange(Ids(page.Items));
                cursor = page.Next;
            }
            while (cursor is not null);

            return seen;
        }

        var firstPass = await TraverseAsync();
        var secondPass = await TraverseAsync();

        Assert.Equal(10, firstPass.Count);
        Assert.Equal(10, firstPass.Distinct().Count());
        Assert.True(events.Select(e => e.Id.ToString()).ToHashSet().SetEquals(firstPass));
        Assert.Equal(firstPass, secondPass);
    }

    [Fact]
    public async Task Events_inserted_between_pages_cause_no_duplicates_or_omissions()
    {
        await SeedAsync(Enumerable.Range(0, 12).Select(i => Make(T0.AddMinutes(i), "test.race")).ToArray());
        using var client = await AdminAsync();

        var first = await PageAsync(client, "?eventType=test.race&pageSize=5");
        await SeedAsync(Enumerable.Range(0, 3).Select(i => Make(T0.AddHours(5).AddMinutes(i), "test.race")).ToArray());
        var second = await PageAsync(client, $"?eventType=test.race&pageSize=5&cursor={first.Next}");
        var third = await PageAsync(client, $"?eventType=test.race&pageSize=5&cursor={second.Next}");

        var all = Ids(first.Items).Concat(Ids(second.Items)).Concat(Ids(third.Items)).ToList();
        Assert.Equal(12, all.Count);
        Assert.Equal(12, all.Distinct().Count());
    }

    [Fact]
    public async Task The_actor_display_is_the_current_email_and_absent_for_removed_or_missing_actors()
    {
        var email = PortalApiFactory.UniqueEmail("actor");
        var userId = await factory.CreateUserAsync(email, PortalRoles.Student);
        await SeedAsync(
            Make(T0.AddSeconds(3), "test.actors", actor: userId),
            Make(T0.AddSeconds(2), "test.actors", actor: "a-removed-account-id"),
            Make(T0.AddSeconds(1), "test.actors", actor: null));
        using var client = await AdminAsync();

        var items = (await PageAsync(client, "?eventType=test.actors")).Items;

        Assert.Equal(email, items[0].GetProperty("actorDisplay").GetString());
        Assert.Equal("a-removed-account-id", items[1].GetProperty("actorUserId").GetString());
        Assert.Equal(JsonValueKind.Null, items[1].GetProperty("actorDisplay").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("actorUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].GetProperty("actorDisplay").ValueKind);
    }

    [Fact]
    public async Task The_detail_shows_metadata_and_the_change_summary_and_a_missing_event_is_404()
    {
        var withChanges = Make(
            T0, "test.detail", category: AuditEventCategory.Business, metadata: "{\"note\":\"hello\"}",
            changes: "[{\"fieldName\":\"status\",\"oldValue\":\"draft\",\"newValue\":\"final\"},{\"fieldName\":\"salary\",\"changed\":true}]");
        await SeedAsync(withChanges);
        using var client = await AdminAsync();

        var detail = await PortalClient.ReadJsonAsync(await client.GetAsync($"/api/audit-events/{withChanges.Id}"));

        Assert.Equal("business", detail.GetProperty("category").GetString());
        Assert.Equal("hello", detail.GetProperty("metadata").GetProperty("note").GetString());
        var changes = detail.GetProperty("changes").EnumerateArray().ToList();
        Assert.Equal("final", changes[0].GetProperty("newValue").GetString());
        Assert.True(changes[1].GetProperty("changed").GetBoolean());
        Assert.False(changes[1].TryGetProperty("newValue", out _));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/audit-events/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Each_search_and_detail_view_is_recorded_without_copying_the_returned_contents()
    {
        var shown = Make(T0, "test.reads", entityId: "contents-marker-5150", metadata: "{\"secretish\":\"contents-marker-5150\"}");
        await SeedAsync(shown);
        using var client = await AdminAsync();
        var adminId = (await factory.AuditEventsAsync("auth.sign-in")).Single().ActorUserId;

        await client.GetAsync("/api/audit-events?eventType=test.reads&outcome=success");
        await client.GetAsync($"/api/audit-events/{shown.Id}");
        await client.GetAsync($"/api/audit-events/{Guid.NewGuid()}");

        var reads = await factory.AuditEventsAsync("audit.review.accessed");
        Assert.Equal(2, reads.Count);
        Assert.All(reads, r =>
        {
            Assert.Equal(adminId, r.ActorUserId);
            Assert.Equal(AuditOutcome.Success, r.Outcome);
            Assert.DoesNotContain("contents-marker-5150", r.MetadataJson + r.EntityId);
        });
        var search = Assert.Single(reads, r => r.EntityId is null);
        using var searchMetadata = JsonDocument.Parse(search.MetadataJson!);
        Assert.Equal("search", searchMetadata.RootElement.GetProperty("kind").GetString());
        Assert.Equal("test.reads", searchMetadata.RootElement.GetProperty("filters").GetProperty("eventType").GetString());
        var detail = Assert.Single(reads, r => r.EntityId is not null);
        Assert.Equal(shown.Id.ToString(), detail.EntityId);
        Assert.Equal("AuditEvent", detail.EntityType);
    }

    [Fact]
    public async Task A_rejected_search_is_not_recorded_as_a_review_access()
    {
        using var client = await AdminAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/audit-events?pageSize=0")).StatusCode);

        Assert.Empty(await factory.AuditEventsAsync("audit.review.accessed"));
    }

    [Fact]
    public async Task A_review_read_still_succeeds_when_recording_it_fails()
    {
        await SeedAsync(Make(T0, "test.resilient"));
        using var client = await AdminAsync();
        factory.Outbox.FailEnqueue = true;
        factory.FallbackSink.Fail = true;

        var page = await PageAsync(client, "?eventType=test.resilient");

        Assert.Single(page.Items);
    }
}
