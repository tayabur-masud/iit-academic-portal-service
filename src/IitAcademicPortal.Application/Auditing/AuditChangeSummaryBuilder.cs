using System.Text.Json;
using System.Text.Json.Nodes;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Builds the change summary from the fields a feature has approved. Anything not approved is omitted:
/// the default is to keep no value.
/// </summary>
public static class AuditChangeSummaryBuilder
{
    /// <summary>
    /// Returns a JSON array of changed fields, or null when nothing auditable changed. Approved fields keep
    /// their previous and new values; indicator fields record only that they changed; all other fields are
    /// omitted entirely. Unchanged values are skipped.
    /// </summary>
    public static string? Build(AuditEventDefinition definition, IReadOnlyList<AuditFieldChange>? changes)
    {
        if (changes is null || changes.Count == 0)
        {
            return null;
        }

        var entries = new JsonArray();
        foreach (var change in changes)
        {
            var oldNode = ToNode(change.OldValue);
            var newNode = ToNode(change.NewValue);
            if (JsonNode.DeepEquals(oldNode, newNode))
            {
                continue;
            }

            var approved = definition.ApprovedChangeFields.Contains(change.Field);
            var indicatorOnly = definition.ChangeIndicatorFields.Contains(change.Field);
            if (!approved && !indicatorOnly)
            {
                continue;
            }

            // A credential-like field name never keeps values, even when the owner approved it.
            if (indicatorOnly || AuditSecretGuard.IsSensitiveName(change.Field))
            {
                entries.Add(new JsonObject { ["fieldName"] = change.Field, ["changed"] = true });
                continue;
            }

            entries.Add(new JsonObject
            {
                ["fieldName"] = change.Field,
                ["oldValue"] = AuditSecretGuard.Sanitize(oldNode),
                ["newValue"] = AuditSecretGuard.Sanitize(newNode),
            });
        }

        return entries.Count == 0 ? null : entries.ToJsonString();
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node.DeepClone(),
        _ => JsonSerializer.SerializeToNode(value),
    };
}
