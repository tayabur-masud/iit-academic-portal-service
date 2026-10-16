using System.Globalization;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.Logging;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>What one scheduler pass did, for tests and operations.</summary>
public sealed record ReminderPassResult(int Scheduled, int Cancelled, int Dispatched);

/// <summary>
/// Turns the feedback-window owner's authoritative windows and the global lead time into at most one reminder per student per
/// window occurrence. A schedule holds only identifiers and times: the scheduler never reads feedback submissions and cannot tell
/// who submitted, so reminders go to every eligible student. When the close time or the lead time changes, a reminder not yet issued
/// is rescheduled; one already issued is never repeated. A window that is closed, cancelled, or superseded cancels its reminders.
/// </summary>
public sealed class FeedbackReminderScheduler(
    IFeedbackWindowSource windows,
    IFeedbackReminderStore store,
    INotificationRequestService notifications,
    TimeProvider timeProvider,
    ILogger<FeedbackReminderScheduler> logger)
{
    public const int DispatchBatchSize = 200;

    public async Task<ReminderPassResult> ProcessAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var snapshots = await windows.GetWindowsAsync(cancellationToken);
        var byOccurrence = snapshots.GroupBy(w => w.OccurrenceId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var policy = await store.GetPolicyAsync(cancellationToken);

        var (scheduled, cancelled) = (0, 0);
        var added = new List<FeedbackReminderSchedule>();
        var changed = new List<FeedbackReminderSchedule>();

        var existing = (await store.GetSchedulesAsync(byOccurrence.Keys.ToList(), cancellationToken))
            .ToDictionary(s => (s.OccurrenceId, s.RecipientUserId));

        foreach (var window in byOccurrence.Values)
        {
            var open = window.Status == FeedbackWindowStatus.Open;
            var eligible = window.EligibleStudentUserIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);

            foreach (var schedule in existing.Values.Where(s => s.OccurrenceId == window.OccurrenceId && s.State == ReminderScheduleState.Scheduled))
            {
                // A closed, cancelled, or superseded window, or a student no longer eligible, makes the reminder obsolete.
                if (!open || !eligible.Contains(schedule.RecipientUserId))
                {
                    Cancel(schedule, now, changed);
                    cancelled++;
                }
            }

            if (!open || policy is null)
            {
                continue;
            }

            var due = DueAt(window.CloseAtUtc, policy.LeadTimeMinutes);
            foreach (var student in eligible)
            {
                if (!existing.TryGetValue((window.OccurrenceId, student), out var schedule))
                {
                    var created = FeedbackReminderSchedule.Create(window.OccurrenceId, student, window.CloseAtUtc, policy.Revision, due, now);
                    added.Add(created);
                    existing[(window.OccurrenceId, student)] = created;
                    scheduled++;
                }
                else if (schedule.State == ReminderScheduleState.Scheduled
                    && (schedule.CloseAtUtc != window.CloseAtUtc || schedule.PolicyRevision != policy.Revision || schedule.DueAtUtc != due))
                {
                    // The close time or the global lead time changed before the reminder was issued: recompute, never duplicate.
                    schedule.CloseAtUtc = window.CloseAtUtc;
                    schedule.PolicyRevision = policy.Revision;
                    schedule.DueAtUtc = due;
                    schedule.UpdatedAtUtc = now;
                    changed.Add(schedule);
                    scheduled++;
                }
            }
        }

        // Occurrences the owner no longer reports at all are cancelled too.
        foreach (var orphan in await store.GetScheduledOutsideAsync(byOccurrence.Keys.ToList(), cancellationToken))
        {
            Cancel(orphan, now, changed);
            cancelled++;
        }

        await store.SaveAsync(added, changed.Distinct().ToList(), cancellationToken);

        var dispatched = await DispatchDueAsync(byOccurrence, now, cancellationToken);
        return new ReminderPassResult(scheduled, cancelled + dispatched.Cancelled, dispatched.Dispatched);
    }

    private async Task<(int Dispatched, int Cancelled)> DispatchDueAsync(
        Dictionary<string, FeedbackWindowSnapshot> byOccurrence, DateTime now, CancellationToken cancellationToken)
    {
        var dispatched = 0;
        var cancelled = 0;
        var changed = new List<FeedbackReminderSchedule>();
        foreach (var schedule in await store.GetDueAsync(now, DispatchBatchSize, cancellationToken))
        {
            // Send only while the window is still current and open at its current closing time.
            if (!byOccurrence.TryGetValue(schedule.OccurrenceId, out var window)
                || window.Status != FeedbackWindowStatus.Open
                || window.CloseAtUtc <= now)
            {
                Cancel(schedule, now, changed);
                cancelled++;
                continue;
            }

            var context = new Dictionary<string, string>
            {
                ["courseName"] = window.CourseLabel,
                ["windowName"] = window.WindowLabel,
                ["closesAtUtc"] = window.CloseAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            };
            var result = await notifications.RequestAsync(
                new NotificationEventRequest(NotificationEventType.FeedbackClosingReminder, schedule.OccurrenceId, [schedule.RecipientUserId], context),
                cancellationToken);
            if (result.Status == NotificationRequestStatus.PersistenceFailed)
            {
                // Left Scheduled and due: the next pass tries again, and the occurrence key prevents a duplicate.
                logger.LogWarning("A feedback reminder could not be requested and will be retried");
                continue;
            }

            schedule.State = ReminderScheduleState.Dispatched;
            schedule.UpdatedAtUtc = now;
            changed.Add(schedule);
            dispatched++;
        }

        await store.SaveAsync([], changed, cancellationToken);
        return (dispatched, cancelled);
    }

    private static DateTime DueAt(DateTime closeAtUtc, int leadTimeMinutes) => closeAtUtc - TimeSpan.FromMinutes(leadTimeMinutes);

    private static void Cancel(FeedbackReminderSchedule schedule, DateTime now, List<FeedbackReminderSchedule> changed)
    {
        schedule.State = ReminderScheduleState.Cancelled;
        schedule.UpdatedAtUtc = now;
        changed.Add(schedule);
    }
}

/// <summary>The feedback-window source used until the feedback owner supplies one: it reports no windows, so nothing is scheduled.</summary>
public sealed class NoFeedbackWindowSource : IFeedbackWindowSource
{
    public Task<IReadOnlyList<FeedbackWindowSnapshot>> GetWindowsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<FeedbackWindowSnapshot>>([]);
}
