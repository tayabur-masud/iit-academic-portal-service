using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Notifications;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// Notification work on a real PostgreSQL 18 database as the restricted runtime role: the migration and its seed, idempotency keys,
/// lease claims and recovery, keyset history paging, and the append-only permissions. SQLite cannot prove these. Set
/// AUDIT_TEST_POSTGRES to run them.
/// </summary>
[Trait("Category", "PostgreSQL")]
public sealed class NotificationPostgresTests(PostgresDatabaseFixture fixture) : IClassFixture<PostgresDatabaseFixture>
{
    private static readonly DateTime Start = new(2031, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private PostgresTestDatabase Db => fixture.Database!;

    private NotificationStore Store => new(Db.CreateRuntimeFactory());

    private static NotificationEventType Event => NotificationEventType.ResultsPublished;

    private static NotificationRecord Record(
        string occurrence, string recipient = "user-a", DateTime? at = null, Guid? parent = null, string? key = null, NotificationEventType? eventType = null) =>
        NotificationRecord.Create(
            eventType ?? Event, occurrence, recipient, "a@iit.test", NotificationTemplateCatalog.InitialRevisionId(eventType ?? Event),
            at ?? Start, "S", "B", parent, key);

    private async Task<NotificationRecord> AcceptAsync(NotificationRecord record, NotificationStatus status = NotificationStatus.Pending, DateTime? stateAt = null)
    {
        var result = await Store.AcceptAsync(record, NotificationDispatchState.Create(record.Id, status, stateAt ?? record.RequestedAtUtc), CancellationToken.None);
        Assert.Equal(AcceptResult.Inserted, result);
        return record;
    }

    private static string Unique(string prefix) => $"{prefix}-{Guid.NewGuid():N}";

    private async Task<string?> SqlStateAsync(Func<Task> action)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(action);
        for (var inner = error; inner is not null; inner = inner.InnerException)
        {
            if (inner is PostgresException postgres)
            {
                return postgres.SqlState;
            }
        }

        return null;
    }

    [PostgresFact]
    public async Task The_migration_seeds_the_seven_templates_with_fixed_identifiers_and_a_first_revision_each()
    {
        await using var db = Db.CreateRuntimeContext();

        var templates = await db.NotificationTemplates.AsNoTracking().OrderBy(t => t.EventType).ToListAsync();
        var revisions = await db.NotificationTemplateRevisions.AsNoTracking().ToListAsync();

        Assert.Equal(7, templates.Count);
        Assert.All(templates, t =>
        {
            Assert.Equal(NotificationTemplateCatalog.TemplateId(t.EventType), t.Id);
            Assert.Equal(NotificationChannel.Email, t.Channel);
            Assert.True(t.Enabled);
            Assert.Equal(1, t.CurrentRevisionNumber);
            Assert.Equal(NotificationTemplateCatalog.InitialRevisionId(t.EventType), t.CurrentRevisionId);
        });
        Assert.Equal(7, revisions.Count);
        Assert.All(revisions, r => Assert.Contains("{{", r.Body));
        Assert.All(revisions.Where(r => templates.Single(t => t.Id == r.TemplateId).EventType.IsSecurity()), r => Assert.Contains("{{actionLink}}", r.Body));
    }

    [PostgresFact]
    public async Task An_original_notification_is_unique_per_occurrence_recipient_and_channel_so_a_replay_is_a_duplicate()
    {
        var occurrence = Unique("dup");
        await AcceptAsync(Record(occurrence));

        var again = Record(occurrence);
        var other = Record(occurrence, "user-b");
        var replay = await Store.AcceptAsync(again, NotificationDispatchState.Create(again.Id, NotificationStatus.Pending, Start), CancellationToken.None);
        var otherRecipient = await Store.AcceptAsync(other, NotificationDispatchState.Create(other.Id, NotificationStatus.Pending, Start), CancellationToken.None);

        Assert.Equal(AcceptResult.Duplicate, replay);
        Assert.Equal(AcceptResult.Inserted, otherRecipient);
    }

    [PostgresFact]
    public async Task A_resend_may_share_the_occurrence_but_not_its_idempotency_key_for_the_same_predecessor()
    {
        var parent = await AcceptAsync(Record(Unique("resend")), NotificationStatus.Failed);

        await using (var db = Db.CreateRuntimeContext())
        {
            var first = Record(parent.OccurrenceId, parent.RecipientUserId, Start.AddMinutes(1), parent.Id, "key-1");
            db.NotificationRecords.Add(first);
            db.NotificationDispatchStates.Add(NotificationDispatchState.Create(first.Id, NotificationStatus.Pending, Start));
            await db.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateRuntimeContext();
        var again = Record(parent.OccurrenceId, parent.RecipientUserId, Start.AddMinutes(2), parent.Id, "key-1");
        duplicate.NotificationRecords.Add(again);
        duplicate.NotificationDispatchStates.Add(NotificationDispatchState.Create(again.Id, NotificationStatus.Pending, Start));
        Assert.Equal("23505", await SqlStateAsync(() => duplicate.SaveChangesAsync()));

        await using var anotherKey = Db.CreateRuntimeContext();
        var other = Record(parent.OccurrenceId, parent.RecipientUserId, Start.AddMinutes(3), parent.Id, "key-2");
        anotherKey.NotificationRecords.Add(other);
        anotherKey.NotificationDispatchStates.Add(NotificationDispatchState.Create(other.Id, NotificationStatus.Pending, Start));
        await anotherKey.SaveChangesAsync();
    }

    [PostgresFact]
    public async Task Only_one_of_two_concurrent_workers_claims_the_same_work_and_the_lease_expiry_makes_it_claimable_again()
    {
        var now = DateTime.UtcNow;
        var record = await AcceptAsync(Record(Unique("lease"), at: now.AddSeconds(-5)), stateAt: now.AddSeconds(-5));
        var lease = TimeSpan.FromMinutes(2);

        var claims = await Task.WhenAll(
            Task.Run(() => Store.ClaimDueAsync(now, lease, 50, CancellationToken.None)),
            Task.Run(() => Store.ClaimDueAsync(now, lease, 50, CancellationToken.None)));
        var claimed = claims.SelectMany(c => c.ToSend).Where(n => n.Id == record.Id).ToList();
        var whileLeased = await Store.ClaimDueAsync(now.AddMinutes(1), lease, 50, CancellationToken.None);
        var afterLease = await Store.ClaimDueAsync(now.AddMinutes(3), lease, 50, CancellationToken.None);

        Assert.Single(claimed);
        Assert.DoesNotContain(whileLeased.ToSend, n => n.Id == record.Id);
        Assert.Contains(afterLease.ToSend, n => n.Id == record.Id);
    }

    [PostgresFact]
    public async Task A_lease_that_expires_after_the_provider_call_started_becomes_Uncertain_not_a_second_send()
    {
        var now = DateTime.UtcNow;
        var record = await AcceptAsync(Record(Unique("interrupted"), at: now.AddSeconds(-5)), stateAt: now.AddSeconds(-5));
        var claimed = await Store.ClaimDueAsync(now, TimeSpan.FromMinutes(2), 50, CancellationToken.None);
        Assert.Contains(claimed.ToSend, n => n.Id == record.Id);
        Assert.True(await Store.BeginSendAsync(record.Id, now, CancellationToken.None));

        var recovered = await Store.ClaimDueAsync(now.AddMinutes(3), TimeSpan.FromMinutes(2), 50, CancellationToken.None);

        Assert.DoesNotContain(recovered.ToSend, n => n.Id == record.Id);
        Assert.True(recovered.Interrupted >= 1);
        await using var db = Db.CreateRuntimeContext();
        Assert.Equal(NotificationStatus.Uncertain, (await db.NotificationDispatchStates.AsNoTracking().SingleAsync(s => s.NotificationId == record.Id)).Status);
        var attempt = await db.NotificationAttempts.AsNoTracking().SingleAsync(a => a.NotificationId == record.Id);
        Assert.Equal(DeliveryAttemptOutcome.Uncertain, attempt.Outcome);
    }

    [PostgresFact]
    public async Task Completing_an_attempt_appends_the_fact_and_advances_the_state_together()
    {
        var now = DateTime.UtcNow;
        var record = await AcceptAsync(Record(Unique("complete"), at: now.AddSeconds(-5)), stateAt: now.AddSeconds(-5));
        await Store.ClaimDueAsync(now, TimeSpan.FromMinutes(2), 50, CancellationToken.None);

        await Store.CompleteAttemptAsync(
            new AttemptCompletion(record.Id, 1, now, now.AddSeconds(1), DeliveryAttemptOutcome.RetryableFailure, "Http503", null, NotificationStatus.Retrying, now.AddSeconds(30)),
            CancellationToken.None);

        await using var db = Db.CreateRuntimeContext();
        var state = await db.NotificationDispatchStates.AsNoTracking().SingleAsync(s => s.NotificationId == record.Id);
        Assert.Equal(NotificationStatus.Retrying, state.Status);
        Assert.Equal(1, state.AttemptCount);
        Assert.Null(state.LeaseExpiresAtUtc);
        Assert.Equal("Http503", state.FailureCode);
        Assert.Single(await db.NotificationAttempts.AsNoTracking().Where(a => a.NotificationId == record.Id).ToListAsync());
    }

    [PostgresFact]
    public async Task History_keyset_paging_visits_every_row_once_in_a_stable_order_with_filters()
    {
        var recipient = Unique("pager");
        var same = Start.AddDays(10);
        for (var i = 0; i < 9; i++)
        {
            await AcceptAsync(Record(Unique("page"), recipient, i < 6 ? same : same.AddMinutes(i), eventType: i % 3 == 0 ? NotificationEventType.FeedbackWindowOpened : Event), NotificationStatus.Failed);
        }

        await using var db = Db.CreateRuntimeContext();
        var store = new NotificationAdminStore(db);

        var expected = await db.NotificationRecords.AsNoTracking().Where(r => r.RecipientUserId == recipient)
            .OrderByDescending(r => r.RequestedAtUtc).ThenByDescending(r => r.Id).Select(r => r.Id).ToListAsync();
        var collected = new List<Guid>();
        DateTime? afterAt = null;
        Guid? afterId = null;
        while (true)
        {
            var page = await store.SearchAsync(new NotificationSearch(recipient, null, null, null, null, 4, afterAt, afterId), CancellationToken.None);
            collected.AddRange(page.Take(3).Select(p => p.Id));
            if (page.Count <= 3)
            {
                break;
            }

            (afterAt, afterId) = (page[2].RequestedAtUtc, page[2].Id);
        }

        var filtered = await store.SearchAsync(
            new NotificationSearch(recipient, NotificationEventType.FeedbackWindowOpened, NotificationStatus.Failed, same, same.AddDays(1), 50, null, null), CancellationToken.None);

        Assert.Equal(expected, collected);
        Assert.Equal(9, collected.Distinct().Count());
        Assert.Equal(3, filtered.Count);
        Assert.All(filtered, f => Assert.Equal(NotificationEventType.FeedbackWindowOpened, f.EventType));
    }

    [PostgresFact]
    public async Task The_runtime_role_can_add_and_read_history_but_never_update_or_delete_records_revisions_or_attempts()
    {
        var record = await AcceptAsync(Record(Unique("append"), at: DateTime.UtcNow));
        await Store.CompleteAttemptAsync(
            new AttemptCompletion(record.Id, 1, DateTime.UtcNow, DateTime.UtcNow, DeliveryAttemptOutcome.Accepted, null, DateTime.UtcNow, NotificationStatus.Sent, null),
            CancellationToken.None);
        var revisionId = NotificationTemplateCatalog.InitialRevisionId(Event);

        await using var runtime = await Db.OpenAsync(Db.RuntimeConnectionString);
        foreach (var (table, key, id) in new[]
        {
            ("NotificationRecords", "Id", record.Id.ToString()),
            ("NotificationTemplateRevisions", "Id", revisionId.ToString()),
            ("NotificationAttempts", "NotificationId", record.Id.ToString()),
        })
        {
            var update = await SqlStateAsync(() => PostgresTestDatabase.ExecuteAsync(runtime, table switch
            {
                "NotificationRecords" => $"UPDATE \"{table}\" SET \"Subject\" = 'changed' WHERE \"{key}\" = '{id}'",
                "NotificationTemplateRevisions" => $"UPDATE \"{table}\" SET \"Subject\" = 'changed' WHERE \"{key}\" = '{id}'",
                _ => $"UPDATE \"{table}\" SET \"FailureCode\" = 'changed' WHERE \"{key}\" = '{id}'",
            }));
            var delete = await SqlStateAsync(() => PostgresTestDatabase.ExecuteAsync(runtime, $"DELETE FROM \"{table}\" WHERE \"{key}\" = '{id}'"));
            var truncate = await SqlStateAsync(() => PostgresTestDatabase.ExecuteAsync(runtime, $"TRUNCATE \"{table}\""));
            Assert.Equal("42501", update);
            Assert.Equal("42501", delete);
            Assert.Equal("42501", truncate);
        }

        await using var db = Db.CreateRuntimeContext();
        Assert.Equal("S", (await db.NotificationRecords.AsNoTracking().SingleAsync(r => r.Id == record.Id)).Subject);
    }

    [PostgresFact]
    public async Task The_runtime_role_can_still_update_the_dispatch_state_the_template_pointer_and_the_transient_tables()
    {
        var record = await AcceptAsync(Record(Unique("mutable"), at: DateTime.UtcNow));
        await using var runtime = await Db.OpenAsync(Db.RuntimeConnectionString);

        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(runtime,
            $"UPDATE \"NotificationDispatchStates\" SET \"Status\" = 'Failed' WHERE \"NotificationId\" = '{record.Id}'"));
        Assert.Equal(1, await PostgresTestDatabase.ExecuteAsync(runtime,
            $"UPDATE \"NotificationTemplates\" SET \"Enabled\" = true WHERE \"Id\" = '{NotificationTemplateCatalog.TemplateId(NotificationEventType.PasswordReset)}'"));

        await Store.AddRecoveryRequestAsync(PasswordRecoveryRequest.Create("protected", DateTime.UtcNow, TimeSpan.FromHours(1)), CancellationToken.None);
        Assert.True(await Store.DeleteExpiredRecoveryRequestsAsync(DateTime.UtcNow.AddHours(2), CancellationToken.None) >= 1);
    }

    [PostgresFact]
    public async Task Removing_an_account_leaves_its_history_and_a_referenced_revision_cannot_be_removed_by_the_schema_owner()
    {
        var recipient = Unique("account");
        await AcceptAsync(Record(Unique("survive"), recipient, DateTime.UtcNow));

        await using var ddl = await Db.OpenAsync(Db.DdlConnectionString);
        // No foreign key to accounts: removing an account that does not exist as a row, or any account, never touches history.
        await PostgresTestDatabase.ExecuteAsync(ddl, $"DELETE FROM \"AspNetUsers\" WHERE \"Id\" = '{recipient}'");
        var revision = await SqlStateAsync(() => PostgresTestDatabase.ExecuteAsync(ddl,
            $"DELETE FROM \"NotificationTemplateRevisions\" WHERE \"Id\" = '{NotificationTemplateCatalog.InitialRevisionId(Event)}'"));

        await using var db = Db.CreateRuntimeContext();
        Assert.Single(await db.NotificationRecords.AsNoTracking().Where(r => r.RecipientUserId == recipient).ToListAsync());
        Assert.Contains(revision, new[] { "23001", "23503" });
    }

    [PostgresFact]
    public async Task A_reminder_schedule_is_unique_per_window_occurrence_and_recipient()
    {
        var occurrence = Unique("window");
        await using (var db = Db.CreateRuntimeContext())
        {
            db.FeedbackReminderSchedules.Add(FeedbackReminderSchedule.Create(occurrence, "student-1", Start, 1, Start, Start));
            await db.SaveChangesAsync();
        }

        await using var duplicate = Db.CreateRuntimeContext();
        duplicate.FeedbackReminderSchedules.Add(FeedbackReminderSchedule.Create(occurrence, "student-1", Start, 2, Start, Start));
        Assert.Equal("23505", await SqlStateAsync(() => duplicate.SaveChangesAsync()));
    }
}
