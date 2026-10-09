using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Domain.Auditing;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

/// <summary>
/// The durable security-event outbox. Every operation uses its own short-lived context from the factory, so
/// it never shares a transaction or pending changes with the request that raised the event.
/// </summary>
public sealed class SecurityAuditOutboxStore(IDbContextFactory<PortalDbContext> factory) : ISecurityAuditOutboxStore
{
    public async Task EnqueueAsync(Guid eventId, string envelopeJson, DateTime enqueuedAtUtc, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        db.SecurityAuditOutbox.Add(new SecurityAuditOutboxItem(eventId, envelopeJson, enqueuedAtUtc));
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditOutboxItem>> ClaimDueAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        var due = await db.SecurityAuditOutbox.AsNoTracking()
            .Where(i => (i.State == OutboxDeliveryState.Pending || i.State == OutboxDeliveryState.RetryScheduled)
                && (i.NextAttemptAtUtc == null || i.NextAttemptAtUtc <= nowUtc)
                && (i.LeaseUntilUtc == null || i.LeaseUntilUtc < nowUtc))
            .OrderBy(i => i.EnqueuedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        var leaseUntil = nowUtc + lease;
        var claimed = new List<SecurityAuditOutboxItem>(due.Count);
        foreach (var item in due)
        {
            // The conditional update is the claim: it matches only while no other worker holds a live lease.
            var eventId = item.EventId;
            var affected = await db.SecurityAuditOutbox
                .Where(i => i.EventId == eventId
                    && (i.State == OutboxDeliveryState.Pending || i.State == OutboxDeliveryState.RetryScheduled)
                    && (i.LeaseUntilUtc == null || i.LeaseUntilUtc < nowUtc))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(i => i.LeaseUntilUtc, leaseUntil)
                        .SetProperty(i => i.AttemptCount, i => i.AttemptCount + 1)
                        .SetProperty(i => i.LastAttemptAtUtc, nowUtc),
                    cancellationToken);

            if (affected == 1)
            {
                claimed.Add(item);
            }
        }

        return claimed;
    }

    public async Task DeliverAsync(SecurityAuditOutboxItem item, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var auditEvent = AuditEnvelope.FromJson(item.EnvelopeJson).ToEvent();

        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            db.AuditEvents.Add(auditEvent);
            await db.SaveChangesAsync(cancellationToken);
            await MarkDeliveredAsync(db, item.EventId, nowUtc, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // A replay after a crash: if the event is already stored, acknowledge it without a second copy.
            await using var check = await factory.CreateDbContextAsync(cancellationToken);
            var eventId = auditEvent.Id;
            if (!await check.AuditEvents.AnyAsync(e => e.Id == eventId, cancellationToken))
            {
                throw;
            }

            await MarkDeliveredAsync(check, item.EventId, nowUtc, cancellationToken);
        }
    }

    public async Task RecordFailureAsync(
        Guid eventId, DateTime nowUtc, string failureCode, DateTime? nextAttemptAtUtc, bool exhausted, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var state = exhausted ? OutboxDeliveryState.Exhausted : OutboxDeliveryState.RetryScheduled;
        await db.SecurityAuditOutbox
            .Where(i => i.EventId == eventId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.State, state)
                    .SetProperty(i => i.NextAttemptAtUtc, nextAttemptAtUtc)
                    .SetProperty(i => i.LastAttemptAtUtc, nowUtc)
                    .SetProperty(i => i.LastFailureCode, failureCode)
                    .SetProperty(i => i.LeaseUntilUtc, (DateTime?)null),
                cancellationToken);
    }

    public async Task<SecurityAuditOutboxStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var counts = await db.SecurityAuditOutbox.AsNoTracking()
            .GroupBy(i => i.State)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var oldest = await db.SecurityAuditOutbox.AsNoTracking()
            .Where(i => i.State == OutboxDeliveryState.Pending
                || i.State == OutboxDeliveryState.RetryScheduled
                || i.State == OutboxDeliveryState.Exhausted)
            .OrderBy(i => i.EnqueuedAtUtc)
            .Select(i => (DateTime?)i.EnqueuedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        int Count(OutboxDeliveryState state) => counts.FirstOrDefault(c => c.State == state)?.Count ?? 0;
        return new SecurityAuditOutboxStatus(
            Count(OutboxDeliveryState.Pending), Count(OutboxDeliveryState.RetryScheduled), Count(OutboxDeliveryState.Exhausted), oldest);
    }

    private static Task<int> MarkDeliveredAsync(PortalDbContext db, Guid eventId, DateTime nowUtc, CancellationToken cancellationToken) =>
        db.SecurityAuditOutbox
            .Where(i => i.EventId == eventId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(i => i.State, OutboxDeliveryState.Delivered)
                    .SetProperty(i => i.DeliveredAtUtc, nowUtc)
                    .SetProperty(i => i.NextAttemptAtUtc, (DateTime?)null)
                    .SetProperty(i => i.LastFailureCode, (string?)null)
                    .SetProperty(i => i.LeaseUntilUtc, (DateTime?)null),
                cancellationToken);
}
