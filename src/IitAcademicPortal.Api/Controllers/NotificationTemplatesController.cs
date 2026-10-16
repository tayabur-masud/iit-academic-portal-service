using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// The seven email templates: list, view, edit (each wording change creates an immutable revision), and a synthetic-only preview.
/// A security template cannot be disabled, and a preview never shows a live link or proof.
/// </summary>
[Route("api/admin/notification-templates")]
public sealed class NotificationTemplatesController(NotificationTemplateService templates) : NotificationControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<NotificationTemplateResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        Ok((await templates.ListAsync(cancellationToken)).Select(NotificationTemplateResponse.From).ToList());

    [HttpGet("{eventType}/email")]
    [ProducesResponseType<NotificationTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(string eventType, CancellationToken cancellationToken) =>
        NotificationEventTypeExtensions.TryParseSlug(eventType, out var parsed)
            ? Map(await templates.GetAsync(parsed, cancellationToken), v => NotificationTemplateResponse.From(v))
            : UnknownEvent();

    [HttpPut("{eventType}/email")]
    [ProducesResponseType<NotificationTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(string eventType, [FromBody] UpdateNotificationTemplateRequest request, CancellationToken cancellationToken) =>
        NotificationEventTypeExtensions.TryParseSlug(eventType, out var parsed)
            ? Map(await templates.UpdateAsync(parsed, request.ToUpdate(), cancellationToken), v => NotificationTemplateResponse.From(v))
            : UnknownEvent();

    [HttpPost("{eventType}/email/previews")]
    [ProducesResponseType<NotificationPreviewResponse>(StatusCodes.Status200OK)]
    public IActionResult Preview(string eventType, [FromBody] PreviewNotificationTemplateRequest request) =>
        NotificationEventTypeExtensions.TryParseSlug(eventType, out var parsed)
            ? Map(templates.Preview(parsed, request.Subject, request.Body, request.SampleValues), p => new NotificationPreviewResponse(p.Subject, p.Body, p.Html, p.Synthetic))
            : UnknownEvent();
}
