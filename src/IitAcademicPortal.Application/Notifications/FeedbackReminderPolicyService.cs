using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>The portal-wide reminder lead time. <see cref="LeadTimeMinutes"/> is null until an Admin saves one: nothing is assumed.</summary>
public sealed record ReminderPolicyView(bool Configured, int? LeadTimeMinutes, int? Revision);

/// <summary>
/// The one portal-wide lead time before a feedback window closes at which each student gets a reminder. Only a positive whole
/// number of minutes is accepted; an invalid value changes nothing, and there is no per-window override. A change applies to every
/// window that has not had its reminder issued yet. Authorization is the caller's job.
/// </summary>
public sealed class FeedbackReminderPolicyService(INotificationAdminStore store, IAuditEventRecorder recorder)
{
    public async Task<ReminderPolicyView> GetAsync(CancellationToken cancellationToken) =>
        await store.FindPolicyAsync(cancellationToken) is { } policy
            ? new ReminderPolicyView(true, policy.LeadTimeMinutes, policy.Revision)
            : new ReminderPolicyView(false, null, null);

    public async Task<NotificationResult<ReminderPolicyView>> UpdateAsync(int? leadTimeMinutes, CancellationToken cancellationToken)
    {
        var errors = new NotificationErrors();
        if (leadTimeMinutes is null)
        {
            errors.Add("leadTimeMinutes", "Enter the number of minutes before closing.");
        }
        else if (leadTimeMinutes < 1 || leadTimeMinutes > FeedbackReminderPolicy.MaxLeadTimeMinutes)
        {
            errors.Add("leadTimeMinutes", $"Enter a whole number of minutes from 1 to {FeedbackReminderPolicy.MaxLeadTimeMinutes}.");
        }

        if (errors.Any)
        {
            return NotificationResult<ReminderPolicyView>.Invalid(errors);
        }

        var minutes = leadTimeMinutes!.Value;
        var policy = await store.FindPolicyAsync(cancellationToken);
        if (policy is null)
        {
            policy = FeedbackReminderPolicy.Create(minutes);
            store.AddPolicy(policy);
            Stage(policy, null, minutes);
        }
        else if (policy.LeadTimeMinutes != minutes)
        {
            var before = policy.LeadTimeMinutes;
            policy.Change(minutes);
            Stage(policy, before, minutes);
        }

        await store.SaveAsync(cancellationToken);
        return new NotificationResult<ReminderPolicyView>(
            NotificationOutcome.Ok, new ReminderPolicyView(true, policy.LeadTimeMinutes, policy.Revision));
    }

    private void Stage(FeedbackReminderPolicy policy, int? before, int after) =>
        recorder.Stage(new AuditEventRequest(NotificationAuditEventDefinitions.ReminderPolicyUpdated, AuditOutcome.Success)
        {
            EntityType = "FeedbackReminderPolicy",
            EntityId = policy.Id.ToString(),
            Changes = [new AuditFieldChange("leadTimeMinutes", before, after)],
        });
}
