using System.Text.Json;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// The safe, already-redacted form of an event held in the outbox and the durable fallback sink.
/// </summary>
public sealed record AuditEnvelope(
    Guid EventId,
    DateTime OccurredAtUtc,
    string Category,
    string EventType,
    string Outcome,
    string? ActorUserId,
    string? EntityType,
    string? EntityId,
    string? CorrelationId,
    string? Source,
    string? MetadataJson,
    string? ChangesJson)
{
    public static AuditEnvelope From(AuditEvent e) => new(
        e.Id, e.OccurredAtUtc, e.Category.ToString(), e.EventType, e.Outcome.ToString(),
        e.ActorUserId, e.EntityType, e.EntityId, e.CorrelationId, e.Source, e.MetadataJson, e.ChangesJson);

    public string ToJson() => JsonSerializer.Serialize(this);

    public static AuditEnvelope FromJson(string json) =>
        JsonSerializer.Deserialize<AuditEnvelope>(json) ?? throw new JsonException("The audit envelope is empty.");

    public AuditEvent ToEvent() => new(
        EventId,
        DateTime.SpecifyKind(OccurredAtUtc, DateTimeKind.Utc),
        Enum.Parse<AuditEventCategory>(Category),
        EventType,
        Enum.Parse<AuditOutcome>(Outcome),
        ActorUserId,
        EntityType,
        EntityId,
        CorrelationId,
        Source,
        MetadataJson,
        ChangesJson);
}
