using System.Linq.Expressions;
using System.Text;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

/// <summary>The services one configuration operation uses, sharing the request's unit of work.</summary>
public sealed record ConfigurationContext(IConfigurationStore Store, IAuditEventRecorder Recorder);

/// <summary>Which optional fields a resource accepts. Anything else in a request is rejected.</summary>
[Flags]
public enum ConfigurationFields
{
    None = 0,
    Code = 1,
    Type = 2,
    Address = 4,
    Degree = 8,
    Semester = 16,
    Session = 32,
    Division = 64,
    District = 128,
}

public interface IConfigurationResource
{
    /// <summary>The route segment, for example <c>degrees</c>.</summary>
    string Route { get; }

    /// <summary>The singular name shown in messages and recorded as the audit entity type.</summary>
    string DisplayName { get; }

    Task<ConfigurationResult<ConfigurationPage>> ListAsync(ConfigurationContext context, ConfigurationQuery query, CancellationToken cancellationToken);

    Task<ConfigurationResult<ConfigurationItem>> GetAsync(ConfigurationContext context, Guid id, CancellationToken cancellationToken);

    Task<ConfigurationResult<ConfigurationItem>> CreateAsync(ConfigurationContext context, ConfigurationInput input, CancellationToken cancellationToken);

    Task<ConfigurationResult<ConfigurationItem>> UpdateAsync(ConfigurationContext context, Guid id, ConfigurationInput input, CancellationToken cancellationToken);

    Task<ConfigurationResult<ConfigurationItem>> DeleteAsync(ConfigurationContext context, Guid id, CancellationToken cancellationToken);
}

/// <summary>
/// The shared create, read, update, deactivate, and delete flow of a configuration record. A resource supplies
/// only what differs: its extra fields, the scope of its name uniqueness, and the references it validates. Every
/// success stages one allowlisted audit event and saves it with the change in one transaction.
/// </summary>
public abstract class ConfigurationResource<T>(string route, string displayName, string eventKey, ConfigurationFields supported)
    : IConfigurationResource where T : ConfigurationRecord
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public string Route { get; } = route;

    public string DisplayName { get; } = displayName;

    protected string Noun => DisplayName.ToLowerInvariant();

    protected abstract T NewRecord();

    /// <summary>The records a name must be unique among. Global by default; a child resource limits it to its parent.</summary>
    protected virtual Expression<Func<T, bool>> InScope(T candidate) => _ => true;

    protected virtual string DuplicateNameMessage => $"A {Noun} with this name already exists.";

    /// <summary>Filters a list to one parent, for resources that have one.</summary>
    protected virtual Expression<Func<T, bool>>? ParentFilter(Guid parentId) => null;

    /// <summary>Sets the resource's own fields and validates its references. Runs after the shared name and code rules.</summary>
    protected virtual Task ApplyAsync(
        T entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
        => Task.CompletedTask;

    protected virtual Task<bool> CodeTakenAsync(ConfigurationContext context, T entity, string normalizedCode, CancellationToken cancellationToken)
        => Task.FromResult(false);

    /// <summary>Adds the resource's own fields to the audit snapshot.</summary>
    protected virtual void Describe(T entity, Dictionary<string, object?> values)
    {
    }

    /// <summary>Adds the resource's own fields to the response.</summary>
    protected virtual ConfigurationItem Project(T entity, ConfigurationItem item) => item;

    public async Task<ConfigurationResult<ConfigurationPage>> ListAsync(
        ConfigurationContext context, ConfigurationQuery query, CancellationToken cancellationToken)
    {
        var errors = new ConfigurationErrors();
        var status = ConfigurationStatus.Active;
        if (!string.IsNullOrWhiteSpace(query.Status) && !Enum.TryParse(query.Status, ignoreCase: true, out status))
        {
            errors.Add("status", "The status must be active, inactive, or all.");
        }

        var pageSize = query.PageSize ?? DefaultPageSize;
        if (pageSize is < 1 or > MaxPageSize)
        {
            errors.Add("pageSize", $"The page size must be between 1 and {MaxPageSize}.");
        }

        string? afterName = null;
        Guid? afterId = null;
        if (!string.IsNullOrEmpty(query.Cursor))
        {
            if (ConfigurationCursor.TryDecode(query.Cursor, out var name, out var id))
            {
                (afterName, afterId) = (name, id);
            }
            else
            {
                errors.Add("cursor", "The cursor is not valid. Start again from the first page.");
            }
        }

        if (query.Page is < 1)
        {
            errors.Add("page", "The page number must be 1 or more.");
        }
        else if (query.Page is not null && !string.IsNullOrEmpty(query.Cursor))
        {
            errors.Add("page", "Use either a page number or a cursor, not both.");
        }

        if (errors.Any)
        {
            return new ConfigurationResult<ConfigurationPage>(ConfigurationOutcome.Invalid, Errors: errors.ToDictionary());
        }

        var search = string.IsNullOrWhiteSpace(query.Search) ? null : NameNormalizer.Normalize(query.Search);
        var filter = query.ParentId is { } parent ? ParentFilter(parent) : null;

        if (query.Page is { } pageNumber)
        {
            // Numbered pages: the same order, an offset, and the count of every match. New records can shift the pages
            // while someone browses; cursor paging cannot shift.
            var total = await context.Store.CountMatchingAsync(filter, search, status, cancellationToken);
            var skip = (int)Math.Min((long)(pageNumber - 1) * pageSize, int.MaxValue);
            var slice = await context.Store.ListAsync(filter, search, status, null, null, pageSize, cancellationToken, skip);
            var totalPages = (int)Math.Min((total + pageSize - 1) / pageSize, int.MaxValue);
            return new ConfigurationResult<ConfigurationPage>(
                ConfigurationOutcome.Ok, new ConfigurationPage(slice.Select(ToItem).ToList(), null, pageNumber, total, totalPages));
        }

        var rows = await context.Store.ListAsync(filter, search, status, afterName, afterId, pageSize + 1, cancellationToken);

        string? next = null;
        if (rows.Count > pageSize)
        {
            rows = rows.Take(pageSize).ToList();
            next = ConfigurationCursor.Encode(rows[^1].NormalizedName, rows[^1].Id);
        }

        return new ConfigurationResult<ConfigurationPage>(
            ConfigurationOutcome.Ok, new ConfigurationPage(rows.Select(ToItem).ToList(), next));
    }

    public async Task<ConfigurationResult<ConfigurationItem>> GetAsync(ConfigurationContext context, Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.Store.FindAsync<T>(id, cancellationToken);
        return entity is null
            ? new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.NotFound)
            : new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.Ok, ToItem(entity));
    }

    public async Task<ConfigurationResult<ConfigurationItem>> CreateAsync(
        ConfigurationContext context, ConfigurationInput input, CancellationToken cancellationToken)
    {
        var entity = NewRecord();
        var errors = new ConfigurationErrors();
        await ApplyAllAsync(entity, input, context, errors, isNew: true, cancellationToken);
        if (errors.Any)
        {
            return Invalid(errors);
        }

        entity.IsActive = input.IsActive ?? true;
        context.Store.Add(entity);
        Stage(context, ConfigurationAuditEventDefinitions.Created, entity.Id, before: null, after: Snapshot(entity));
        return await SaveAsync(context, entity, ConfigurationOutcome.Created, cancellationToken);
    }

    public async Task<ConfigurationResult<ConfigurationItem>> UpdateAsync(
        ConfigurationContext context, Guid id, ConfigurationInput input, CancellationToken cancellationToken)
    {
        var entity = await context.Store.FindAsync<T>(id, cancellationToken);
        if (entity is null)
        {
            return new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.NotFound);
        }

        var before = Snapshot(entity);
        var wasActive = entity.IsActive;
        var errors = new ConfigurationErrors();
        await ApplyAllAsync(entity, input, context, errors, isNew: false, cancellationToken);
        if (errors.Any)
        {
            return Invalid(errors);
        }

        if (input.IsActive is { } active)
        {
            entity.IsActive = active;
        }

        var after = Snapshot(entity);
        if (before.Keys.All(key => Equals(before[key], after[key])))
        {
            // Nothing changed, so there is nothing to save and nothing to audit.
            return new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.Ok, ToItem(entity));
        }

        var action = wasActive && !entity.IsActive
            ? ConfigurationAuditEventDefinitions.Deactivated
            : ConfigurationAuditEventDefinitions.Updated;
        Stage(context, action, entity.Id, before, after);
        return await SaveAsync(context, entity, ConfigurationOutcome.Ok, cancellationToken);
    }

    public async Task<ConfigurationResult<ConfigurationItem>> DeleteAsync(ConfigurationContext context, Guid id, CancellationToken cancellationToken)
    {
        var entity = await context.Store.FindAsync<T>(id, cancellationToken);
        if (entity is null)
        {
            return new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.NotFound);
        }

        context.Store.Remove(entity);
        Stage(context, ConfigurationAuditEventDefinitions.Deleted, entity.Id, before: Snapshot(entity), after: null);
        try
        {
            await context.Store.SaveAsync(cancellationToken);
        }
        catch (ConfigurationConflictException ex) when (ex.Kind == ConfigurationConflictKind.Referenced)
        {
            return new ConfigurationResult<ConfigurationItem>(
                ConfigurationOutcome.Conflict,
                Message: $"This {Noun} is used by other records, so it cannot be deleted. Deactivate it instead.");
        }

        return new ConfigurationResult<ConfigurationItem>(ConfigurationOutcome.Deleted);
    }

    private async Task ApplyAllAsync(
        T entity, ConfigurationInput input, ConfigurationContext context, ConfigurationErrors errors, bool isNew, CancellationToken cancellationToken)
    {
        RejectUnsupported(input, errors);

        var name = NameNormalizer.Display(input.Name);
        if (name.Length == 0)
        {
            errors.Add("name", "Enter a name.");
        }
        else if (name.Length > NameNormalizer.MaxNameLength)
        {
            errors.Add("name", $"The name can be at most {NameNormalizer.MaxNameLength} characters.");
        }
        else
        {
            entity.Rename(name);
        }

        if (entity is ICodedRecord coded && supported.HasFlag(ConfigurationFields.Code))
        {
            var code = NameNormalizer.DisplayCode(input.Code);
            if (code is { Length: > NameNormalizer.MaxCodeLength })
            {
                errors.Add("code", $"The code can be at most {NameNormalizer.MaxCodeLength} characters.");
            }
            else
            {
                coded.SetCode(code);
            }
        }

        await ApplyAsync(entity, input, context, errors, isNew, cancellationToken);

        // Duplicates are checked last, once the fields that define a name's scope are known to be valid. The
        // database unique indexes stay the final guard against two requests racing past this check.
        if (errors.Any)
        {
            return;
        }

        if (await context.Store.NameExistsAsync(InScope(entity), entity.NormalizedName, entity.Id, cancellationToken))
        {
            errors.Add("name", DuplicateNameMessage);
        }

        if (entity is ICodedRecord { NormalizedCode: { } normalizedCode }
            && await CodeTakenAsync(context, entity, normalizedCode, cancellationToken))
        {
            errors.Add("code", $"A {Noun} with this code already exists.");
        }
    }

    private void RejectUnsupported(ConfigurationInput input, ConfigurationErrors errors)
    {
        void Reject(bool provided, ConfigurationFields field, string name)
        {
            if (provided && !supported.HasFlag(field))
            {
                errors.Add(name, $"This field is not used for a {Noun}.");
            }
        }

        Reject(!string.IsNullOrWhiteSpace(input.Code), ConfigurationFields.Code, "code");
        Reject(!string.IsNullOrWhiteSpace(input.Type), ConfigurationFields.Type, "type");
        Reject(!string.IsNullOrWhiteSpace(input.Address), ConfigurationFields.Address, "address");
        Reject(input.DegreeId.HasValue, ConfigurationFields.Degree, "degreeId");
        Reject(input.StartingSemesterId.HasValue, ConfigurationFields.Semester, "startingSemesterId");
        Reject(input.AcademicSessionId.HasValue, ConfigurationFields.Session, "academicSessionId");
        Reject(input.DivisionId.HasValue, ConfigurationFields.Division, "divisionId");
        Reject(input.DistrictId.HasValue, ConfigurationFields.District, "districtId");
    }

    /// <summary>
    /// Checks a reference to another record: it must be supplied, exist, and be active. An unchanged reference is
    /// accepted even if the record has since been deactivated, so an existing link can keep being edited.
    /// </summary>
    protected static async Task<Guid> ValidateReferenceAsync<TRef>(
        Guid? requested, Guid current, bool isNew, string field, string label, ConfigurationContext context, ConfigurationErrors errors, CancellationToken cancellationToken)
        where TRef : ConfigurationRecord
    {
        if (requested is null || requested == Guid.Empty)
        {
            errors.Add(field, $"Select a {label}.");
            return current;
        }

        if (!isNew && requested == current)
        {
            return current;
        }

        var reference = await context.Store.FindAsync<TRef>(requested.Value, cancellationToken);
        if (reference is null)
        {
            errors.Add(field, $"The selected {label} does not exist.");
        }
        else if (!reference.IsActive)
        {
            errors.Add(field, $"The selected {label} is inactive. Choose an active {label}.");
        }

        return requested.Value;
    }

    private async Task<ConfigurationResult<ConfigurationItem>> SaveAsync(
        ConfigurationContext context, T entity, ConfigurationOutcome success, CancellationToken cancellationToken)
    {
        try
        {
            await context.Store.SaveAsync(cancellationToken);
        }
        catch (ConfigurationConflictException ex) when (ex.Kind == ConfigurationConflictKind.Duplicate)
        {
            var errors = new ConfigurationErrors();
            errors.Add("name", DuplicateNameMessage);
            return Invalid(errors);
        }

        return new ConfigurationResult<ConfigurationItem>(success, ToItem(entity));
    }

    private static ConfigurationResult<ConfigurationItem> Invalid(ConfigurationErrors errors) =>
        new(ConfigurationOutcome.Invalid, Errors: errors.ToDictionary());

    private ConfigurationItem ToItem(T entity)
    {
        var item = new ConfigurationItem
        {
            Id = entity.Id,
            Name = entity.Name,
            IsActive = entity.IsActive,
            Code = (entity as ICodedRecord)?.Code,
        };
        return Project(entity, item);
    }

    /// <summary>The values that may appear in an audit change summary, keyed by field name.</summary>
    private Dictionary<string, object?> Snapshot(T entity)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["name"] = entity.Name,
            ["isActive"] = entity.IsActive,
        };
        if (entity is ICodedRecord coded)
        {
            values["code"] = coded.Code;
        }

        Describe(entity, values);
        return values;
    }

    private void Stage(ConfigurationContext context, string action, Guid id, Dictionary<string, object?>? before, Dictionary<string, object?>? after)
    {
        var keys = (before?.Keys ?? after!.Keys).ToList();
        var changes = keys
            .Select(key => new AuditFieldChange(key, before?.GetValueOrDefault(key), after?.GetValueOrDefault(key)))
            .ToList();

        context.Recorder.Stage(new AuditEventRequest(ConfigurationAuditEventDefinitions.For(eventKey, action), AuditOutcome.Success)
        {
            EntityType = DisplayName,
            EntityId = id.ToString(),
            Changes = changes,
        });
    }
}

/// <summary>An opaque position in a list ordered by normalized name then identifier.</summary>
public static class ConfigurationCursor
{
    public static string Encode(string normalizedName, Guid id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{normalizedName}\n{id:N}")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecode(string? cursor, out string name, out Guid id)
    {
        (name, id) = (string.Empty, Guid.Empty);
        if (string.IsNullOrEmpty(cursor) || cursor.Length > 600)
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('\n');
            if (parts.Length != 2 || !Guid.TryParseExact(parts[1], "N", out id))
            {
                return false;
            }

            name = parts[0];
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
