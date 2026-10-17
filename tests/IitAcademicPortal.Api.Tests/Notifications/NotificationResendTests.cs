using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// US3: an Admin re-sends a Failed notification as a new linked execution. The failed record and its attempts never change, a repeated
/// confirmation never makes a second execution, only eligible records qualify, and a security message gets a fresh link at delivery
/// while history stays content-free.
/// </summary>
public sealed partial class NotificationResendTests : NotificationTestBase
{
    [GeneratedRegex(@"proof=([^\s&]+)")]
    private static partial Regex ProofPattern();

    private static Dictionary<string, string> Key(string key = "confirm-1") => new() { ["Idempotency-Key"] = key };

    private Task<(HttpStatusCode Status, JsonElement Body)> Resend(PortalClient admin, Guid id, string? key = "confirm-1") =>
        SendAsync(admin, HttpMethod.Post, $"{History}/{id}/resends", headers: key is null ? null : Key(key));

    /// <summary>An ordinary notification that failed permanently.</summary>
    private async Task<(Guid Id, string UserId, string Email)> FailedAsync(string occurrence = "pub-40")
    {
        var student = await Host.CreateRecipientAsync();
        var email = await Host.EmailOfAsync(student);
        var id = await AcceptOneAsync(Results(occurrence, student));
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");
        await Host.FlushNotificationsAsync();
        Host.Recover();
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(id)).Status);
        return (id, student, email);
    }

    [Fact]
    public async Task A_failed_ordinary_notification_is_re_sent_as_a_new_linked_execution_with_the_recorded_destination_and_content()
    {
        using var admin = await AdminAsync();
        var (id, userId, email) = await FailedAsync();
        var predecessor = (await Host.RecordsAsync()).Single(r => r.Id == id);
        var attemptsBefore = await Host.AttemptsAsync(id);

        var (status, body) = await Resend(admin, id);

        Assert.Equal(HttpStatusCode.Accepted, status);
        var newId = body.GetProperty("notification").GetProperty("id").GetGuid();
        Assert.NotEqual(id, newId);
        Assert.Equal(id, body.GetProperty("notification").GetProperty("parentNotificationId").GetGuid());
        Assert.Equal("Pending", body.GetProperty("notification").GetProperty("status").GetString());

        // Background: nothing is sent until the worker runs, and it uses the recorded destination and exact content.
        Assert.Empty(Host.Emails.Messages);
        await Host.FlushNotificationsAsync();
        var message = Assert.Single(Host.Emails.Messages);
        Assert.Equal(email, message.To);
        Assert.Equal(predecessor.Subject, message.Subject);
        Assert.Equal(predecessor.Body, message.Body);

        var child = (await Host.RecordsAsync()).Single(r => r.Id == newId);
        Assert.Equal(predecessor.OccurrenceId, child.OccurrenceId);
        Assert.Equal(userId, child.RecipientUserId);
        Assert.Equal(predecessor.TemplateRevisionId, child.TemplateRevisionId);
        Assert.Equal(NotificationStatus.Sent, (await Host.StateAsync(newId)).Status);

        // The predecessor is untouched: still Failed, with exactly the attempts it had.
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(id)).Status);
        var attemptsAfter = await Host.AttemptsAsync(id);
        Assert.Equal(attemptsBefore.Select(a => (a.AttemptNumber, a.Outcome, a.FailureCode, a.CompletedAtUtc)), attemptsAfter.Select(a => (a.AttemptNumber, a.Outcome, a.FailureCode, a.CompletedAtUtc)));
        Assert.Equal(predecessor.Subject, (await Host.RecordsAsync()).Single(r => r.Id == id).Subject);
        Assert.Equal(1, (await Host.AttemptsAsync(newId)).Single().AttemptNumber);
    }

    [Fact]
    public async Task The_same_confirmation_repeated_returns_the_same_execution_and_makes_no_second_one()
    {
        using var admin = await AdminAsync();
        var (id, _, _) = await FailedAsync();

        var (first, firstBody) = await Resend(admin, id, "double-click");
        var (second, secondBody) = await Resend(admin, id, "double-click");
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Accepted, first);
        Assert.Equal(HttpStatusCode.Accepted, second);
        Assert.Equal(firstBody.GetProperty("notification").GetProperty("id").GetGuid(), secondBody.GetProperty("notification").GetProperty("id").GetGuid());
        Assert.Equal(2, (await Host.RecordsAsync()).Count);
        Assert.Single(Host.Emails.Messages);
        Assert.Single(await Host.AuditEventsAsync("notification.resend.requested"));
    }

    [Fact]
    public async Task A_different_confirmation_while_the_first_resend_is_pending_is_refused()
    {
        using var admin = await AdminAsync();
        var (id, _, _) = await FailedAsync();
        await Resend(admin, id, "first");

        var (status, problem) = await Resend(admin, id, "second");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("ResendInProgress", problem.GetProperty("code").GetString());
        Assert.Equal(2, (await Host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task Once_a_resend_has_been_accepted_by_the_provider_the_failed_original_cannot_be_re_sent_again()
    {
        using var admin = await AdminAsync();
        var (id, _, _) = await FailedAsync();
        await Resend(admin, id, "first");
        await Host.FlushNotificationsAsync();

        var (status, problem) = await Resend(admin, id, "second");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("AlreadyResent", problem.GetProperty("code").GetString());
        Assert.Single(Host.Emails.Messages);
    }

    [Fact]
    public async Task A_failed_resend_can_itself_be_re_sent()
    {
        using var admin = await AdminAsync();
        var (id, _, email) = await FailedAsync();
        var (_, first) = await Resend(admin, id, "first");
        var childId = first.GetProperty("notification").GetProperty("id").GetGuid();
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");
        await Host.FlushNotificationsAsync();
        Host.Recover();
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(childId)).Status);

        var (status, second) = await Resend(admin, childId, "again");
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(childId, second.GetProperty("notification").GetProperty("parentNotificationId").GetGuid());
        Assert.Equal(email, Assert.Single(Host.Emails.Messages).To);
        Assert.Equal(3, (await Host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task Only_a_Failed_notification_qualifies_and_each_other_status_has_a_safe_reason()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();

        var pending = await AcceptOneAsync(Results("p1", student));
        var retrying = await AcceptOneAsync(Results("p2", student));
        var sent = await AcceptOneAsync(Results("p3", student));
        var suppressed = await AcceptOneAsync(Results("p4", student));
        var uncertain = await AcceptOneAsync(Results("p5", student));
        foreach (var (id, status) in new[] { (retrying, NotificationStatus.Retrying), (sent, NotificationStatus.Sent), (suppressed, NotificationStatus.Suppressed), (uncertain, NotificationStatus.Uncertain) })
        {
            await UpdateStateAsync(id, s =>
            {
                s.Status = status;
                s.NextAttemptAtUtc = status == NotificationStatus.Retrying ? DateTime.UtcNow.AddHours(1) : null;
            });
        }

        await UpdateStateAsync(pending, s => s.NextAttemptAtUtc = DateTime.UtcNow.AddHours(1));
        var expected = new (Guid Id, string Code)[]
        {
            (pending, "InProgress"), (retrying, "InProgress"), (sent, "AlreadySent"), (suppressed, "Suppressed"), (uncertain, "DeliveryUncertain"),
        };

        foreach (var (id, code) in expected)
        {
            var (status, problem) = await Resend(admin, id);
            Assert.Equal(HttpStatusCode.Conflict, status);
            Assert.Equal(code, problem.GetProperty("code").GetString());
            Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("detail").GetString()));
        }

        Assert.Equal(5, (await Host.RecordsAsync()).Count);
        Assert.Empty(await Host.AuditEventsAsync("notification.resend.requested"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    public async Task A_valid_idempotency_key_is_required(string? key)
    {
        using var admin = await AdminAsync();
        var (id, _, _) = await FailedAsync();

        var (status, problem) = await Resend(admin, id, key);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "idempotencyKey"));
        Assert.Single(await Host.RecordsAsync());
    }

    [Fact]
    public async Task An_unknown_notification_is_not_found()
    {
        using var admin = await AdminAsync();

        var (status, _) = await Resend(admin, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task A_resend_is_refused_when_the_account_is_gone_deactivated_or_the_address_changed()
    {
        using var admin = await AdminAsync();
        var (gone, goneUser, _) = await FailedAsync("g1");
        var (deactivated, deactivatedUser, _) = await FailedAsync("g2");
        var (moved, movedUser, _) = await FailedAsync("g3");
        await Host.WithDbAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == goneUser));
            (await db.Users.SingleAsync(u => u.Id == deactivatedUser)).AccountStatus = TeacherAccountStatus.Deactivated;
            var user = await db.Users.SingleAsync(u => u.Id == movedUser);
            user.Email = "moved@iit.test";
            user.NormalizedEmail = "MOVED@IIT.TEST";
            await db.SaveChangesAsync();
        });

        var (goneStatus, goneProblem) = await Resend(admin, gone);
        var (deactivatedStatus, deactivatedProblem) = await Resend(admin, deactivated);
        var (movedStatus, movedProblem) = await Resend(admin, moved);

        Assert.Equal(HttpStatusCode.Conflict, goneStatus);
        Assert.Equal("RecipientIneligible", goneProblem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, deactivatedStatus);
        Assert.Equal("RecipientIneligible", deactivatedProblem.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, movedStatus);
        Assert.Equal("DestinationChanged", movedProblem.GetProperty("code").GetString());
        Assert.Equal(3, (await Host.RecordsAsync()).Count);
        Assert.DoesNotContain("moved@iit.test", movedProblem.GetRawText());
    }

    [Fact]
    public async Task A_record_that_never_had_an_address_cannot_be_re_sent()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await Host.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == student);
            user.Email = null;
            user.NormalizedEmail = null;
            await db.SaveChangesAsync();
        });
        var id = await AcceptOneAsync(Results("none-1", student));

        var (status, problem) = await Resend(admin, id);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("NoDestination", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_failed_password_reset_is_re_sent_with_a_fresh_working_link_and_history_stays_content_free()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await Host.CreateUserAsync(email, PortalRoles.Student);
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");
        using var client = Host.CreatePortalClient();
        Assert.Equal(HttpStatusCode.Accepted, (await PasswordRecoveryTests.RequestResetAsync(client, email)).StatusCode);
        await Host.FlushNotificationsAsync();
        Host.Recover();
        var failed = (await Host.RecordsAsync()).Single();
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(failed.Id)).Status);
        Assert.Empty(Host.Emails.Messages);

        var (status, body) = await Resend(admin, failed.Id);
        var childId = body.GetProperty("notification").GetProperty("id").GetGuid();
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.True(body.GetProperty("notification").GetProperty("contentWithheld").GetBoolean());
        var message = Assert.Single(Host.Emails.Messages);
        Assert.Equal(email, message.To);
        var proof = Uri.UnescapeDataString(ProofPattern().Match(message.Body).Groups[1].Value);
        Assert.Equal(HttpStatusCode.NoContent,
            (await PasswordRecoveryTests.CompleteResetAsync(client, email, proof, "Replacement-pass2")).StatusCode);

        // History holds no content, link, or proof for either execution.
        var child = (await Host.RecordsAsync()).Single(r => r.Id == childId);
        Assert.Null(child.Subject);
        Assert.Null(child.Body);
        Assert.True(child.ContentWithheld);
        Assert.Equal(userId, child.RecipientUserId);
        var (_, detail) = await SendAsync(admin, HttpMethod.Get, $"{History}/{childId}");
        Assert.DoesNotContain(proof, detail.GetRawText());
        Assert.DoesNotContain(proof, Host.Logs.Everything);
    }

    [Fact]
    public async Task A_security_resend_is_refused_when_the_owning_workflow_cannot_authorize_it()
    {
        using var admin = await AdminAsync();

        // Student invitation has no producer and no secure-link owner (account setup uses the common AccountCredentialSetup event), so
        // its provider is the unavailable stand-in.
        var studentId = await Host.CreateRecipientAsync();
        var invitationId = await AcceptOneAsync(new NotificationEventRequest(NotificationEventType.StudentInvitation, $"invite-{studentId}", [studentId]));
        await Host.FlushNotificationsAsync();
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(invitationId)).Status);

        var (status, problem) = await Resend(admin, invitationId);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("OwnerWorkflowUnavailable", problem.GetProperty("code").GetString());
        Assert.Contains("owning", problem.GetProperty("detail").GetString());
        Assert.Single(await Host.RecordsAsync());
    }

    [Fact]
    public async Task A_setup_email_resend_is_refused_once_the_account_is_no_longer_pending()
    {
        using var admin = await AdminAsync();
        var teacherEmail = PortalApiFactory.UniqueEmail("teacher");
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");
        var (created, _) = await SendAsync(admin, HttpMethod.Post, "/api/admin/teachers", new { displayName = "Awaiting Link", email = teacherEmail });
        Assert.Equal(HttpStatusCode.Created, created);
        await Host.FlushNotificationsAsync();
        Host.Recover();
        var setup = (await Host.RecordsAsync()).Single();
        Assert.Equal(NotificationStatus.Failed, (await Host.StateAsync(setup.Id)).Status);
        await Host.SetAccountStatusAsync(teacherEmail, TeacherAccountStatus.Active);

        var (status, problem) = await Resend(admin, setup.Id);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("AccountNotPending", problem.GetProperty("code").GetString());
        Assert.Single(await Host.RecordsAsync());
        Assert.Empty(Host.Emails.Messages);
    }

    [Fact]
    public async Task A_security_resend_is_refused_when_the_address_no_longer_belongs_to_the_recipient()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await Host.CreateUserAsync(email, PortalRoles.Student);
        Host.Emails.Behavior = _ => NotificationTestHost.Permanent("Http400");
        using var client = Host.CreatePortalClient();
        await PasswordRecoveryTests.RequestResetAsync(client, email);
        await Host.FlushNotificationsAsync();
        Host.Recover();
        await Host.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == userId);
            user.Email = "other@iit.test";
            user.NormalizedEmail = "OTHER@IIT.TEST";
            await db.SaveChangesAsync();
        });

        var (status, problem) = await Resend(admin, (await Host.RecordsAsync()).Single().Id);

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("DestinationChanged", problem.GetProperty("code").GetString());
        Assert.Empty(Host.Emails.Messages);
    }

    [Fact]
    public async Task An_accepted_resend_is_audited_once_with_safe_details_and_a_refusal_is_not()
    {
        using var admin = await AdminAsync();
        var (id, userId, email) = await FailedAsync();
        await Resend(admin, id, "ok");
        await Resend(admin, id, "other");

        var audit = Assert.Single(await Host.AuditEventsAsync("notification.resend.requested"));

        Assert.Equal(AdminUserId, audit.ActorUserId);
        Assert.Equal("Notification", audit.EntityType);
        Assert.Contains(id.ToString(), audit.MetadataJson);
        Assert.Contains(userId, audit.MetadataJson);
        Assert.DoesNotContain(email, audit.MetadataJson + audit.ChangesJson);
        Assert.DoesNotContain("Algorithms", audit.MetadataJson + audit.ChangesJson);
    }

    [Fact]
    public async Task A_resend_without_the_anti_forgery_token_is_rejected_and_creates_nothing()
    {
        using var admin = await AdminAsync();
        var (id, _, _) = await FailedAsync();

        using var response = await admin.SendAsync(HttpMethod.Post, $"{History}/{id}/resends", headers: Key(), withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single(await Host.RecordsAsync());
    }
}
