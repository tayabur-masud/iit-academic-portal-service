using System.Collections.Concurrent;

namespace IitAcademicPortal.Application.Auditing;

/// <summary>
/// Business events owned by the Configuration feature, named <c>configuration.{resource}.{action}</c>. Only the
/// fields listed here keep their before and after values; configuration holds no secrets or personal data, and
/// anything else is omitted by default.
/// </summary>
public static class ConfigurationAuditEventDefinitions
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deactivated = "deactivated";
    public const string Deleted = "deleted";

    /// <summary>The fields a configuration event may record, across every resource.</summary>
    public static readonly string[] ApprovedFields =
    [
        "name", "code", "type", "address", "isActive",
        "degreeId", "startingSemesterId", "academicSessionId", "divisionId", "districtId",
    ];

    /// <summary>A bulk geography import: its metadata carries the source citation, version, and per-level counts.</summary>
    public static AuditEventDefinition GeographyImported { get; } = AuditEventDefinition.Business("configuration.geography.imported");

    private static readonly ConcurrentDictionary<string, AuditEventDefinition> Cache = new(StringComparer.Ordinal);

    /// <summary>The event for one action on one resource, for example <c>configuration.degree.created</c>.</summary>
    public static AuditEventDefinition For(string resourceKey, string action) =>
        Cache.GetOrAdd($"configuration.{resourceKey}.{action}", type => AuditEventDefinition.Business(type, ApprovedFields));
}
