using IitAcademicPortal.Api.Contracts;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

[ApiController]
[Route("api/auth/anti-forgery-token")]
[AllowAnonymous]
public sealed class AntiForgeryController(IAntiforgery antiforgery) : ControllerBase
{
    /// <summary>
    /// Issues a request token bound to the caller's current identity. Clients fetch a fresh token after
    /// signing in or out because the identity the token is bound to changes.
    /// </summary>
    [HttpGet]
    public ActionResult<AntiForgeryTokenResponse> Get()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new AntiForgeryTokenResponse(tokens.RequestToken!);
    }
}
