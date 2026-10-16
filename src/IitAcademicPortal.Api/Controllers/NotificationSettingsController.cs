using IitAcademicPortal.Api.Contracts;
using IitAcademicPortal.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace IitAcademicPortal.Api.Controllers;

/// <summary>
/// The one portal-wide lead time before a feedback window closes at which reminders are due. There is no per-window setting and no
/// default: until an Admin saves a positive number of minutes, <c>configured</c> is false and no reminder is scheduled.
/// </summary>
[Route("api/admin/notification-settings/feedback-reminder")]
public sealed class NotificationSettingsController(FeedbackReminderPolicyService policy) : NotificationControllerBase
{
    [HttpGet]
    [ProducesResponseType<FeedbackReminderPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken) =>
        Ok(FeedbackReminderPolicyResponse.From(await policy.GetAsync(cancellationToken)));

    [HttpPut]
    [ProducesResponseType<FeedbackReminderPolicyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update([FromBody] FeedbackReminderPolicyRequest request, CancellationToken cancellationToken) =>
        Map(await policy.UpdateAsync(request.LeadTimeMinutes, cancellationToken), FeedbackReminderPolicyResponse.From);
}
