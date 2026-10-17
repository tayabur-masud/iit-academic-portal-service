using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// US1: accepted work is durable, delivered in the background, retried a bounded number of times, never duplicated by a replay, and
/// ends in an honest outcome. The originating action never waits for, or fails because of, delivery.
/// </summary>
public sealed class NotificationDispatchTests : NotificationTestBase
{
    [Fact]
    public async Task An_accepted_request_is_stored_pending_with_the_ordinary_content_fixed_and_is_then_delivered()
    {
        var student = await Host.CreateRecipientAsync();
        var email = await Host.EmailOfAsync(student);

        var result = await Host.RequestAsync(Results("pub-1", student));

        Assert.Equal(NotificationRequestStatus.Accepted, result.Status);
        Assert.Equal(1, result.Accepted);
        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(email, record.Destination);
        Assert.Equal("Results published for Algorithms", record.Subject);
        Assert.Contains("Results for Algorithms (Fall 2026) have been published.", record.Body);
        Assert.Contains("https://portal.test", record.Body);
        Assert.False(record.ContentWithheld);
        Assert.Equal(NotificationStatus.Pending, (await Host.StateAsync(record.Id)).Status);
        Assert.Empty(Host.Emails.Messages);

        await Host.FlushNotificationsAsync();

        var message = Assert.Single(Host.Emails.Messages);
        Assert.Equal(email, message.To);
        Assert.Equal(record.Subject, message.Subject);
        Assert.Equal(record.Body, message.Body);
        var state = await Host.StateAsync(record.Id);
        Assert.Equal(NotificationStatus.Sent, state.Status);
        Assert.NotNull(state.SentAtUtc);
        var attempt = Assert.Single(await Host.AttemptsAsync(record.Id));
        Assert.Equal(DeliveryAttemptOutcome.Accepted, attempt.Outcome);
        Assert.Equal(1, attempt.AttemptNumber);
    }

    [Fact]
    public async Task Each_recipient_is_addressed_separately_and_sees_no_other_address()
    {
        var first = await Host.CreateRecipientAsync();
        var second = await Host.CreateRecipientAsync();

        await Host.RequestAsync(Results("pub-2", first, second));
        await Host.FlushNotificationsAsync();

        Assert.Equal(2, Host.Emails.Messages.Count);
        var firstEmail = await Host.EmailOfAsync(first);
        var secondEmail = await Host.EmailOfAsync(second);
        foreach (var message in Host.Emails.Messages)
        {
            var other = message.To == firstEmail ? secondEmail : firstEmail;
            Assert.DoesNotContain(other, message.Body);
            Assert.DoesNotContain(other, message.Subject);
        }
    }

    [Fact]
    public async Task A_provider_outage_is_retried_with_doubling_delays_and_succeeds_after_recovery_without_repeating_the_action()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-3", student));
        Host.Outage();

        await Host.FlushNotificationsAsync();
        var afterFirst = await Host.StateAsync(id);
        Host.Advance(TimeSpan.FromSeconds(6));
        await Host.FlushNotificationsAsync();
        var afterSecond = await Host.StateAsync(id);

        Assert.Equal(NotificationStatus.Retrying, afterFirst.Status);
        Assert.Equal(1, afterFirst.AttemptCount);
        Assert.Equal("Http503", afterFirst.FailureCode);
        Assert.Equal(NotificationStatus.Retrying, afterSecond.Status);
        Assert.Equal(2, afterSecond.AttemptCount);
        Assert.InRange((afterSecond.NextAttemptAtUtc!.Value - afterSecond.UpdatedAtUtc).TotalSeconds, 9.9, 10.1);

        Host.Recover();
        Host.Advance(TimeSpan.FromSeconds(11));
        await Host.FlushNotificationsAsync();

        Assert.Equal(NotificationStatus.Sent, (await Host.StateAsync(id)).Status);
        Assert.Single(Host.Emails.Messages);
        var attempts = await Host.AttemptsAsync(id);
        Assert.Equal([DeliveryAttemptOutcome.RetryableFailure, DeliveryAttemptOutcome.RetryableFailure, DeliveryAttemptOutcome.Accepted], attempts.Select(a => a.Outcome));
        Assert.Equal([1, 2, 3], attempts.Select(a => a.AttemptNumber));
        Assert.Single(await Host.RecordsAsync());
    }

    [Fact]
    public async Task Retries_are_bounded_and_exhausting_them_makes_the_notification_Failed_and_raises_the_alarm()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-4", student));
        Host.Outage();

        for (var i = 0; i < 10; i++)
        {
            await Host.FlushNotificationsAsync();
            Host.Advance(TimeSpan.FromMinutes(2));
        }

        var state = await Host.StateAsync(id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("RetriesExhausted", state.FailureCode);
        Assert.Equal(4, state.AttemptCount);
        Assert.Equal(4, Host.Emails.Attempts);
        Assert.Equal(4, (await Host.AttemptsAsync(id)).Count);
        Assert.Null(state.NextAttemptAtUtc);
        Assert.Single(Host.Signals.Exhausted);

        // Nothing is retried again, however long it waits.
        Host.Advance(TimeSpan.FromDays(1));
        await Host.FlushNotificationsAsync();
        Assert.Equal(4, Host.Emails.Attempts);
    }

    [Fact]
    public async Task A_permanent_rejection_is_terminal_after_one_attempt()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-5", student));
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");

        await Host.FlushNotificationsAsync();
        Host.Advance(TimeSpan.FromHours(1));
        await Host.FlushNotificationsAsync();

        var state = await Host.StateAsync(id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("Http400", state.FailureCode);
        Assert.Equal(1, Host.Emails.Attempts);
        var attempt = Assert.Single(await Host.AttemptsAsync(id));
        Assert.Equal(DeliveryAttemptOutcome.PermanentFailure, attempt.Outcome);
        Assert.Empty(Host.Signals.Exhausted);
    }

    [Fact]
    public async Task An_ambiguous_provider_outcome_is_Uncertain_never_Sent_and_never_retried()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-6", student));
        Host.Emails.Behavior = _ => NotificationTestHost.Uncertain("Timeout");

        await Host.FlushNotificationsAsync();
        Host.Recover();
        Host.Advance(TimeSpan.FromHours(1));
        await Host.FlushNotificationsAsync();

        var state = await Host.StateAsync(id);
        Assert.Equal(NotificationStatus.Uncertain, state.Status);
        Assert.Null(state.SentAtUtc);
        Assert.Equal(1, Host.Emails.Attempts);
        Assert.Empty(Host.Emails.Messages);
        Assert.Equal(DeliveryAttemptOutcome.Uncertain, Assert.Single(await Host.AttemptsAsync(id)).Outcome);
    }

    [Fact]
    public async Task A_restart_before_the_provider_call_started_delivers_after_the_lease_expires()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-7", student));

        // A worker claimed it and died before calling the provider.
        await UpdateStateAsync(id, s => s.LeaseExpiresAtUtc = Host.Clock.GetUtcNow().UtcDateTime.AddMinutes(2));
        await Host.FlushNotificationsAsync();
        Assert.Empty(Host.Emails.Messages);

        Host.Advance(TimeSpan.FromMinutes(3));
        await Host.FlushNotificationsAsync();

        Assert.Single(Host.Emails.Messages);
        Assert.Equal(NotificationStatus.Sent, (await Host.StateAsync(id)).Status);
    }

    [Fact]
    public async Task A_restart_after_the_provider_call_started_is_Uncertain_and_not_sent_again()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-8", student));

        // A worker started the provider call and died: whether the provider accepted it cannot be known.
        var now = Host.Clock.GetUtcNow().UtcDateTime;
        await UpdateStateAsync(id, s =>
        {
            s.LeaseExpiresAtUtc = now.AddMinutes(2);
            s.SendStartedAtUtc = now;
        });
        Host.Advance(TimeSpan.FromMinutes(3));

        await Host.FlushNotificationsAsync();

        var state = await Host.StateAsync(id);
        Assert.Equal(NotificationStatus.Uncertain, state.Status);
        Assert.Empty(Host.Emails.Messages);
        Assert.Equal(0, Host.Emails.Attempts);
        var attempt = Assert.Single(await Host.AttemptsAsync(id));
        Assert.Equal(DeliveryAttemptOutcome.Uncertain, attempt.Outcome);
        Assert.Equal("WorkerInterrupted", attempt.FailureCode);
    }

    [Fact]
    public async Task Replaying_the_same_occurrence_creates_no_extra_logical_notification_before_or_after_delivery()
    {
        var student = await Host.CreateRecipientAsync();
        var request = Results("pub-9", student);

        var first = await Host.RequestAsync(request);
        var beforeDelivery = await Host.RequestAsync(request);
        await Host.FlushNotificationsAsync();
        var afterDelivery = await Host.RequestAsync(request);
        await Host.FlushNotificationsAsync();

        Assert.Equal(1, first.Accepted);
        Assert.Equal(1, beforeDelivery.Duplicates);
        Assert.Equal(1, afterDelivery.Duplicates);
        Assert.Single(await Host.RecordsAsync());
        Assert.Single(Host.Emails.Messages);
    }

    [Fact]
    public async Task A_replay_with_a_new_recipient_adds_only_that_recipient()
    {
        var first = await Host.CreateRecipientAsync();
        var second = await Host.CreateRecipientAsync();
        await Host.RequestAsync(Results("pub-10", first));

        var replay = await Host.RequestAsync(Results("pub-10", first, second));

        Assert.Equal(1, replay.Accepted);
        Assert.Equal(1, replay.Duplicates);
        Assert.Equal(2, (await Host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task A_recipient_with_no_address_is_recorded_as_Failed_with_a_reason_and_nothing_is_sent()
    {
        var student = await Host.CreateRecipientAsync();
        await Host.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == student);
            user.Email = null;
            user.NormalizedEmail = null;
            await db.SaveChangesAsync();
        });

        var result = await Host.RequestAsync(Results("pub-11", student));
        await Host.FlushNotificationsAsync();

        Assert.Equal(1, result.Failed);
        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Null(record.Destination);
        var state = await Host.StateAsync(record.Id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("NoDestination", state.FailureCode);
        Assert.Empty(await Host.AttemptsAsync(record.Id));
        Assert.Equal(0, Host.Emails.Attempts);
    }

    [Fact]
    public async Task A_disabled_ordinary_template_suppresses_new_requests_and_enabling_it_again_replays_nothing()
    {
        var student = await Host.CreateRecipientAsync();
        await SetEnabledAsync(NotificationEventType.ResultsPublished, false);

        var result = await Host.RequestAsync(Results("pub-12", student));
        await SetEnabledAsync(NotificationEventType.ResultsPublished, true);
        await Host.FlushNotificationsAsync();

        Assert.Equal(1, result.Suppressed);
        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationStatus.Suppressed, (await Host.StateAsync(record.Id)).Status);
        Assert.Null(record.Body);
        Assert.Empty(Host.Emails.Messages);
        Assert.Equal(0, Host.Emails.Attempts);
    }

    [Fact]
    public async Task An_invalid_request_stores_nothing()
    {
        var student = await Host.CreateRecipientAsync();

        var unsupportedContext = await Host.RequestAsync(new NotificationEventRequest(
            NotificationEventType.ResultsPublished, "pub-13", [student],
            new Dictionary<string, string> { ["courseName"] = "A", ["termName"] = "T", ["feedbackText"] = "great" }));
        var missingValue = await Host.RequestAsync(new NotificationEventRequest(
            NotificationEventType.ResultsPublished, "pub-14", [student], new Dictionary<string, string> { ["courseName"] = "A" }));
        var noOccurrence = await Host.RequestAsync(Results(" ", student));
        var noRecipients = await Host.RequestAsync(Results("pub-15"));

        Assert.All([unsupportedContext, missingValue, noOccurrence, noRecipients], r => Assert.Equal(NotificationRequestStatus.Invalid, r.Status));
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task A_store_failure_raises_a_secret_free_alarm_keeps_the_action_successful_and_a_replay_recovers_exactly_one()
    {
        using var admin = Host.CreatePortalClient();
        var adminEmail = PortalApiFactory.UniqueEmail("admin");
        await Host.CreateUserAsync(adminEmail, IitAcademicPortal.Domain.Identity.PortalRoles.Admin);
        await admin.SignInSuccessfullyAsync(adminEmail);
        var teacherEmail = PortalApiFactory.UniqueEmail("teacher");
        Host.Fault.FailAccept = true;

        // The originating action: an Admin adds a teacher while notification work cannot be retained.
        var (status, body) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Outage Teacher", email = teacherEmail });

        Assert.Equal(HttpStatusCode.Created, status);
        var userId = body.GetProperty("userId").GetString()!;
        var signal = Assert.Single(Host.Signals.PersistenceFailures);
        Assert.StartsWith($"AccountCredentialSetup|{userId}|1|", signal);
        Assert.DoesNotContain(teacherEmail, signal);
        Assert.Empty(await Host.RecordsAsync());

        // Reconciliation: the owner replays the same stable occurrence once the store is back.
        Host.Fault.FailAccept = false;
        var request = new NotificationEventRequest(NotificationEventType.AccountCredentialSetup, userId, [userId]);
        var replay = await Host.RequestAsync(request);
        var again = await Host.RequestAsync(request);

        Assert.Equal(1, replay.Accepted);
        Assert.Equal(1, again.Duplicates);
        Assert.Single(await Host.RecordsAsync());
    }

    [Fact]
    public async Task History_and_attempts_are_unchanged_by_later_delivery_activity_for_other_notifications()
    {
        var first = await Host.CreateRecipientAsync();
        var firstId = await AcceptOneAsync(Results("pub-16", first));
        await Host.FlushNotificationsAsync();
        var recordBefore = (await Host.RecordsAsync()).Single();
        var attemptsBefore = await Host.AttemptsAsync(firstId);

        var second = await Host.CreateRecipientAsync();
        await Host.RequestAsync(Results("pub-17", second));
        Host.Outage();
        await Host.FlushNotificationsAsync();

        var recordAfter = (await Host.RecordsAsync()).First(r => r.Id == firstId);
        Assert.Equal(recordBefore.Subject, recordAfter.Subject);
        Assert.Equal(recordBefore.Destination, recordAfter.Destination);
        Assert.Equal(recordBefore.RequestedAtUtc, recordAfter.RequestedAtUtc);
        var attemptsAfter = await Host.AttemptsAsync(firstId);
        Assert.Equal(attemptsBefore.Select(a => (a.AttemptNumber, a.Outcome, a.CompletedAtUtc)), attemptsAfter.Select(a => (a.AttemptNumber, a.Outcome, a.CompletedAtUtc)));
    }

    [Fact]
    public async Task Provider_failure_codes_never_carry_the_address_or_content()
    {
        var student = await Host.CreateRecipientAsync();
        var email = await Host.EmailOfAsync(student);
        var id = await AcceptOneAsync(Results("pub-18", student));
        Host.Emails.Behavior = message => new InvalidOperationException($"Mailbox {message.To} rejected: {message.Body}");

        await Host.FlushNotificationsAsync();

        var attempt = Assert.Single(await Host.AttemptsAsync(id));
        Assert.Equal("InvalidOperationException", attempt.FailureCode);
        var state = await Host.StateAsync(id);
        NotificationTestHost.AssertNoSecret([attempt.FailureCode!, state.FailureCode!], email, "Algorithms");
        NotificationTestHost.AssertNoSecret(Host.Logs.Messages, email, "Algorithms");
    }

    private async Task SetEnabledAsync(NotificationEventType eventType, bool enabled)
    {
        await Host.WithDbAsync(async db =>
        {
            var template = await db.NotificationTemplates.SingleAsync(t => t.EventType == eventType);
            var revision = await db.NotificationTemplateRevisions.SingleAsync(r => r.Id == template.CurrentRevisionId);
            template.Publish(revision, enabled);
            await db.SaveChangesAsync();
        });
    }
}
