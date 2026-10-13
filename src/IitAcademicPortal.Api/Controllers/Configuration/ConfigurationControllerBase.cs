using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts.Configuration;
using IitAcademicPortal.Application.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Configuration;

/// <summary>
/// The five operations every configuration resource shares: list, read, create, update (which also deactivates),
/// and delete-if-unreferenced. Every action requires the session's active Admin role; the role is re-checked on
/// each request, and a state-changing call also needs the anti-forgery token.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
public abstract class ConfigurationControllerBase(CatalogService service) : ControllerBase
{
    /// <summary>The resource this request addresses, or null when the route names something this controller does not serve.</summary>
    protected abstract string? ResourceRoute { get; }

    [HttpGet]
    [ProducesResponseType<ConfigurationPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ConfigurationQueryString query, CancellationToken cancellationToken)
    {
        if (ResourceRoute is not { } route)
        {
            return UnknownResource();
        }

        var result = await service.ListAsync(route, query.ToQuery(), cancellationToken);
        return result.Outcome switch
        {
            ConfigurationOutcome.Ok => Ok(new ConfigurationPageResponse(
                result.Value!.Items.Select(ConfigurationItemResponse.From).ToList(),
                result.Value.NextCursor, result.Value.Page, result.Value.TotalCount, result.Value.TotalPages)),
            _ => Map(result),
        };
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConfigurationItemResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) =>
        ResourceRoute is { } route ? Map(await service.GetAsync(route, id, cancellationToken)) : UnknownResource();

    [HttpPost]
    [ProducesResponseType<ConfigurationItemResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] ConfigurationRequest request, CancellationToken cancellationToken) =>
        ResourceRoute is { } route ? Map(await service.CreateAsync(route, request.ToInput(), cancellationToken)) : UnknownResource();

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ConfigurationItemResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConfigurationRequest request, CancellationToken cancellationToken) =>
        ResourceRoute is { } route ? Map(await service.UpdateAsync(route, id, request.ToInput(), cancellationToken)) : UnknownResource();

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken) =>
        ResourceRoute is { } route ? Map(await service.DeleteAsync(route, id, cancellationToken)) : UnknownResource();

    private IActionResult UnknownResource() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "There is no such configuration resource.");

    private IActionResult Map<T>(ConfigurationResult<T> result) => result.Outcome switch
    {
        ConfigurationOutcome.Ok when result.Value is ConfigurationItem item => Ok(ConfigurationItemResponse.From(item)),
        ConfigurationOutcome.Created when result.Value is ConfigurationItem item =>
            Created($"{Request.Path.Value!.TrimEnd('/')}/{item.Id}", ConfigurationItemResponse.From(item)),
        ConfigurationOutcome.Deleted => NoContent(),
        ConfigurationOutcome.NotFound => Problem(
            statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No record has that identifier."),
        ConfigurationOutcome.Conflict => Problem(
            statusCode: StatusCodes.Status409Conflict, title: "Record in use", detail: result.Message),
        // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
        ConfigurationOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
