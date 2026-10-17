using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// User Management for Admins: the account directory, generic Admin/Student provisioning, email and role maintenance, deactivation,
/// and setup-email reissue. Every action requires the session's active Admin role, checked on each request; a state-changing call also
/// needs the anti-forgery token. Teacher-specific changes are delegated to Teacher Management. There is no delete, and no response
/// carries a password, proof, or setup link.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
[Route("api/admin/users")]
public sealed class UserManagementController(
    UserDirectoryService directory,
    UserProvisioningService provisioning,
    UserAccessService access,
    AccountCredentialSetupService setup) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<UserAccountPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] UserQueryString query, CancellationToken cancellationToken)
    {
        var result = await directory.ListAsync(query.ToQuery(), cancellationToken);
        return result.Succeeded ? Ok(UserAccountPageResponse.From(result.Value!)) : Map(result);
    }

    [HttpGet("{userId}")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string userId, CancellationToken cancellationToken) =>
        Map(await directory.GetAsync(userId, cancellationToken));

    /// <summary>Creates a pending Admin or Student account with no password, then requests its setup email.</summary>
    [HttpPost]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateUserAccountRequest request, CancellationToken cancellationToken) =>
        Map(await provisioning.CreateAsync(request.ToInput(), cancellationToken));

    [HttpPut("{userId}/email")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateEmail(string userId, [FromBody] UpdateUserEmailRequest request, CancellationToken cancellationToken) =>
        Map(await access.UpdateEmailAsync(userId, request.ToInput(), cancellationToken));

    [HttpPut("{userId}/roles")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateRoles(string userId, [FromBody] UpdateUserRolesRequest request, CancellationToken cancellationToken) =>
        Map(await access.UpdateRolesAsync(userId, request.ToInput(), cancellationToken));

    /// <summary>Blocks sign-in and revokes every session. Nothing is deleted. An Admin cannot deactivate their own account.</summary>
    [HttpPost("{userId}/deactivation")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(string userId, CancellationToken cancellationToken) =>
        Map(await access.DeactivateAsync(userId, cancellationToken));

    /// <summary>
    /// Sends a fresh setup email to an account still waiting for credential setup. Earlier setup links stop working. The response
    /// never contains the link or proof.
    /// </summary>
    [HttpPost("{userId}/credential-setup-reissues")]
    [ProducesResponseType<CredentialSetupReissueResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ReissueCredentialSetup(string userId, CancellationToken cancellationToken)
    {
        var result = await setup.ReissueAsync(userId, cancellationToken);
        if (!result.Succeeded)
        {
            return Map(result);
        }

        var reissue = result.Value!;
        return Ok(new CredentialSetupReissueResponse(
            UserAccountResponse.From(reissue.Account),
            reissue.SetupEmailRequested,
            reissue.SetupEmailRequested
                ? "A new setup email was requested. Earlier setup links no longer work."
                : "Earlier setup links no longer work, but the new setup email could not be queued. Try again shortly."));
    }

    private IActionResult Map<T>(AccountResult<T> result) => result.Outcome switch
    {
        AccountOutcome.Ok when result.Value is UserAccountDetail detail => Ok(UserAccountResponse.From(detail)),
        AccountOutcome.Created when result.Value is UserAccountDetail detail =>
            Created($"/api/admin/users/{Uri.EscapeDataString(detail.UserId)}", UserAccountResponse.From(detail)),
        AccountOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "No account has that identifier."),
        AccountOutcome.Conflict => Problem(statusCode: StatusCodes.Status409Conflict, title: "Not allowed", detail: result.Message),
        // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
        AccountOutcome.Invalid => ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value))),
        _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
    };
}
