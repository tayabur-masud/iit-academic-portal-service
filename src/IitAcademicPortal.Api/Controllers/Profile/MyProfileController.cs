using IitAcademicPortal.Api.Authentication;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.MyProfile;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers.Profile;

/// <summary>
/// My Profile: the signed-in account's own profile for the session's active role, for every supported role. The account and role come
/// only from the validated session; the route takes no identifier, role, query, or body, and has no write. The fallback policy requires an
/// authenticated session, and authenticated responses are not stored.
/// </summary>
[ApiController]
[Route("api/profile")]
public sealed class MyProfileController(MyProfileService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MyProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(User.GetUserId(), User.GetActiveRole(), cancellationToken);
        return result.Outcome switch
        {
            MyProfileOutcome.Ok => Ok(MyProfileResponse.From(result.View!)),
            MyProfileOutcome.Forbidden => Problem(
                statusCode: StatusCodes.Status403Forbidden, title: "Forbidden", detail: "Choose a portal role to see your profile."),
            MyProfileOutcome.NotFound => Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "Your account could not be found."),
            _ => Problem(statusCode: StatusCodes.Status500InternalServerError),
        };
    }
}
