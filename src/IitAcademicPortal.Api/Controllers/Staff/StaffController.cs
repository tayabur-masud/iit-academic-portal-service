using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Staff;

/// <summary>
/// Staff Management for Admins: the staff directory; creating a staff member by email (a new pending Admin account, or an existing
/// eligible one); reading and updating a profile; and deactivating a staff member with their account. Every action requires the session's
/// active Admin role, checked on each request; a state-changing call also needs the anti-forgery token. An Admin updates their own profile
/// through the same routes. There is no delete, reactivation, role change, or My Profile write.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/staff")]
public sealed class StaffController(StaffProfileService service, StaffLifecycleService lifecycle) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<StaffPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] StaffQueryString query, CancellationToken cancellationToken)
    {
        var result = await service.ListAsync(query.ToQuery(), cancellationToken);
        return result.Outcome == StaffOutcome.Ok ? Ok(StaffPageResponse.From(result.Value!)) : Map(result);
    }

    [HttpGet("{staffProfileId:guid}")]
    [ProducesResponseType<StaffProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid staffProfileId, CancellationToken cancellationToken) =>
        Map(await service.GetAsync(staffProfileId, cancellationToken));

    /// <summary>Returns the profile with <c>accountCreated</c> and <c>setupEmailRequested</c>, so the caller can say what happened.</summary>
    [HttpPost]
    [ProducesResponseType<StaffProfileResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateStaffProfileRequest request, CancellationToken cancellationToken) =>
        Map(await service.CreateAsync(request.ToInput(), cancellationToken));

    [HttpPut("{staffProfileId:guid}")]
    [ProducesResponseType<StaffProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid staffProfileId, [FromBody] UpdateStaffProfileRequest request, CancellationToken cancellationToken) =>
        Map(await service.UpdateAsync(staffProfileId, request.ToInput(), cancellationToken));

    /// <summary>Deactivates the staff member and their account and revokes their sessions; repeating it changes nothing.</summary>
    [HttpPost("{staffProfileId:guid}/deactivation")]
    [ProducesResponseType<StaffProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid staffProfileId, CancellationToken cancellationToken) =>
        Map(await lifecycle.DeactivateAsync(staffProfileId, cancellationToken));

    private IActionResult Map<T>(StaffResult<T> result) => result.Outcome switch
    {
        StaffOutcome.Ok when result.Value is StaffDetail detail => Ok(StaffProfileResponse.From(detail)),
        StaffOutcome.Created when result.Value is StaffDetail detail => Created($"/api/admin/staff/{detail.Id}", StaffProfileResponse.From(detail)),
        StaffOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: result.Message),
        StaffOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Not allowed", detail: result.Message),
        // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
        StaffOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
