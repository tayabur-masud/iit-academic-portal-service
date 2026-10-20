using System.Text.Json;
using IitAcademicPortal.Application.Configuration;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Configuration;

/// <summary>Divisions, Districts, Upazilas, and City Corporations under <c>/api/admin/configuration/geography/{level}</c>.</summary>
[Route("api/admin/configuration/geography/{level}")]
public sealed class GeographyController(CatalogService service, GeographyImportService import) : ConfigurationControllerBase(service)
{
    private static readonly HashSet<string> Levels = new(StringComparer.Ordinal) { "divisions", "districts", "upazilas", "city-corporations" };

    protected override string? ResourceRoute =>
        RouteData.Values["level"] is string level && Levels.Contains(level) ? level : null;

    /// <summary>
    /// Checks an owner-supplied seed document and reports what an import would do. Nothing is written. The report
    /// is returned with 200 when the document is valid and 400 when it is not.
    /// </summary>
    [HttpPost("~/api/admin/configuration/geography/import/validate")]
    [ProducesResponseType<GeographyReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ValidateImport([FromBody] JsonElement document, CancellationToken cancellationToken)
    {
        var result = await import.ValidateAsync(document, cancellationToken);
        return result.Outcome == GeographyImportOutcome.Valid ? Ok(result.Report) : BadRequest(result.Report);
    }

    /// <summary>
    /// Imports a valid seed document into empty geography tables, all or nothing. An invalid document returns 400
    /// with its report; tables that already hold rows return 409, and no Admin-managed row is ever overwritten.
    /// </summary>
    [HttpPost("~/api/admin/configuration/geography/import")]
    [ProducesResponseType<GeographyReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Import([FromBody] JsonElement document, CancellationToken cancellationToken)
    {
        var result = await import.ImportAsync(document, cancellationToken);
        return result.Outcome switch
        {
            GeographyImportOutcome.Imported => Ok(result.Report),
            GeographyImportOutcome.TargetNotEmpty => Conflict(result.Report),
            _ => BadRequest(result.Report),
        };
    }
}
