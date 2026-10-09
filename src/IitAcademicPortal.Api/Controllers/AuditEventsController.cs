using IitAcademicPortal.Api.Authorization;
using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// Read-only review of audit history for a session whose active role is Admin. The role is re-checked from the
/// server-validated session on every request; neither navigation nor a client-supplied role grants access.
/// </summary>
[ApiController]
[Route("api/audit-events")]
[Authorize(Policy = PortalPolicies.AuditReview)]
public sealed class AuditEventsController(AuditReviewService review, IAuditRequestContext context) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AuditEventPageResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditEventPageResponse>> Search([FromQuery] AuditSearchQuery query, CancellationToken cancellationToken)
    {
        var result = await review.SearchAsync(query.ToRequest(), context.ActorUserId, cancellationToken);
        if (!result.IsValid)
        {
            // Keys are the query-parameter names. Going through ModelState would re-case them to the bound property
            // names ("PageSize"), so the response is built directly.
            return ValidationProblem(new ValidationProblemDetails(result.Errors!.ToDictionary(e => e.Key, e => e.Value)));
        }

        return new AuditEventPageResponse(result.Value!.Items.Select(AuditEventResponse.From).ToList(), result.Value.NextCursor);
    }

    // The identifier is parsed here, not constrained in the route: a constrained route would let the framework
    // answer a malformed identifier with its own 405 that advertises the mutation methods.
    [HttpGet("{eventId}")]
    [ProducesResponseType<AuditEventResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditEventResponse>> Get(string eventId, CancellationToken cancellationToken)
    {
        var view = Guid.TryParse(eventId, out var id)
            ? await review.GetAsync(id, context.ActorUserId, cancellationToken)
            : null;
        return view is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, title: "Event not found", detail: "No audit event has that identifier.")
            : AuditEventResponse.From(view);
    }
}
