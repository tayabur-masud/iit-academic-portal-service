using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// Permanent notification history for Admins: bounded keyset search, safe detail, and resend of a failed record. History is
/// append-only through these routes (there is no update or delete), and security messages never expose subject, body, link, or proof.
/// </summary>
[Route("api/admin/notifications")]
public sealed class NotificationHistoryController(NotificationHistoryService history, NotificationResendService resends) : NotificationControllerBase
{
    [HttpGet]
    [ProducesResponseType<NotificationPageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search([FromQuery] NotificationQueryString query, CancellationToken cancellationToken) =>
        Map(await history.SearchAsync(query.ToQuery(), cancellationToken),
            page => new NotificationPageResponse(page.Items.Select(NotificationResponse.From).ToList(), page.NextCursor));

    [HttpGet("{notificationId:guid}")]
    [ProducesResponseType<NotificationDetailResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid notificationId, CancellationToken cancellationToken) =>
        Map(await history.GetAsync(notificationId, cancellationToken), NotificationDetailResponse.From);

    /// <summary>
    /// Requests a linked, background re-send of a Failed notification. Admin confirmation is the request: the body is empty and an
    /// <c>Idempotency-Key</c> header makes a repeated confirmation return the same execution rather than a second one.
    /// </summary>
    [HttpPost("{notificationId:guid}/resends")]
    [ProducesResponseType<NotificationDetailResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resend(
        Guid notificationId, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken cancellationToken) =>
        Map(await resends.ResendAsync(notificationId, idempotencyKey, cancellationToken),
            NotificationDetailResponse.From,
            detail => $"/api/admin/notifications/{detail.Summary.Id}");
}
