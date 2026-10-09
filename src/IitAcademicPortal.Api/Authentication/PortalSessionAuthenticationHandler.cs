using System.Text.Encodings.Web;
using IitAcademicPortal.Application.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Api.Authentication;

/// <summary>
/// Validates the session cookie against server-side session state on every request: the session must
/// be unrevoked, and its active role is honored only while it remains assigned to the account.
/// </summary>
public sealed class PortalSessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    PortalAuthenticationService authentication,
    TimeProvider timeProvider,
    IProblemDetailsService problemDetails)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    public const string SchemeName = "PortalSession";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var handle = SessionCookie.Read(Request);
        if (string.IsNullOrEmpty(handle))
        {
            return AuthenticateResult.NoResult();
        }

        var session = await authentication.ValidateAsync(handle, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("The session is not active.");
        }

        SessionCookie.Write(Response, handle, timeProvider.GetUtcNow());
        var principal = PortalClaims.CreatePrincipal(session, SchemeName);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // A cookie that failed validation is stale (revoked or unknown); stop the browser resending it.
        if (SessionCookie.Read(Request) is not null)
        {
            SessionCookie.Delete(Response);
        }

        await WriteProblemAsync(StatusCodes.Status401Unauthorized, "Sign-in required", "Sign in to continue.");
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties) =>
        WriteProblemAsync(
            StatusCodes.Status403Forbidden,
            "Access denied",
            "Your current role does not have access to this resource.");

    private async Task WriteProblemAsync(int status, string title, string detail)
    {
        Response.StatusCode = status;
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = Context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title, Detail = detail },
        });
    }
}
