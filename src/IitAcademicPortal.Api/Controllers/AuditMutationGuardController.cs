using IitAcademicPortal.Api.Security;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// Audit history is read-only. Any attempt to create, change, or delete an event through the API gets
/// HTTP 405 with <c>Allow: GET</c> and is recorded as an <c>audit.mutation.attempted</c> security event, whatever
/// the caller's role. Nothing here touches stored events.
/// </summary>
/// <remarks>
/// Anonymous callers get 401 from the secure-by-default policy and are not recorded. The CSRF filter is skipped
/// because these actions change nothing; otherwise an attempt without a CSRF token would be rejected with 400
/// before it could be recorded.
/// </remarks>
[ApiController]
[SkipAntiforgeryValidation]
public sealed class AuditMutationGuardController(IAuditEventRecorder audit, IAuditRequestContext context) : ControllerBase
{
    [AcceptVerbs("POST", "PUT", "PATCH", "DELETE")]
    [Route("api/audit-events")]
    [Route("api/audit-events/{eventId}")]
    public async Task<IActionResult> Reject(string? eventId = null)
    {
        await audit.RecordSecurityAsync(new AuditEventRequest(AuditEventDefinitions.AuditMutationAttempted, AuditOutcome.Denied)
        {
            ActorUserId = context.ActorUserId,
            Metadata = new Dictionary<string, object?>
            {
                ["method"] = Request.Method,

                // Only whether a collection or one event was targeted: the identifier is client-controlled.
                ["target"] = string.IsNullOrEmpty(eventId) ? "collection" : "event",
            },
        });

        Response.Headers.Allow = "GET";
        return Problem(
            statusCode: StatusCodes.Status405MethodNotAllowed,
            title: "Method not allowed",
            detail: "Audit history is read-only. It can be searched and viewed, but not created, changed, or deleted.");
    }
}
