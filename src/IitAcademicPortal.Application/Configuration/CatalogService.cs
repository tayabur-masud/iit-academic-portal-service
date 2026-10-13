using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Configuration;

namespace IitAcademicPortal.Application.Configuration;

/// <summary>Every configuration resource, found by its route segment. Registered once for the whole application.</summary>
public sealed class ConfigurationResources
{
    private readonly Dictionary<string, IConfigurationResource> byRoute;

    public ConfigurationResources()
    {
        IConfigurationResource[] all =
        [
            new InstitutionResource(),
            new CodedResource<EducationBoard>("education-boards", "Education board", "education-board", () => new EducationBoard()),
            new NameOnlyResource<EducationGroup>("education-groups", "Education group", "education-group", () => new EducationGroup()),
            new NameOnlyResource<ExaminationType>("examination-types", "Examination type", "examination-type", () => new ExaminationType()),
            new CodedResource<Degree>("degrees", "Degree", "degree", () => new Degree()),
            new NameOnlyResource<Semester>("semesters", "Semester", "semester", () => new Semester()),
            new NameOnlyResource<AcademicSession>("academic-sessions", "Academic session", "academic-session", () => new AcademicSession()),
            new CodedResource<Designation>("designations", "Designation", "designation", () => new Designation()),
            new BatchResource(),
            new NameOnlyResource<Division>("divisions", "Division", "division", () => new Division()),
            new DistrictResource(),
            new UpazilaResource(),
        ];
        byRoute = all.ToDictionary(r => r.Route, StringComparer.Ordinal);
    }

    public IConfigurationResource? Find(string route) => byRoute.GetValueOrDefault(route);
}

/// <summary>
/// The entry point for Admin configuration: catalogs, Batches, and the geographic hierarchy. Authorization is the
/// caller's job (the Admin policy on the controllers); everything here validates and keeps the audit event in the
/// same unit of work as the change.
/// </summary>
public sealed class CatalogService(ConfigurationResources resources, IConfigurationStore store, IAuditEventRecorder recorder)
{
    private readonly ConfigurationContext context = new(store, recorder);

    private static readonly ConfigurationResult<ConfigurationItem> UnknownItem = new(ConfigurationOutcome.NotFound);

    public Task<ConfigurationResult<ConfigurationPage>> ListAsync(string route, ConfigurationQuery query, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource
            ? resource.ListAsync(context, query, cancellationToken)
            : Task.FromResult(new ConfigurationResult<ConfigurationPage>(ConfigurationOutcome.NotFound));

    public Task<ConfigurationResult<ConfigurationItem>> GetAsync(string route, Guid id, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? resource.GetAsync(context, id, cancellationToken) : Task.FromResult(UnknownItem);

    public Task<ConfigurationResult<ConfigurationItem>> CreateAsync(string route, ConfigurationInput input, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? resource.CreateAsync(context, input, cancellationToken) : Task.FromResult(UnknownItem);

    public Task<ConfigurationResult<ConfigurationItem>> UpdateAsync(string route, Guid id, ConfigurationInput input, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? resource.UpdateAsync(context, id, input, cancellationToken) : Task.FromResult(UnknownItem);

    public Task<ConfigurationResult<ConfigurationItem>> DeleteAsync(string route, Guid id, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? resource.DeleteAsync(context, id, cancellationToken) : Task.FromResult(UnknownItem);
}
