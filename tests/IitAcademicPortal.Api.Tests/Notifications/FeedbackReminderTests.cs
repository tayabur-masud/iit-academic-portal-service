using System.Net;
using System.Reflection;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// US4: one portal-wide reminder lead time, set by an Admin with no default and no per-window override, drives at most one reminder
/// per student per window occurrence. It reads only what the window owner supplies and never any feedback submission.
/// </summary>
public sealed class FeedbackReminderTests : NotificationTestBase
{
    private static readonly DateTime Origin = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private DateTime Now => Host.Clock.GetUtcNow().UtcDateTime;

    private static FeedbackWindowSnapshot Window(
        string occurrence, DateTime closeAt, IReadOnlyList<string> students, FeedbackWindowStatus status = FeedbackWindowStatus.Open,
        string course = "Databases", string label = "Mid-term feedback") =>
        new(occurrence, course, label, closeAt, status, students);

    private async Task SetLeadAsync(PortalClient admin, int minutes)
    {
        var (status, _) = await SendAsync(admin, HttpMethod.Put, ReminderSetting, new { leadTimeMinutes = minutes });
        Assert.Equal(HttpStatusCode.OK, status);
    }

    private async Task<List<IitAcademicPortal.Application.Abstractions.EmailMessage>> ReminderEmailsAsync()
    {
        await Host.FlushNotificationsAsync();
        return Host.Emails.Messages.Where(m => m.Subject.StartsWith("Feedback for ", StringComparison.Ordinal)).ToList();
    }

    // --- The portal-wide setting ---

    [Fact]
    public async Task The_setting_is_unconfigured_until_an_Admin_saves_a_value_and_no_default_is_assumed()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Get, ReminderSetting);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(body.GetProperty("configured").GetBoolean());
        Assert.False(body.TryGetProperty("leadTimeMinutes", out _));
    }

    [Fact]
    public async Task A_positive_whole_number_of_minutes_is_saved_and_returned()
    {
        using var admin = await AdminAsync();

        var (put, saved) = await SendAsync(admin, HttpMethod.Put, ReminderSetting, new { leadTimeMinutes = 90 });
        var (_, read) = await SendAsync(admin, HttpMethod.Get, ReminderSetting);

        Assert.Equal(HttpStatusCode.OK, put);
        Assert.True(saved.GetProperty("configured").GetBoolean());
        Assert.Equal(90, saved.GetProperty("leadTimeMinutes").GetInt32());
        Assert.Equal(90, read.GetProperty("leadTimeMinutes").GetInt32());
    }

    [Theory]
    [InlineData("{\"leadTimeMinutes\":0}")]
    [InlineData("{\"leadTimeMinutes\":-5}")]
    [InlineData("{\"leadTimeMinutes\":525601}")]
    [InlineData("{}")]
    [InlineData("{\"leadTimeMinutes\":null}")]
    public async Task An_invalid_value_is_rejected_and_the_last_valid_setting_is_kept(string json)
    {
        using var admin = await AdminAsync();
        await SetLeadAsync(admin, 45);

        var (status, problem) = await SendRawAsync(admin, json);
        var (_, read) = await SendAsync(admin, HttpMethod.Get, ReminderSetting);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "leadTimeMinutes"));
        Assert.Equal(45, read.GetProperty("leadTimeMinutes").GetInt32());
    }

    [Theory]
    [InlineData("{\"leadTimeMinutes\":1.5}")]
    [InlineData("{\"leadTimeMinutes\":\"soon\"}")]
    [InlineData("{\"leadTimeMinutes\":30,\"windowId\":\"w1\"}")]
    public async Task A_non_integer_or_a_per_window_override_is_rejected(string json)
    {
        using var admin = await AdminAsync();
        await SetLeadAsync(admin, 45);

        var (status, _) = await SendRawAsync(admin, json);
        var (_, read) = await SendAsync(admin, HttpMethod.Get, ReminderSetting);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(45, read.GetProperty("leadTimeMinutes").GetInt32());
    }

    [Fact]
    public async Task There_is_no_per_window_setting_route()
    {
        using var admin = await AdminAsync();

        using var response = await admin.SendAsync(HttpMethod.Put, $"{ReminderSetting}/window-1", new { leadTimeMinutes = 5 });

        Assert.True(response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);
    }

    [Fact]
    public async Task A_change_is_audited_with_the_old_and_new_value_and_a_repeat_of_the_same_value_is_not()
    {
        using var admin = await AdminAsync();

        await SetLeadAsync(admin, 30);
        await SetLeadAsync(admin, 30);
        await SetLeadAsync(admin, 60);

        var events = await Host.AuditEventsAsync("notification.reminder-policy.updated");
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(AdminUserId, e.ActorUserId));
        Assert.Contains("leadTimeMinutes", events[0].ChangesJson);
        Assert.Contains("30", events[0].ChangesJson);
        Assert.Contains("60", events[1].ChangesJson);
    }

    [Fact]
    public async Task The_setting_routes_deny_anonymous_callers_and_non_Admin_roles()
    {
        using var anonymous = Host.CreatePortalClient();
        using var student = Host.CreatePortalClient();
        var email = PortalApiFactory.UniqueEmail("student");
        await Host.CreateUserAsync(email, PortalRoles.Student);
        await student.SignInSuccessfullyAsync(email);

        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var unauthenticated = await anonymous.SendAsync(method, ReminderSetting, method == HttpMethod.Put ? new { leadTimeMinutes = 5 } : null);
            using var forbidden = await student.SendAsync(method, ReminderSetting, method == HttpMethod.Put ? new { leadTimeMinutes = 5 } : null);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }

        using var admin = await AdminAsync();
        var (_, read) = await SendAsync(admin, HttpMethod.Get, ReminderSetting);
        Assert.False(read.GetProperty("configured").GetBoolean());
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> SendRawAsync(PortalClient client, string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, ReminderSetting)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-CSRF-Token", await client.GetAntiforgeryTokenAsync());
        using var response = await client.SendRawAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    // --- Scheduling ---

    [Fact]
    public async Task Nothing_is_scheduled_or_sent_until_a_lead_time_is_configured()
    {
        var student = await Host.CreateRecipientAsync();
        Host.Windows.Set(Window("w1", Now.AddMinutes(10), [student]));

        var pass = await Host.RunSchedulerAsync();
        Host.Advance(TimeSpan.FromHours(1));
        await Host.RunSchedulerAsync();

        Assert.Equal(0, pass.Scheduled);
        Assert.Empty(await Host.SchedulesAsync());
        Assert.Empty(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_reminder_is_due_at_the_closing_time_minus_the_global_lead_and_is_sent_once_to_each_eligible_student()
    {
        using var admin = await AdminAsync();
        var first = await Host.CreateRecipientAsync();
        var second = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("w1", close, [first, second]));

        await Host.RunSchedulerAsync();
        var schedules = await Host.SchedulesAsync();
        var beforeDue = await ReminderEmailsAsync();

        Assert.Equal(2, schedules.Count);
        Assert.All(schedules, s =>
        {
            Assert.Equal(ReminderScheduleState.Scheduled, s.State);
            Assert.Equal(close.AddMinutes(-30), s.DueAtUtc);
            Assert.Equal(close, s.CloseAtUtc);
        });
        Assert.Empty(beforeDue);

        Host.Advance(TimeSpan.FromMinutes(95));
        var dispatch = await Host.RunSchedulerAsync();
        var sent = await ReminderEmailsAsync();
        Host.Advance(TimeSpan.FromMinutes(5));
        await Host.RunSchedulerAsync();
        var afterRepeat = await ReminderEmailsAsync();

        Assert.Equal(2, dispatch.Dispatched);
        Assert.Equal(2, sent.Count);
        Assert.Equal(2, afterRepeat.Count);
        Assert.Equal(new[] { await Host.EmailOfAsync(first), await Host.EmailOfAsync(second) }.Order(), sent.Select(m => m.To).Order());
        Assert.All(sent, m =>
        {
            Assert.Equal("Feedback for Databases closes soon", m.Subject);
            Assert.Contains("Mid-term feedback", m.Body);
            Assert.Contains(close.ToString("yyyy-MM-dd HH:mm") + " UTC", m.Body);
        });
        Assert.All(await Host.SchedulesAsync(), s => Assert.Equal(ReminderScheduleState.Dispatched, s.State));
        var records = await Host.RecordsAsync();
        Assert.Equal(2, records.Count(r => r.EventType == NotificationEventType.FeedbackClosingReminder && r.OccurrenceId == "w1"));
    }

    [Fact]
    public async Task Every_window_uses_the_same_lead_time_with_no_per_window_difference()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 20);
        var closeA = Now.AddHours(3);
        var closeB = Now.AddHours(8);
        Host.Windows.Set(Window("a", closeA, [student]), Window("b", closeB, [student], course: "Networks"));

        await Host.RunSchedulerAsync();

        var schedules = (await Host.SchedulesAsync()).ToDictionary(s => s.OccurrenceId);
        Assert.Equal(closeA.AddMinutes(-20), schedules["a"].DueAtUtc);
        Assert.Equal(closeB.AddMinutes(-20), schedules["b"].DueAtUtc);
    }

    [Fact]
    public async Task Changing_the_lead_time_before_the_reminder_is_issued_reschedules_it_and_never_duplicates_it()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("w1", close, [student]));
        await Host.RunSchedulerAsync();
        var before = Assert.Single(await Host.SchedulesAsync());

        await SetLeadAsync(admin, 90);
        await Host.RunSchedulerAsync();
        var after = Assert.Single(await Host.SchedulesAsync());

        Assert.Equal(close.AddMinutes(-30), before.DueAtUtc);
        Assert.Equal(close.AddMinutes(-90), after.DueAtUtc);
        Assert.NotEqual(before.PolicyRevision, after.PolicyRevision);

        Host.Advance(TimeSpan.FromMinutes(40));
        await Host.RunSchedulerAsync();
        Assert.Single(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_reminder_already_issued_is_never_repeated_after_the_lead_time_or_close_time_changes()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(1);
        Host.Windows.Set(Window("w1", close, [student]));
        Host.Advance(TimeSpan.FromMinutes(40));
        await Host.RunSchedulerAsync();
        Assert.Single(await ReminderEmailsAsync());

        await SetLeadAsync(admin, 55);
        Host.Windows.Set(Window("w1", close.AddHours(2), [student]));
        Host.Advance(TimeSpan.FromHours(1));
        await Host.RunSchedulerAsync();
        Host.Advance(TimeSpan.FromHours(1));
        await Host.RunSchedulerAsync();

        Assert.Single(await ReminderEmailsAsync());
        Assert.Equal(ReminderScheduleState.Dispatched, Assert.Single(await Host.SchedulesAsync()).State);
    }

    [Fact]
    public async Task A_moved_closing_time_recomputes_the_due_time_of_an_unsent_reminder()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("w1", close, [student]));
        await Host.RunSchedulerAsync();

        Host.Windows.Set(Window("w1", close.AddHours(3), [student]));
        await Host.RunSchedulerAsync();
        Host.Advance(TimeSpan.FromMinutes(100));
        await Host.RunSchedulerAsync();
        var notYet = await ReminderEmailsAsync();
        Host.Advance(TimeSpan.FromHours(3));
        await Host.RunSchedulerAsync();

        var schedule = Assert.Single(await Host.SchedulesAsync());
        Assert.Equal(close.AddHours(3).AddMinutes(-30), schedule.DueAtUtc);
        Assert.Empty(notYet);
        Assert.Single(await ReminderEmailsAsync());
    }

    [Theory]
    [InlineData(FeedbackWindowStatus.Closed)]
    [InlineData(FeedbackWindowStatus.Cancelled)]
    [InlineData(FeedbackWindowStatus.Superseded)]
    public async Task A_window_that_closes_is_cancelled_or_superseded_before_its_reminder_sends_nothing(FeedbackWindowStatus status)
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("w1", close, [student]));
        await Host.RunSchedulerAsync();

        Host.Windows.Set(Window("w1", close, [student], status));
        await Host.RunSchedulerAsync();
        Host.Advance(TimeSpan.FromHours(3));
        await Host.RunSchedulerAsync();

        Assert.Equal(ReminderScheduleState.Cancelled, Assert.Single(await Host.SchedulesAsync()).State);
        Assert.Empty(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_superseding_occurrence_gets_its_own_reminder_and_the_old_one_gets_none()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("old", close, [student]));
        await Host.RunSchedulerAsync();

        Host.Windows.Set(Window("old", close, [student], FeedbackWindowStatus.Superseded), Window("new", close.AddHours(1), [student]));
        await Host.RunSchedulerAsync();
        Host.Advance(TimeSpan.FromMinutes(150));
        await Host.RunSchedulerAsync();

        var schedules = (await Host.SchedulesAsync()).ToDictionary(s => s.OccurrenceId);
        Assert.Equal(ReminderScheduleState.Cancelled, schedules["old"].State);
        Assert.Equal(ReminderScheduleState.Dispatched, schedules["new"].State);
        Assert.Single(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_window_the_owner_no_longer_reports_and_a_student_no_longer_eligible_are_cancelled()
    {
        using var admin = await AdminAsync();
        var kept = await Host.CreateRecipientAsync();
        var dropped = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(2);
        Host.Windows.Set(Window("w1", close, [kept, dropped]), Window("w2", close, [kept]));
        await Host.RunSchedulerAsync();

        Host.Windows.Set(Window("w1", close, [kept]));
        await Host.RunSchedulerAsync();

        var schedules = await Host.SchedulesAsync();
        Assert.Equal(ReminderScheduleState.Cancelled, schedules.Single(s => s.OccurrenceId == "w2").State);
        Assert.Equal(ReminderScheduleState.Cancelled, schedules.Single(s => s.OccurrenceId == "w1" && s.RecipientUserId == dropped).State);
        Assert.Equal(ReminderScheduleState.Scheduled, schedules.Single(s => s.OccurrenceId == "w1" && s.RecipientUserId == kept).State);
    }

    [Fact]
    public async Task A_window_still_reported_open_after_its_closing_time_gets_no_reminder()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 30);
        var close = Now.AddHours(1);
        Host.Windows.Set(Window("w1", close, [student]));
        await Host.RunSchedulerAsync();

        // The owner lags: the window still says Open but its closing time has passed.
        Host.Advance(TimeSpan.FromHours(2));
        await Host.RunSchedulerAsync();

        Assert.Equal(ReminderScheduleState.Cancelled, Assert.Single(await Host.SchedulesAsync()).State);
        Assert.Empty(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_window_that_is_already_inside_the_lead_when_first_seen_is_reminded_promptly_while_it_is_open()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 120);
        Host.Windows.Set(Window("late", Now.AddMinutes(30), [student]));

        await Host.RunSchedulerAsync();

        Assert.Single(await ReminderEmailsAsync());
    }

    [Fact]
    public async Task A_notification_store_outage_leaves_the_reminder_due_and_the_next_pass_sends_exactly_one()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 120);
        Host.Windows.Set(Window("w1", Now.AddMinutes(30), [student]));
        Host.Fault.FailAccept = true;

        var failed = await Host.RunSchedulerAsync();
        Host.Fault.FailAccept = false;
        var recovered = await Host.RunSchedulerAsync();
        await Host.RunSchedulerAsync();

        Assert.Equal(0, failed.Dispatched);
        Assert.Equal(1, recovered.Dispatched);
        Assert.Single(await ReminderEmailsAsync());
        Assert.NotEmpty(Host.Signals.PersistenceFailures);
    }

    [Fact]
    public async Task A_disabled_reminder_template_suppresses_the_reminder_and_enabling_it_later_does_not_replay_it()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await SetLeadAsync(admin, 120);
        var (_, current) = await SendAsync(admin, HttpMethod.Get, $"{Templates}/feedback-closing-reminder/email");
        await SendAsync(admin, HttpMethod.Put, $"{Templates}/feedback-closing-reminder/email",
            new { expectedRevision = 1, subject = current.GetProperty("subject").GetString(), body = current.GetProperty("body").GetString(), enabled = false });
        Host.Windows.Set(Window("w1", Now.AddMinutes(30), [student]));

        await Host.RunSchedulerAsync();
        await SendAsync(admin, HttpMethod.Put, $"{Templates}/feedback-closing-reminder/email",
            new { expectedRevision = 1, subject = current.GetProperty("subject").GetString(), body = current.GetProperty("body").GetString(), enabled = true });
        await Host.RunSchedulerAsync();

        Assert.Empty(await ReminderEmailsAsync());
        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationStatus.Suppressed, (await Host.StateAsync(record.Id)).Status);
        Assert.Equal(ReminderScheduleState.Dispatched, Assert.Single(await Host.SchedulesAsync()).State);
    }

    // --- Feedback confidentiality ---

    [Fact]
    public async Task Nothing_about_feedback_submissions_can_reach_the_scheduler_a_schedule_or_a_reminder()
    {
        var forbidden = new[] { "submi", "rating", "answer", "response", "comment", "feedbacktext", "score" };
        var types = new[] { typeof(IFeedbackWindowSource), typeof(FeedbackWindowSnapshot), typeof(FeedbackReminderSchedule), typeof(IitAcademicPortal.Application.Notifications.FeedbackReminderScheduler) };

        foreach (var type in types)
        {
            var names = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Select(m => m.Name)
                .Concat(type.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.Name ?? string.Empty)))
                .Select(n => n.ToLowerInvariant());
            Assert.All(names, n => Assert.DoesNotContain(forbidden, f => n.Contains(f)));
        }

        await Task.CompletedTask;
    }

    [Fact]
    public async Task Every_eligible_student_is_reminded_and_the_message_and_history_reveal_nothing_about_submissions()
    {
        using var admin = await AdminAsync();
        var students = new[] { await Host.CreateRecipientAsync(), await Host.CreateRecipientAsync(), await Host.CreateRecipientAsync() };
        await SetLeadAsync(admin, 120);
        Host.Windows.Set(Window("w1", Now.AddMinutes(30), students));

        await Host.RunSchedulerAsync();
        var emails = await ReminderEmailsAsync();

        Assert.Equal(3, emails.Count);
        Assert.All(emails, m =>
        {
            Assert.DoesNotContain("submitted", m.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("have not", m.Body, StringComparison.OrdinalIgnoreCase);
        });
        var (_, list) = await SendAsync(admin, HttpMethod.Get, $"{History}?eventType=feedback-closing-reminder");
        Assert.DoesNotContain("submi", list.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }
}
