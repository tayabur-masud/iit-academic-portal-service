using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace IitAcademicPortal.Api.Security;

/// <summary>
/// Requires a valid anti-forgery request token (X-CSRF-Token header paired with the anti-forgery cookie)
/// on every state-changing API request, including anonymous sign-in and recovery requests.
/// </summary>
public sealed class ValidateAntiforgeryTokenFilter(IAntiforgery antiforgery, ProblemDetailsFactory problems)
    : IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.ActionDescriptor.EndpointMetadata.OfType<SkipAntiforgeryValidationAttribute>().Any())
        {
            return;
        }

        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            return;
        }

        if (!await antiforgery.IsRequestValidAsync(context.HttpContext))
        {
            var problem = problems.CreateProblemDetails(
                context.HttpContext,
                StatusCodes.Status400BadRequest,
                title: "Request could not be verified",
                detail: "Refresh the page and try again.");
            context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
        }
    }
}
