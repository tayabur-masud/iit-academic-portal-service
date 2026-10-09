using IitAcademicPortal.Api.Authentication;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Api.Security;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IitAcademicPortal.Api.Controllers;

[ApiController]
[Route("api/auth/sessions")]
public sealed class SessionsController(PortalAuthenticationService authentication, TimeProvider timeProvider) : ControllerBase
{
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Authentication)]
    [ProducesResponseType<SessionContextResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<SessionContextResponse>> Create(SignInRequest request, CancellationToken cancellationToken)
    {
        var outcome = await authentication.SignInAsync(request.Email, request.Password, cancellationToken);
        if (!outcome.Succeeded)
        {
            // Identical for unknown emails and wrong passwords.
            return Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in failed",
                detail: "The email or password is incorrect. Check both and try again, or reset your password.");
        }

        // Signing in again from the same browser replaces, rather than orphans, its previous session.
        if (User.GetSessionId() is { } previousSessionId)
        {
            await authentication.RevokeAsync(previousSessionId, SessionRevocationReason.Logout, cancellationToken);
        }

        SessionCookie.Write(Response, outcome.SessionHandle!, timeProvider.GetUtcNow());
        return Created("/api/auth/sessions/current", SessionContextResponse.From(outcome.Context!));
    }

    [HttpGet("current")]
    public ActionResult<SessionContextResponse> GetCurrent()
    {
        return SessionContextResponse.From(User.GetSessionContext());
    }

    /// <summary>Signs out by revoking only the current session.</summary>
    [HttpDelete("current")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        await authentication.RevokeAsync(User.GetSessionId()!.Value, SessionRevocationReason.Logout, cancellationToken);
        SessionCookie.Delete(Response);
        return NoContent();
    }

    [HttpPut("current/active-role")]
    public async Task<ActionResult<SessionContextResponse>> SetActiveRole(ActiveRoleRequest request, CancellationToken cancellationToken)
    {
        if (!PortalRoles.IsSupported(request.Role))
        {
            ModelState.AddModelError(nameof(request.Role), "Choose one of the supported roles.");
            return ValidationProblem(ModelState);
        }

        var outcome = await authentication.SetActiveRoleAsync(User.GetSessionId()!.Value, request.Role, cancellationToken);
        return outcome.Status switch
        {
            SetActiveRoleStatus.Updated => SessionContextResponse.From(outcome.Context!),
            SetActiveRoleStatus.RoleNotAssigned => Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Role not available",
                detail: "You can only switch to a role assigned to your account."),
            _ => Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Sign-in required",
                detail: "Sign in to continue."),
        };
    }
}
