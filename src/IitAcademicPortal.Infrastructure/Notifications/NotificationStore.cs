using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Infrastructure.Notifications;

/// <summary>
/// Durable notification work in PostgreSQL. Every operation uses its own short-lived context from the factory, so it never shares a
/// transaction or pending changes with the request that raised the event, and a failure here cannot undo that action. Workers
/// change only the dispatch state and append attempts; the notification record and template revisions are never updated.
/// </summary>
public sealed class NotificationStore(IDbContextFactory<PortalDbContext> factory) : INotificationStore
{
    private const string UniqueViolation = "23505";

    // SQLite extended result codes, used by the test host: UNIQUE and PRIMARYKEY.
    private const int SqliteUnique = 2067;
    private const int SqlitePrimaryKey = 1555;

    public async Task<TemplateSnapshot?> GetTemplateAsync(NotificationEventType eventType, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await (
            from template in db.NotificationTemplates.AsNoTracking()
            join revision in db.NotificationTemplateRevisions.AsNoTracking() on template.CurrentRevisionId equals revision.Id
            where template.EventType == eventType && template.Channel == NotificationChannel.Email
            select new TemplateSnapshot(
                template.Id, template.EventType, template.Enabled, revision.Id, revision.RevisionNumber,
                revision.Subject, revision.Body, revision.Placeholders))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<AcceptResult> AcceptAsync(NotificationRecord record, NotificationDispatchState state, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.NotificationRecords.Add(record);
        db.NotificationDispatchStates.Add(state);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AcceptResult.Inserted;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return AcceptResult.Duplicate;
        }
    }

    public async Task<ClaimResult> ClaimDueAsync(DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var due = await db.NotificationDispatchStates.AsNoTracking()
            .Where(s => (s.Status == NotificationStatus.Pending || s.Status == NotificationStatus.Retrying)
                && s.NextAttemptAtUtc != null && s.NextAttemptAtUtc <= nowUtc
                && (s.LeaseExpiresAtUtc == null || s.LeaseExpiresAtUtc < nowUtc))
            .OrderBy(s => s.NextAttemptAtUtc)
            .Take(batchSize)
            .Select(s => new { s.NotificationId, s.AttemptCount, s.SendStartedAtUtc })
            .ToListAsync(cancellationToken);

        var leaseUntil = nowUtc + lease;
        var toSend = new List<ClaimedNotification>(due.Count);
        var interrupted = 0;
        foreach (var candidate in due)
        {
            var id = candidate.NotificationId;

            // The conditional update is the claim: it matches only while no other worker holds a live lease.
            var claimed = await db.NotificationDispatchStates
                .Where(s => s.NotificationId == id
                    && (s.Status == NotificationStatus.Pending || s.Status == NotificationStatus.Retrying)
                    && (s.LeaseExpiresAtUtc == null || s.LeaseExpiresAtUtc < nowUtc))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(s => s.LeaseExpiresAtUtc, leaseUntil).SetProperty(s => s.UpdatedAtUtc, nowUtc),
                    cancellationToken);
            if (claimed != 1)
            {
                continue;
            }

            if (candidate.SendStartedAtUtc is { } started)
            {
                // A worker died after starting the provider call, so whether the provider accepted it is unknown. Say so honestly
                // rather than sending a possible duplicate.
                await MarkInterruptedAsync(db, id, candidate.AttemptCount + 1, started, nowUtc, cancellationToken);
                interrupted++;
                continue;
            }

            var row = await (
                from record in db.NotificationRecords.AsNoTracking()
                join revision in db.NotificationTemplateRevisions.AsNoTracking() on record.TemplateRevisionId equals revision.Id
                where record.Id == id
                select new ClaimedNotification(
                    record.Id, record.EventType, record.RecipientUserId, record.Destination, record.Subject, record.Body,
                    record.TemplateRevisionId, revision.Subject, revision.Body, candidate.AttemptCount))
                .FirstAsync(cancellationToken);
            toSend.Add(row);
        }

        return new ClaimResult(toSend, interrupted);
    }

    public async Task<bool> BeginSendAsync(Guid notificationId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var affected = await db.NotificationDispatchStates
            .Where(s => s.NotificationId == notificationId && s.LeaseExpiresAtUtc != null && s.LeaseExpiresAtUtc >= nowUtc
                && (s.Status == NotificationStatus.Pending || s.Status == NotificationStatus.Retrying))
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.SendStartedAtUtc, nowUtc), cancellationToken);
        return affected == 1;
    }

    public async Task CompleteAttemptAsync(AttemptCompletion completion, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AppendAndAdvanceAsync(db, completion, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AddRecoveryRequestAsync(PasswordRecoveryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.PasswordRecoveryRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> DeleteExpiredRecoveryRequestsAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var ids = await db.PasswordRecoveryRequests.AsNoTracking().Where(r => r.ExpiresAtUtc <= nowUtc).Select(r => r.Id).ToListAsync(cancellationToken);
        return await DeleteRequestsAsync(db, ids, cancellationToken);
    }

    public async Task<IReadOnlyList<RecoveryRequestClaim>> ClaimRecoveryRequestsAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, int maxAttempts, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // A request that used every attempt is dropped (it could not be processed), keeping the transient table bounded.
        var exhausted = await db.PasswordRecoveryRequests.AsNoTracking().Where(r => r.AttemptCount >= maxAttempts).Select(r => r.Id).ToListAsync(cancellationToken);
        await DeleteRequestsAsync(db, exhausted, cancellationToken);

        var due = await db.PasswordRecoveryRequests.AsNoTracking()
            .Where(r => r.ExpiresAtUtc > nowUtc && (r.LeaseExpiresAtUtc == null || r.LeaseExpiresAtUtc < nowUtc))
            .OrderBy(r => r.RequestedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var leaseUntil = nowUtc + lease;
        var claimed = new List<RecoveryRequestClaim>(due.Count);
        foreach (var request in due)
        {
            var id = request.Id;
            var affected = await db.PasswordRecoveryRequests
                .Where(r => r.Id == id && (r.LeaseExpiresAtUtc == null || r.LeaseExpiresAtUtc < nowUtc))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(r => r.LeaseExpiresAtUtc, leaseUntil).SetProperty(r => r.AttemptCount, r => r.AttemptCount + 1),
                    cancellationToken);
            if (affected == 1)
            {
                claimed.Add(new RecoveryRequestClaim(request.Id, request.ProtectedAddress, request.AttemptCount + 1));
            }
        }

        return claimed;
    }

    public async Task DeleteRecoveryRequestAsync(Guid requestId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await DeleteRequestsAsync(db, [requestId], cancellationToken);
    }

    // Only the transient recovery input is ever removed, by key; permanent notification history is never deleted.
    private static async Task<int> DeleteRequestsAsync(PortalDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        foreach (var id in ids)
        {
            db.PasswordRecoveryRequests.Remove(PasswordRecoveryRequest.Stub(id));
        }

        try
        {
            return await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker already removed it, which is the outcome wanted.
            return 0;
        }
    }

    private static async Task MarkInterruptedAsync(
        PortalDbContext db, Guid id, int attemptNumber, DateTime startedAtUtc, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AppendAndAdvanceAsync(
            db,
            new AttemptCompletion(
                id, attemptNumber, startedAtUtc, nowUtc, DeliveryAttemptOutcome.Uncertain, "WorkerInterrupted", null,
                NotificationStatus.Uncertain, null),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // The attempt is appended and the dispatch state advanced together, so history and current status never disagree.
    private static async Task AppendAndAdvanceAsync(PortalDbContext db, AttemptCompletion c, CancellationToken cancellationToken)
    {
        db.NotificationAttempts.Add(NotificationAttempt.Create(
            c.NotificationId, c.AttemptNumber, c.StartedAtUtc, c.CompletedAtUtc, c.Outcome, c.FailureCode, c.ProviderAcceptedAtUtc));
        await db.SaveChangesAsync(cancellationToken);

        var sentAt = c.NewStatus == NotificationStatus.Sent ? c.ProviderAcceptedAtUtc ?? c.CompletedAtUtc : (DateTime?)null;
        await db.NotificationDispatchStates
            .Where(s => s.NotificationId == c.NotificationId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.Status, c.NewStatus)
                    .SetProperty(s => s.AttemptCount, c.AttemptNumber)
                    .SetProperty(s => s.NextAttemptAtUtc, c.NextAttemptAtUtc)
                    .SetProperty(s => s.LeaseExpiresAtUtc, (DateTime?)null)
                    .SetProperty(s => s.SendStartedAtUtc, (DateTime?)null)
                    .SetProperty(s => s.SentAtUtc, sentAt)
                    .SetProperty(s => s.FailureCode, c.StatusFailureCode ?? (c.NewStatus == NotificationStatus.Sent ? null : c.FailureCode))
                    .SetProperty(s => s.UpdatedAtUtc, c.CompletedAtUtc),
                cancellationToken);
    }

    internal static bool IsUniqueViolation(Exception exception)
    {
        for (var inner = exception.InnerException; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState == UniqueViolation;
            }

            // The SQLite provider is referenced only by the tests, so its error code is read by name.
            if (inner.GetType().Name == "SqliteException")
            {
                var extended = inner.GetType().GetProperty("SqliteExtendedErrorCode")?.GetValue(inner) as int?;
                return extended is SqliteUnique or SqlitePrimaryKey || inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
