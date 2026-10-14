using IitAcademicPortal.Application.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Configuration;

/// <summary>The eight independent academic catalogs under <c>/api/admin/configuration/{catalog}</c>.</summary>
[Route("api/admin/configuration/{catalog}")]
public sealed class CatalogsController(CatalogService service) : ConfigurationControllerBase(service)
{
    private static readonly HashSet<string> Catalogs = new(StringComparer.Ordinal)
    {
        "institutions", "education-boards", "education-groups", "examination-types",
        "degrees", "semesters", "academic-sessions", "designations", "halls",
    };

    protected override string? ResourceRoute =>
        RouteData.Values["catalog"] is string catalog && Catalogs.Contains(catalog) ? catalog : null;
}
