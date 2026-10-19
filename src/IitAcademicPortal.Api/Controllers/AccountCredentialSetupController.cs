using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Api.Security;
using IitAcademicPortal.Application.UserManagement;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// Public completion of first-password setup from the emailed link. Separate from password reset: a reset proof is not accepted here,
/// and this endpoint never resets an existing password. Every invalid, expired, replayed, or stale link gets the same response, which
/// reveals nothing about the account.
/// </summary>
[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Authentication)]
public sealed class AccountCredentialSetupController(AccountCredentialSetupService setup) : ControllerBase
{
    /// <summary>
    /// Checks a setup link before the page shows the password form. Always 200 with <c>usable</c>; an unusable link (used, expired,
    /// reissued, tampered, or for an unknown or active account) is just <c>false</c>, so the answer never reveals account details.
    /// </summary>
    [HttpPost("account-credential-setup-checks")]
    [ProducesResponseType<AccountCredentialSetupCheckResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountCredentialSetupCheckResponse>> Check(AccountCredentialSetupCheckRequest request) =>
        Ok(new AccountCredentialSetupCheckResponse(await setup.IsUsableAsync(request.UserId, request.Proof)));

    [HttpPost("account-credential-setups")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Complete(AccountCredentialSetupRequest request, CancellationToken cancellationToken)
    {
        var outcome = await setup.CompleteAsync(request.UserId, request.Proof, request.NewPassword, cancellationToken);
        switch (outcome.Status)
        {
            case CredentialSetupStatus.Succeeded:
                return NoContent();

            case CredentialSetupStatus.PasswordRejected:
                foreach (var error in outcome.PasswordErrors)
                {
                    ModelState.AddModelError(nameof(request.NewPassword), error);
                }

                return ValidationProblem(ModelState);

            default:
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Setup link not valid",
                    detail: "This setup link has already been used or is no longer valid. If you have already set your password, sign in with it. Otherwise ask an Admin to send a new setup email.");
        }
    }
}
