using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// Common behavior of the notification administration routes: every action requires the session's active Admin role, re-checked on
/// each request, and a state-changing call also needs the anti-forgery token. Results map to field-keyed validation problems,
/// 404, and 409 with a stable <c>code</c> that never carries provider diagnostics or message content.
/// </summary>
[ApiController]
[Authorize(Policy = PortalPolicies.AdminModule)]
public abstract class NotificationControllerBase : ControllerBase
{
    protected IActionResult Map<T>(NotificationResult<T> result, Func<T, object> project, Func<T, string?>? location = null)
    {
        switch (result.Outcome)
        {
            case NotificationOutcome.Ok:
                return Ok(project(result.Value!));
            case NotificationOutcome.Accepted:
                var target = location?.Invoke(result.Value!);
                return target is null ? Accepted(project(result.Value!)) : Accepted(target, project(result.Value!));
            case NotificationOutcome.NotFound:
                return Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "There is no such notification resource.");
            case NotificationOutcome.Invalid:
                // The keys are the camelCase field names; building the response directly keeps them from being re-cased.
                return ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value)));
            case NotificationOutcome.Conflict:
                var problem = ProblemDetailsFactory.CreateProblemDetails(
                    HttpContext, StatusCodes.Status409Conflict, "Not allowed", detail: result.Message);
                problem.Extensions["code"] = result.Code;
                return new ObjectResult(problem) { StatusCode = StatusCodes.Status409Conflict };
            default:
                return Problem(statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    protected IActionResult UnknownEvent() =>
        Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: "There is no such notification template.");
}
