using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>Reminder policy and schedules for the scheduler. Each operation uses its own short-lived context.</summary>
public sealed class FeedbackReminderStore(IDbContextFactory<PortalDbContext> factory) : IFeedbackReminderStore
{
    public async Task<ReminderPolicySnapshot?> GetPolicyAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.FeedbackReminderPolicies.AsNoTracking()
            .Select(p => new ReminderPolicySnapshot(p.LeadTimeMinutes, p.Revision))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackReminderSchedule>> GetSchedulesAsync(
        IReadOnlyCollection<string> occurrenceIds, CancellationToken cancellationToken)
    {
        if (occurrenceIds.Count == 0)
        {
            return [];
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.FeedbackReminderSchedules.AsNoTracking().Where(s => occurrenceIds.Contains(s.OccurrenceId)).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackReminderSchedule>> GetScheduledOutsideAsync(
        IReadOnlyCollection<string> knownOccurrenceIds, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.FeedbackReminderSchedules.AsNoTracking()
            .Where(s => s.State == ReminderScheduleState.Scheduled && !knownOccurrenceIds.Contains(s.OccurrenceId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackReminderSchedule>> GetDueAsync(DateTime nowUtc, int batchSize, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.FeedbackReminderSchedules.AsNoTracking()
            .Where(s => s.State == ReminderScheduleState.Scheduled && s.DueAtUtc <= nowUtc)
            .OrderBy(s => s.DueAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(
        IReadOnlyCollection<FeedbackReminderSchedule> added, IReadOnlyCollection<FeedbackReminderSchedule> changed, CancellationToken cancellationToken)
    {
        if (added.Count == 0 && changed.Count == 0)
        {
            return;
        }

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.FeedbackReminderSchedules.AddRange(added);
        db.FeedbackReminderSchedules.UpdateRange(changed);
        await db.SaveChangesAsync(cancellationToken);
    }
}
