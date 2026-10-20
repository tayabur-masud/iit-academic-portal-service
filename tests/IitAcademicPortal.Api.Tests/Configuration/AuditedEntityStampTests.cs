using System.Net;
using System.Text.Json;
using IitAcademicPortal.Domain.Configuration;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Configuration;

/// <summary>
/// Every entity records who created it and when, and who last changed it and when. The values come from the server clock and the
/// signed-in account, are never accepted from a client, and are stamped by the database context so a feature cannot forget them.
/// </summary>
public sealed class AuditedEntityStampTests : ConfigurationTestBase
{
    private static DateTime Time(JsonElement item, string property) =>
        item.GetProperty(property).GetDateTime().ToUniversalTime();

    private static string? Text(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) ? value.GetString() : null;

    [Fact]
    public async Task Creating_records_who_and_when_and_starts_the_last_modified_pair_equal_to_it()
    {
        using var admin = await AdminAsync();

        var (status, json) = await Post(admin, "degrees", new { name = "Stamped", creditsToBeCompleted = 120m, minimumCgpaForCompletion = 2.50m });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(AdminUserId, Text(json, "createdByUserId"));
        Assert.Equal(AdminEmail, Text(json, "createdBy"));
        Assert.Equal(AdminUserId, Text(json, "lastModifiedByUserId"));
        Assert.Equal(AdminEmail, Text(json, "lastModifiedBy"));
        Assert.Equal(Time(json, "createdAtUtc"), Time(json, "lastModifiedAtUtc"));
        Assert.InRange(Time(json, "createdAtUtc"), DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
    }

    [Fact]
    public async Task The_stamps_use_the_server_clock()
    {
        using var admin = await AdminAsync();
        Factory.Clock.Advance(TimeSpan.FromHours(2));

        var (_, json) = await Post(admin, "degrees", new { name = "Future clock", creditsToBeCompleted = 120m, minimumCgpaForCompletion = 2.50m });

        Assert.InRange(Time(json, "createdAtUtc"), DateTime.UtcNow.AddHours(2).AddMinutes(-2), DateTime.UtcNow.AddHours(2).AddMinutes(1));
    }

    [Fact]
    public async Task An_update_moves_only_the_last_modified_pair_to_the_account_that_made_it()
    {
        using var first = await AdminAsync();
        var (firstId, firstEmail) = (AdminUserId, AdminEmail);
        var id = await CreateAsync(first, "semesters", new { name = "Original" });
        var created = (await Get(first, $"semesters/{id}")).Body;
        Factory.Clock.Advance(TimeSpan.FromMinutes(10));
        using var second = await AdminAsync();
        var (secondId, secondEmail) = (AdminUserId, AdminEmail);

        var (status, updated) = await Put(second, "semesters", id, new { name = "Renamed" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(firstId, Text(updated, "createdByUserId"));
        Assert.Equal(firstEmail, Text(updated, "createdBy"));
        Assert.Equal(Time(created, "createdAtUtc"), Time(updated, "createdAtUtc"));
        Assert.Equal(secondId, Text(updated, "lastModifiedByUserId"));
        Assert.Equal(secondEmail, Text(updated, "lastModifiedBy"));
        Assert.True(Time(updated, "lastModifiedAtUtc") - Time(updated, "createdAtUtc") >= TimeSpan.FromMinutes(9));
        // What is stored agrees with what was returned.
        var reread = (await Get(second, $"semesters/{id}")).Body;
        Assert.Equal(Text(updated, "lastModifiedBy"), Text(reread, "lastModifiedBy"));
        Assert.Equal(Time(updated, "lastModifiedAtUtc"), Time(reread, "lastModifiedAtUtc"));
    }

    [Fact]
    public async Task Deactivating_and_deleting_also_move_the_last_modified_pair()
    {
        using var creator = await AdminAsync();
        var creatorId = AdminUserId;
        var id = await CreateAsync(creator, "halls", new { name = "Moving Hall" });
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var deactivator = await AdminAsync();
        var deactivatorId = AdminUserId;
        var (_, off) = await Put(deactivator, "halls", id, new { name = "Moving Hall", isActive = false });
        Assert.Equal(deactivatorId, Text(off, "lastModifiedByUserId"));
        Factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var deleter = await AdminAsync();
        var deleterId = AdminUserId;

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(deleter, "halls", id)).Status);

        await Factory.WithDbAsync(async db =>
        {
            var row = await db.Halls.IgnoreQueryFilters().SingleAsync(h => h.Id == id);
            Assert.Equal(deleterId, row.LastModifiedByUserId);
            Assert.Equal(deleterId, row.DeletedByUserId);
            Assert.Equal(row.DeletedAtUtc!.Value, row.LastModifiedAtUtc, TimeSpan.FromSeconds(2));
            Assert.True(row.LastModifiedAtUtc - row.CreatedAtUtc >= TimeSpan.FromMinutes(9));
            Assert.Equal(creatorId, row.CreatedByUserId);
        });
    }

    [Fact]
    public async Task An_update_that_changes_nothing_leaves_the_stamps_alone()
    {
        using var admin = await AdminAsync();
        var id = await CreateAsync(admin, "degrees", new { name = "Quiet" });
        var before = (await Get(admin, $"degrees/{id}")).Body;
        Factory.Clock.Advance(TimeSpan.FromMinutes(30));

        var (_, after) = await Put(admin, "degrees", id, new { name = "Quiet", creditsToBeCompleted = 120m, minimumCgpaForCompletion = 2.50m });

        Assert.Equal(Time(before, "lastModifiedAtUtc"), Time(after, "lastModifiedAtUtc"));
        Assert.Equal(Text(before, "lastModifiedByUserId"), Text(after, "lastModifiedByUserId"));
    }

    [Fact]
    public async Task A_client_cannot_supply_any_of_the_stamps()
    {
        using var admin = await AdminAsync();

        foreach (var field in new[] { "createdByUserId", "createdAtUtc", "lastModifiedByUserId", "lastModifiedAtUtc" })
        {
            var body = new Dictionary<string, object> { ["name"] = "Spoof " + field, [field] = field.EndsWith("Utc") ? "2000-01-01T00:00:00Z" : "someone-else" };
            var (status, _) = await Post(admin, "degrees", body);
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }

        Assert.Empty(Names((await Get(admin, "degrees", "?status=all")).Body));
    }

    [Fact]
    public async Task A_list_names_the_accounts_with_one_lookup_and_a_removed_account_keeps_only_its_id()
    {
        using var creator = await AdminAsync();
        var (creatorId, creatorEmail) = (AdminUserId, AdminEmail);
        foreach (var name in new[] { "One", "Two", "Three" })
        {
            await CreateAsync(creator, "semesters", new { name });
        }

        using var viewer = await AdminAsync();
        var (_, named) = await Get(viewer, "semesters");
        Assert.All(named.GetProperty("items").EnumerateArray(), i => Assert.Equal(creatorEmail, Text(i, "createdBy")));

        await Factory.WithDbAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == creatorId));
            await db.SaveChangesAsync();
        });
        var (_, removed) = await Get(viewer, "semesters");

        Assert.All(removed.GetProperty("items").EnumerateArray(), i =>
        {
            Assert.Equal(creatorId, Text(i, "createdByUserId"));
            Assert.Null(Text(i, "createdBy"));
        });
    }

    [Fact]
    public async Task A_change_made_outside_a_request_has_the_time_but_no_account()
    {
        var id = Guid.NewGuid();
        await Factory.WithDbAsync(async db =>
        {
            var hall = new Hall { Id = id };
            hall.Rename("Background Hall");
            db.Halls.Add(hall);
            await db.SaveChangesAsync();
        });

        await Factory.WithDbAsync(async db =>
        {
            var row = await db.Halls.SingleAsync(h => h.Id == id);
            Assert.Null(row.CreatedByUserId);
            Assert.Null(row.LastModifiedByUserId);
            Assert.InRange(row.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddMinutes(1));
            Assert.Equal(DateTimeKind.Utc, row.CreatedAtUtc.Kind);
        });
    }
}
