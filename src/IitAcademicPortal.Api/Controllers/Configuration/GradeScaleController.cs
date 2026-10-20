using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts.Configuration;
using IitAcademicPortal.Application.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Configuration;

/// <summary>
/// The global Grade scale under <c>/api/admin/configuration/grades</c>. Bands are read like any catalog, but the only write is
/// <c>PUT grades/scale</c> with the complete scale; creating, changing, or deleting one band on its own answers 405. Every action
/// requires the session's active Admin role, and the write also needs the anti-forgery token.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/configuration/grades")]
public sealed class GradeScaleController(GradeScaleService service) : ControllerBase
{
    private const string ScaleOnly =
        "Grade bands change only together, through PUT /api/admin/configuration/grades/scale with the complete scale.";

    [HttpGet]
    [ProducesResponseType<GradeScalePageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] string? search, [FromQuery] string? status, CancellationToken cancellationToken) =>
        Map(await service.ListAsync(search, status, cancellationToken));

    [HttpGet("{id:guid}")]
    [ProducesResponseType<GradeBandResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => Map(await service.GetAsync(id, cancellationToken));

    /// <summary>
    /// Replaces the scale with the complete set given: bands with an id are updated, bands without one are created, and live
    /// bands left out are deleted. The active bands must cover 0-100% exactly once. Returns the saved scale, or 400 with
    /// field-keyed errors and nothing changed.
    /// </summary>
    [HttpPut("scale")]
    [ProducesResponseType<GradeScalePageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Replace([FromBody] GradeScaleRequest request, CancellationToken cancellationToken) =>
        Map(await service.ReplaceAsync(request.ToInput(), cancellationToken));

    [HttpPost]
    public IActionResult CreateBand() => NotAllowed("GET");

    [HttpPut("{id:guid}")]
    public IActionResult UpdateBand(Guid id) => NotAllowed("GET");

    [HttpDelete("{id:guid}")]
    public IActionResult DeleteBand(Guid id) => NotAllowed("GET");

    private ObjectResult NotAllowed(string allow)
    {
        Response.Headers.Allow = allow;
        return Problem(statusCode: StatusCodes.Status405MethodNotAllowed, title: "Not allowed", detail: ScaleOnly);
    }

    private IActionResult Map(GradeScaleResult result) => result.Outcome switch
    {
        GradeScaleOutcome.Ok when result.Item is { } item => Ok(GradeBandResponse.From(item)),
        GradeScaleOutcome.Ok => Ok(new GradeScalePageResponse(result.Items!.Select(GradeBandResponse.From).ToList(), null)),
        GradeScaleOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No grade band has that identifier."),
        // The keys are already camelCase field paths; building the response directly keeps them from being re-cased.
        _ => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
    };
}
