using IitAcademicPortal.Application.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Configuration;

/// <summary>Batches: each needs an active Degree, starting Semester, and Academic Session.</summary>
[Route("api/admin/configuration/batches")]
public sealed class BatchesController(CatalogService service) : ConfigurationControllerBase(service)
{
    protected override string? ResourceRoute => "batches";
}
