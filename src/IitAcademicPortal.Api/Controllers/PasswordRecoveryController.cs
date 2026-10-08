using IitAcademicPortal.Api.Authentication;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Api.Security;
using IitAcademicPortal.Application.PasswordRecovery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IitAcademicPortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
public sealed class PasswordRecoveryController(PasswordRecoveryService recovery) : ControllerBase
{
    /// <summary>Same response whether or not an eligible account exists.</summary>
    [HttpPost("password-reset-requests")]
    [ProducesResponseType<GenericResetAcceptedResponse>(StatusCodes.Status202Accepted)]
    public ActionResult<GenericResetAcceptedResponse> RequestReset(PasswordResetRequest request)
    {
        recovery.Request(request.Email);
        return Accepted(new GenericResetAcceptedResponse(PasswordRecoveryService.PublicRequestMessage));
    }

    [HttpPost("password-resets")]
    public async Task<IActionResult> CompleteReset(PasswordResetCompletion request, CancellationToken cancellationToken)
    {
        var currentSessionId = User.GetSessionId();
        var outcome = await recovery.ResetAsync(request.Email, request.Proof, request.NewPassword, currentSessionId, cancellationToken);

        switch (outcome.Status)
        {
            case PasswordResetStatus.Succeeded:
                if (currentSessionId is not null)
                {
                    SessionCookie.Delete(Response);
                }

                return NoContent();

            case PasswordResetStatus.PasswordRejected:
                foreach (var error in outcome.PasswordErrors)
                {
                    ModelState.AddModelError(nameof(request.NewPassword), error);
                }

                return ValidationProblem(ModelState);

            default:
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Reset link not valid",
                    detail: "This reset link is invalid, has expired, or was already used. Request a new link.");
        }
    }
}
