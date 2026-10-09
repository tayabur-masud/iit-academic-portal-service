using System.Text.Json;
using System.Text.Json.Serialization;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Api.Contracts;

/// <summary>One audit change: only allowlisted values, or a safe "changed" indicator.</summary>
public sealed record AuditChangeResponse(
    string FieldName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? OldValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? NewValue,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Changed);

/// <summary>A formal audit event as shown to an Admin reviewer (contracts/audit.openapi.json).</summary>
public sealed record AuditEventResponse(
    Guid EventId,
    DateTime OccurredAtUtc,
    string Category,
    string EventType,
    string Outcome,
    string? ActorUserId,
    string? ActorDisplay,
    string? EntityType,
    string? EntityId,
    string? CorrelationId,
    string? Source,
    JsonElement Metadata,
    IReadOnlyList<AuditChangeResponse> Changes)
{
    public static AuditEventResponse From(AuditEventView view)
    {
        var e = view.Event;
        return new AuditEventResponse(
            e.Id,
            e.OccurredAtUtc,
            e.Category.ToString().ToLowerInvariant(),
            e.EventType,
            e.Outcome.ToString().ToLowerInvariant(),
            e.ActorUserId,
            view.ActorDisplay,
            e.EntityType,
            e.EntityId,
            e.CorrelationId,
            e.Source,
            ParseMetadata(e.MetadataJson),
            ParseChanges(e.ChangesJson));
    }

    private static JsonElement ParseMetadata(string? json) =>
        JsonDocument.Parse(string.IsNullOrEmpty(json) ? "{}" : json).RootElement.Clone();

    private static IReadOnlyList<AuditChangeResponse> ParseChanges(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(change => new AuditChangeResponse(
            change.GetProperty("fieldName").GetString()!,
            change.TryGetProperty("oldValue", out var oldValue) ? oldValue.Clone() : null,
            change.TryGetProperty("newValue", out var newValue) ? newValue.Clone() : null,
            change.TryGetProperty("changed", out var changed) ? changed.GetBoolean() : null)).ToList();
    }
}

public sealed record AuditEventPageResponse(IReadOnlyList<AuditEventResponse> Items, string? NextCursor);

/// <summary>Query-string filters for audit search. Bound as text so the service validates them uniformly.</summary>
public sealed class AuditSearchQuery
{
    public string? FromUtc { get; init; }

    public string? ToUtc { get; init; }

    public string? ActorUserId { get; init; }

    public string? Category { get; init; }

    public string? EventType { get; init; }

    public string? EntityType { get; init; }

    public string? EntityId { get; init; }

    public string? Outcome { get; init; }

    public string? CorrelationId { get; init; }

    public int? PageSize { get; init; }

    public string? Cursor { get; init; }

    public AuditSearchRequest ToRequest() =>
        new(FromUtc, ToUtc, ActorUserId, Category, EventType, EntityType, EntityId, Outcome, CorrelationId, PageSize, Cursor);
}
