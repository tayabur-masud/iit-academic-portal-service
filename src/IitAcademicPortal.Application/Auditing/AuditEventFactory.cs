using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Auditing;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Turns a feature's request into a validated, redacted, size-limited <see cref="AuditEvent"/>.
/// </summary>
/// <remarks>
/// A strict build (business events) rejects invalid input so the business change does not commit with a
/// bad event. A lenient build (security events) truncates, so a recording problem never changes the
/// authentication or authorization decision.
/// </remarks>
public static partial class AuditEventFactory
{
    public static AuditEvent Create(
        AuditEventRequest request, IAuditRequestContext context, DateTime occurredAtUtc, bool strict)
    {
        var definition = request.Definition;
        if (!EventTypePattern().IsMatch(definition.EventType) || definition.EventType.Length > AuditEvent.MaxEventTypeLength)
        {
            throw new AuditValidationException(
                "An event type is lower-case, dot-separated, and at most 100 characters, for example course.created.");
        }

        var actor = Limit(request.ActorUserId, AuditEvent.MaxActorUserIdLength, "actor", strict);
        var entityType = Limit(request.EntityType, AuditEvent.MaxEntityTypeLength, "entity type", strict);
        var entityId = Limit(request.EntityId, AuditEvent.MaxEntityIdLength, "entity ID", strict);
        var correlationId = Limit(context.CorrelationId, AuditEvent.MaxCorrelationIdLength, "correlation ID", strict);
        var source = Limit(context.Source, AuditEvent.MaxSourceLength, "source", strict);

        var metadataJson = BuildMetadata(request.Metadata, strict);
        var changesJson = BuildChanges(definition, request.Changes, strict);

        return new AuditEvent(
            Guid.NewGuid(),
            occurredAtUtc,
            definition.Category,
            definition.EventType,
            request.Outcome,
            actor,
            entityType,
            entityId,
            correlationId,
            source,
            metadataJson,
            changesJson);
    }

    private static string? BuildMetadata(IReadOnlyDictionary<string, object?>? metadata, bool strict)
    {
        if (metadata is null || metadata.Count == 0)
        {
            return null;
        }

        var node = AuditSecretGuard.Sanitize(JsonSerializer.SerializeToNode(metadata));
        var json = node!.ToJsonString();
        if (Encoding.UTF8.GetByteCount(json) <= AuditEvent.MaxMetadataBytes)
        {
            return json;
        }

        if (strict)
        {
            throw new AuditValidationException("Audit metadata is limited to 4 KB.");
        }

        return new JsonObject { ["truncated"] = true }.ToJsonString();
    }

    private static string? BuildChanges(AuditEventDefinition definition, IReadOnlyList<AuditFieldChange>? changes, bool strict)
    {
        var json = AuditChangeSummaryBuilder.Build(definition, changes);
        if (json is null || Encoding.UTF8.GetByteCount(json) <= AuditEvent.MaxChangesBytes)
        {
            return json;
        }

        if (strict)
        {
            throw new AuditValidationException("An audit change summary is limited to 16 KB.");
        }

        return new JsonArray(new JsonObject { ["fieldName"] = "*", ["changed"] = true }).ToJsonString();
    }

    private static string? Limit(string? value, int max, string name, bool strict)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= max)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        return strict
            ? throw new AuditValidationException($"An audit {name} is limited to {max} characters.")
            : value[..max];
    }

    [GeneratedRegex(@"^[a-z][a-z0-9-]*(\.[a-z][a-z0-9-]*)+$")]
    private static partial Regex EventTypePattern();
}
