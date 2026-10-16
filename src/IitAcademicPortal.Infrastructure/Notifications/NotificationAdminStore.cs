using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>
/// Notification administration over the request's <see cref="PortalDbContext"/>, so an audit event staged by the caller is saved in
/// the same transaction as the change. History reads project the current status from the dispatch state.
/// </summary>
public sealed class NotificationAdminStore(PortalDbContext db) : INotificationAdminStore
{
    public async Task<IReadOnlyList<TemplateWithRevision>> ListTemplatesAsync(CancellationToken cancellationToken)
    {
        // The Admin screens manage the email templates; templates of any other channel are not shown here.
        var templates = await db.NotificationTemplates.AsNoTracking().Where(t => t.Channel == NotificationChannel.Email).ToListAsync(cancellationToken);
        var revisionIds = templates.Select(t => t.CurrentRevisionId).OfType<Guid>().ToList();
        var revisions = await db.NotificationTemplateRevisions.AsNoTracking().Where(r => revisionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, cancellationToken);
        return templates.Where(t => t.CurrentRevisionId is { } id && revisions.ContainsKey(id))
            .Select(t => new TemplateWithRevision(t, revisions[t.CurrentRevisionId!.Value]))
            .ToList();
    }

    public async Task<TemplateWithRevision?> FindTemplateAsync(NotificationEventType eventType, CancellationToken cancellationToken)
    {
        var template = await db.NotificationTemplates.AsNoTracking()
            .FirstOrDefaultAsync(t => t.EventType == eventType && t.Channel == NotificationChannel.Email, cancellationToken);
        if (template?.CurrentRevisionId is not { } revisionId)
        {
            return null;
        }

        var revision = await db.NotificationTemplateRevisions.AsNoTracking().FirstOrDefaultAsync(r => r.Id == revisionId, cancellationToken);
        return revision is null ? null : new TemplateWithRevision(template, revision);
    }

    public Task<NotificationTemplate?> FindTrackedTemplateAsync(NotificationEventType eventType, CancellationToken cancellationToken) =>
        db.NotificationTemplates.FirstOrDefaultAsync(t => t.EventType == eventType && t.Channel == NotificationChannel.Email, cancellationToken);

    public void AddRevision(NotificationTemplateRevision revision) => db.NotificationTemplateRevisions.Add(revision);

    public async Task<IReadOnlyList<NotificationSummary>> SearchAsync(NotificationSearch search, CancellationToken cancellationToken)
    {
        var query =
            from record in db.NotificationRecords.AsNoTracking()
            join state in db.NotificationDispatchStates.AsNoTracking() on record.Id equals state.NotificationId
            select new { record, state };

        if (search.Recipient is { } recipient)
        {
            var upper = recipient.ToUpperInvariant();
            query = query.Where(x => x.record.RecipientUserId == recipient || (x.record.Destination != null && x.record.Destination.ToUpper() == upper));
        }

        if (search.EventType is { } eventType)
        {
            query = query.Where(x => x.record.EventType == eventType);
        }

        if (search.Status is { } status)
        {
            query = query.Where(x => x.state.Status == status);
        }

        // The start of the range is included and the end is not.
        if (search.FromUtc is { } from)
        {
            query = query.Where(x => x.record.RequestedAtUtc >= from);
        }

        if (search.ToUtc is { } to)
        {
            query = query.Where(x => x.record.RequestedAtUtc < to);
        }

        if (search.AfterRequestedAtUtc is { } afterAt && search.AfterId is { } afterId)
        {
            query = query.Where(x => x.record.RequestedAtUtc < afterAt || (x.record.RequestedAtUtc == afterAt && x.record.Id.CompareTo(afterId) < 0));
        }

        var rows = await query
            .OrderByDescending(x => x.record.RequestedAtUtc).ThenByDescending(x => x.record.Id)
            .Take(search.Take)
            .ToListAsync(cancellationToken);
        return rows.Select(x => ToSummary(x.record, x.state)).ToList();
    }

    public async Task<NotificationDetail?> GetDetailAsync(Guid notificationId, CancellationToken cancellationToken)
    {
        var found = await (
            from record in db.NotificationRecords.AsNoTracking()
            join state in db.NotificationDispatchStates.AsNoTracking() on record.Id equals state.NotificationId
            join revision in db.NotificationTemplateRevisions.AsNoTracking() on record.TemplateRevisionId equals revision.Id
            where record.Id == notificationId
            select new { record, state, revision.RevisionNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return null;
        }

        var attempts = await db.NotificationAttempts.AsNoTracking()
            .Where(a => a.NotificationId == notificationId)
            .OrderBy(a => a.AttemptNumber)
            .ToListAsync(cancellationToken);

        return new NotificationDetail(
            ToSummary(found.record, found.state),
            found.record.OccurrenceId,
            found.RevisionNumber,
            found.record.Subject,
            found.record.Body,
            attempts.Select(a => new AttemptView(a.AttemptNumber, a.StartedAtUtc, a.CompletedAtUtc, a.Outcome, a.FailureCode, a.ProviderAcceptedAtUtc)).ToList());
    }

    public Task<NotificationRecord?> FindRecordAsync(Guid notificationId, CancellationToken cancellationToken) =>
        db.NotificationRecords.AsNoTracking().FirstOrDefaultAsync(r => r.Id == notificationId, cancellationToken);

    public async Task<NotificationStatus?> GetStatusAsync(Guid notificationId, CancellationToken cancellationToken) =>
        await db.NotificationDispatchStates.AsNoTracking()
            .Where(s => s.NotificationId == notificationId)
            .Select(s => (NotificationStatus?)s.Status)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<NotificationRecord?> FindResendAsync(Guid parentId, string idempotencyKey, CancellationToken cancellationToken) =>
        db.NotificationRecords.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ParentNotificationId == parentId && r.RequestIdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<NotificationStatus>> GetResendStatusesAsync(Guid parentId, CancellationToken cancellationToken) =>
        await (
            from record in db.NotificationRecords.AsNoTracking()
            join state in db.NotificationDispatchStates.AsNoTracking() on record.Id equals state.NotificationId
            where record.ParentNotificationId == parentId
            select state.Status)
            .ToListAsync(cancellationToken);

    public void AddNotification(NotificationRecord record, NotificationDispatchState state)
    {
        db.NotificationRecords.Add(record);
        db.NotificationDispatchStates.Add(state);
    }

    public Task<FeedbackReminderPolicy?> FindPolicyAsync(CancellationToken cancellationToken) =>
        db.FeedbackReminderPolicies.FirstOrDefaultAsync(p => p.Id == FeedbackReminderPolicy.SingletonId, cancellationToken);

    public void AddPolicy(FeedbackReminderPolicy policy) => db.FeedbackReminderPolicies.Add(policy);

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (NotificationStore.IsUniqueViolation(ex))
        {
            throw new NotificationConflictException("Duplicate", ex);
        }
    }

    private static NotificationSummary ToSummary(NotificationRecord record, NotificationDispatchState state) => new(
        record.Id, record.EventType, record.RecipientUserId, record.Destination, record.Channel, state.Status, record.RequestedAtUtc,
        state.SentAtUtc, record.ContentWithheld, record.ParentNotificationId, state.FailureCode);
}
