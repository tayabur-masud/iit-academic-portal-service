using System.Text.Json;
using IitAcademicPortal.Application.Auditing;

namespace IitAcademicPortal.Api.Tests.Auditing;

/// <summary>Only fields the owning feature approved keep their values; everything else is omitted.</summary>
public sealed class AuditChangeSummaryTests
{
    private static readonly AuditEventDefinition Definition =
        AuditEventDefinition.Business("course.updated", "title", "credits", "schedule", "passwordHash")
            .WithChangeIndicators("internalNote");

    private static Dictionary<string, JsonElement> Build(params AuditFieldChange[] changes)
    {
        var json = AuditChangeSummaryBuilder.Build(Definition, changes);
        Assert.NotNull(json);
        return JsonDocument.Parse(json).RootElement.EnumerateArray()
            .ToDictionary(e => e.GetProperty("fieldName").GetString()!, e => e.Clone());
    }

    [Fact]
    public void Approved_fields_keep_their_previous_and_new_values()
    {
        var changes = Build(new AuditFieldChange("title", "Algorithms", "Advanced Algorithms"), new AuditFieldChange("credits", 3, 4));

        Assert.Equal("Algorithms", changes["title"].GetProperty("oldValue").GetString());
        Assert.Equal("Advanced Algorithms", changes["title"].GetProperty("newValue").GetString());
        Assert.Equal(3, changes["credits"].GetProperty("oldValue").GetInt32());
        Assert.Equal(4, changes["credits"].GetProperty("newValue").GetInt32());
    }

    [Fact]
    public void Unchanged_values_are_skipped_and_a_change_to_or_from_nothing_is_kept()
    {
        var changes = Build(
            new AuditFieldChange("title", "Same", "Same"),
            new AuditFieldChange("credits", null, 4),
            new AuditFieldChange("schedule", "Mon", null));

        Assert.False(changes.ContainsKey("title"));
        Assert.Equal(JsonValueKind.Null, changes["credits"].GetProperty("oldValue").ValueKind);
        Assert.Equal(JsonValueKind.Null, changes["schedule"].GetProperty("newValue").ValueKind);
    }

    [Fact]
    public void Structured_values_are_preserved_as_structured_json()
    {
        var changes = Build(new AuditFieldChange(
            "schedule",
            new { days = new[] { "Mon", "Wed" }, room = "R1" },
            new { days = new[] { "Tue" }, room = "R2" }));

        Assert.Equal("R1", changes["schedule"].GetProperty("oldValue").GetProperty("room").GetString());
        Assert.Equal("Tue", changes["schedule"].GetProperty("newValue").GetProperty("days")[0].GetString());
    }

    [Fact]
    public void Fields_the_owner_did_not_approve_are_omitted_entirely()
    {
        var changes = Build(new AuditFieldChange("title", "a", "b"), new AuditFieldChange("salary", 1000, 2000));

        Assert.False(changes.ContainsKey("salary"));
        Assert.Single(changes);
    }

    [Fact]
    public void An_indicator_field_records_that_it_changed_but_never_its_values()
    {
        var changes = Build(new AuditFieldChange("internalNote", "confidential before", "confidential after"));

        Assert.True(changes["internalNote"].GetProperty("changed").GetBoolean());
        Assert.False(changes["internalNote"].TryGetProperty("oldValue", out _));
        Assert.False(changes["internalNote"].TryGetProperty("newValue", out _));
    }

    [Fact]
    public void A_credential_like_field_name_never_keeps_values_even_when_approved()
    {
        var changes = Build(new AuditFieldChange("passwordHash", "old-hash-value", "new-hash-value"));

        Assert.True(changes["passwordHash"].GetProperty("changed").GetBoolean());
        Assert.False(changes["passwordHash"].TryGetProperty("newValue", out _));
    }

    [Fact]
    public void A_credential_like_value_in_an_approved_field_is_masked()
    {
        var changes = Build(new AuditFieldChange("title", "ordinary", "AbCdEfGhIjKlMnOpQrStUvWxYz0123456789_-AbCdE"));

        Assert.Equal(AuditSecretGuard.Redacted, changes["title"].GetProperty("newValue").GetString());
    }

    [Fact]
    public void No_auditable_change_gives_no_summary_rather_than_an_invented_one()
    {
        Assert.Null(AuditChangeSummaryBuilder.Build(Definition, null));
        Assert.Null(AuditChangeSummaryBuilder.Build(Definition, []));
        Assert.Null(AuditChangeSummaryBuilder.Build(Definition, [new AuditFieldChange("title", "x", "x")]));
        Assert.Null(AuditChangeSummaryBuilder.Build(Definition, [new AuditFieldChange("salary", 1, 2)]));
    }
}
