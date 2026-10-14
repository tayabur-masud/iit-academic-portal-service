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
            new CodedResource<Degree>("degrees", "Degree", "degree", () => new Degree(),
                dependents: (degree, c, ct) => c.Store.ExistsAsync<Batch>(b => b.DegreeId == degree.Id, ct)),
            new NameOnlyResource<Semester>("semesters", "Semester", "semester", () => new Semester(),
                (semester, c, ct) => c.Store.ExistsAsync<Batch>(b => b.StartingSemesterId == semester.Id, ct)),
            new NameOnlyResource<AcademicSession>("academic-sessions", "Academic session", "academic-session", () => new AcademicSession(),
                (session, c, ct) => c.Store.ExistsAsync<Batch>(b => b.AcademicSessionId == session.Id, ct)),
            new CodedResource<Designation>("designations", "Designation", "designation", () => new Designation()),
            new HallResource(),
            new BatchResource(),
            new NameOnlyResource<Division>("divisions", "Division", "division", () => new Division(),
                (division, c, ct) => c.Store.ExistsAsync<District>(d => d.DivisionId == division.Id, ct)),
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
public sealed class CatalogService(
    ConfigurationResources resources,
    IConfigurationStore store,
    IAuditEventRecorder recorder,
    TimeProvider clock,
    IAuditRequestContext request,
    IAuditActorDirectory actors)
{
    private readonly ConfigurationContext context = new(store, recorder, clock, request);

    private static readonly ConfigurationResult<ConfigurationItem> UnknownItem = new(ConfigurationOutcome.NotFound);

    public async Task<ConfigurationResult<ConfigurationPage>> ListAsync(string route, ConfigurationQuery query, CancellationToken cancellationToken)
    {
        if (resources.Find(route) is not { } resource)
        {
            return new ConfigurationResult<ConfigurationPage>(ConfigurationOutcome.NotFound);
        }

        var result = await resource.ListAsync(context, query, cancellationToken);
        return result.Value is { } page ? result with { Value = page with { Items = await NameActorsAsync(page.Items, cancellationToken) } } : result;
    }

    public async Task<ConfigurationResult<ConfigurationItem>> GetAsync(string route, Guid id, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? await NameActorsAsync(await resource.GetAsync(context, id, cancellationToken), cancellationToken) : UnknownItem;

    public async Task<ConfigurationResult<ConfigurationItem>> CreateAsync(string route, ConfigurationInput input, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? await NameActorsAsync(await resource.CreateAsync(context, input, cancellationToken), cancellationToken) : UnknownItem;

    public async Task<ConfigurationResult<ConfigurationItem>> UpdateAsync(string route, Guid id, ConfigurationInput input, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? await NameActorsAsync(await resource.UpdateAsync(context, id, input, cancellationToken), cancellationToken) : UnknownItem;

    public Task<ConfigurationResult<ConfigurationItem>> DeleteAsync(string route, Guid id, CancellationToken cancellationToken) =>
        resources.Find(route) is { } resource ? resource.DeleteAsync(context, id, cancellationToken) : Task.FromResult(UnknownItem);

    private async Task<ConfigurationResult<ConfigurationItem>> NameActorsAsync(ConfigurationResult<ConfigurationItem> result, CancellationToken cancellationToken) =>
        result.Value is { } item ? result with { Value = (await NameActorsAsync([item], cancellationToken))[0] } : result;

    /// <summary>
    /// Adds the current email of the account that created and last changed each record, with one lookup for the whole response.
    /// The records store only the stable account id, so no personal data is copied into them.
    /// </summary>
    private async Task<IReadOnlyList<ConfigurationItem>> NameActorsAsync(IReadOnlyList<ConfigurationItem> items, CancellationToken cancellationToken)
    {
        var ids = items.SelectMany(i => new[] { i.CreatedByUserId, i.LastModifiedByUserId }).OfType<string>().Distinct().ToArray();
        if (ids.Length == 0)
        {
            return items;
        }

        var emails = await actors.GetEmailsAsync(ids, cancellationToken);
        string? Email(string? id) => id is not null && emails.TryGetValue(id, out var email) ? email : null;
        return items.Select(i => i with { CreatedBy = Email(i.CreatedByUserId), LastModifiedBy = Email(i.LastModifiedByUserId) }).ToList();
    }
}
