using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// US3: Admins search the permanent notification history with combined filters and bounded keyset batches, and read safe detail.
/// Security messages never expose content, and history survives template edits, address changes, and account removal.
/// </summary>
public sealed class NotificationHistoryTests : NotificationTestBase
{
    private static List<string> Ids(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    private Task<(HttpStatusCode Status, JsonElement Body)> Search(PortalClient admin, string query = "") =>
        SendAsync(admin, HttpMethod.Get, $"{History}{query}");

    /// <summary>Adds history rows directly (so a test can have many, at exact times) without going through delivery.</summary>
    private async Task<List<NotificationRecord>> SeedAsync(
        int count, DateTime firstAt, TimeSpan step, NotificationEventType eventType = NotificationEventType.ResultsPublished,
        string? recipient = null, string? destination = null, NotificationStatus status = NotificationStatus.Sent)
    {
        var revisionId = NotificationTemplateCatalog.InitialRevisionId(eventType);
        var records = new List<NotificationRecord>();
        await Host.WithDbAsync(async db =>
        {
            for (var i = 0; i < count; i++)
            {
                var record = NotificationRecord.Create(
                    eventType, $"seed-{Guid.NewGuid():N}", recipient ?? $"user-{i}", destination ?? $"seed{i}@iit.test", revisionId,
                    firstAt + step * i, eventType.IsSecurity() ? null : "Subject", eventType.IsSecurity() ? null : "Body");
                db.NotificationRecords.Add(record);
                db.NotificationDispatchStates.Add(NotificationDispatchState.Create(record.Id, status, record.RequestedAtUtc));
                records.Add(record);
            }

            await db.SaveChangesAsync();
        });
        return records;
    }

    [Fact]
    public async Task History_is_newest_first_in_bounded_batches_followed_by_an_opaque_cursor()
    {
        using var admin = await AdminAsync();
        var records = await SeedAsync(5, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromMinutes(1));
        var expected = records.OrderByDescending(r => r.RequestedAtUtc).ThenByDescending(r => r.Id).Select(r => r.Id.ToString()).ToList();

        var (_, first) = await Search(admin, "?pageSize=2");
        var cursor1 = first.GetProperty("nextCursor").GetString();
        var (_, second) = await Search(admin, $"?pageSize=2&cursor={cursor1}");
        var cursor2 = second.GetProperty("nextCursor").GetString();
        var (_, third) = await Search(admin, $"?pageSize=2&cursor={cursor2}");

        Assert.Equal(expected.Take(2), Ids(first).Select(i => i));
        Assert.Equal(expected.Skip(2).Take(2), Ids(second));
        Assert.Equal(expected.Skip(4), Ids(third));
        Assert.Equal(JsonValueKind.Null, third.GetProperty("nextCursor").ValueKind);
        Assert.False(first.TryGetProperty("totalCount", out _));
        Assert.False(first.TryGetProperty("totalPages", out _));
    }

    [Fact]
    public async Task Rows_with_the_same_request_time_are_ordered_by_identifier_without_skips_or_repeats()
    {
        using var admin = await AdminAsync();
        var sameTime = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc);
        var records = await SeedAsync(7, sameTime, TimeSpan.Zero);
        var expected = records.OrderByDescending(r => r.Id).Select(r => r.Id.ToString()).ToList();

        var collected = new List<string>();
        string? cursor = null;
        do
        {
            var (_, page) = await Search(admin, $"?pageSize=3{(cursor is null ? string.Empty : $"&cursor={cursor}")}");
            collected.AddRange(Ids(page));
            cursor = page.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(expected, collected);
    }

    [Fact]
    public async Task The_default_page_is_25_and_the_largest_page_is_100()
    {
        using var admin = await AdminAsync();
        await SeedAsync(130, new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.FromSeconds(1));

        var (_, standard) = await Search(admin);
        var (_, largest) = await Search(admin, "?pageSize=100");
        var (_, rest) = await Search(admin, $"?pageSize=100&cursor={largest.GetProperty("nextCursor").GetString()}");

        Assert.Equal(25, Ids(standard).Count);
        Assert.NotEqual(JsonValueKind.Null, standard.GetProperty("nextCursor").ValueKind);
        Assert.Equal(100, Ids(largest).Count);
        Assert.Equal(30, Ids(rest).Count);
    }

    [Theory]
    [InlineData("?pageSize=0", "pageSize")]
    [InlineData("?pageSize=101", "pageSize")]
    [InlineData("?status=Delivered", "status")]
    [InlineData("?eventType=Newsletter", "eventType")]
    [InlineData("?cursor=not-a-cursor", "cursor")]
    [InlineData("?fromUtc=2026-05-02T00:00:00Z&toUtc=2026-05-01T00:00:00Z", "toUtc")]
    [InlineData("?fromUtc=2026-05-01T00:00:00Z&toUtc=2026-05-01T00:00:00Z", "toUtc")]
    public async Task Invalid_filters_are_rejected_with_a_field_error(string query, string field)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await Search(admin, query);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, field));
    }

    [Fact]
    public async Task The_date_range_includes_its_start_and_excludes_its_end()
    {
        using var admin = await AdminAsync();
        var start = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc);
        var records = await SeedAsync(3, start, TimeSpan.FromHours(1));

        var (_, inclusive) = await Search(admin, $"?fromUtc={start:O}&toUtc={start.AddHours(2):O}");
        var (_, onlyStart) = await Search(admin, $"?fromUtc={start:O}&toUtc={start.AddHours(1):O}");
        var (_, fromLast) = await Search(admin, $"?fromUtc={start.AddHours(2):O}");
        var (_, toBeforeEverything) = await Search(admin, $"?toUtc={start:O}");

        Assert.Equal([records[1].Id.ToString(), records[0].Id.ToString()], Ids(inclusive));
        Assert.Equal([records[0].Id.ToString()], Ids(onlyStart));
        Assert.Equal([records[2].Id.ToString()], Ids(fromLast));
        Assert.Empty(Ids(toBeforeEverything));
    }

    [Fact]
    public async Task Recipient_event_status_and_dates_combine_with_and()
    {
        using var admin = await AdminAsync();
        var day = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var target = (await SeedAsync(1, day.AddHours(5), TimeSpan.Zero, NotificationEventType.FeedbackWindowOpened, "student-1", "Student.One@iit.test", NotificationStatus.Failed))[0];
        await SeedAsync(1, day.AddHours(5), TimeSpan.Zero, NotificationEventType.ResultsPublished, "student-1", "Student.One@iit.test", NotificationStatus.Failed);
        await SeedAsync(1, day.AddHours(5), TimeSpan.Zero, NotificationEventType.FeedbackWindowOpened, "student-2", "student.two@iit.test", NotificationStatus.Failed);
        await SeedAsync(1, day.AddHours(5), TimeSpan.Zero, NotificationEventType.FeedbackWindowOpened, "student-1", "Student.One@iit.test", NotificationStatus.Sent);
        await SeedAsync(1, day.AddDays(3), TimeSpan.Zero, NotificationEventType.FeedbackWindowOpened, "student-1", "Student.One@iit.test", NotificationStatus.Failed);

        var (_, byId) = await Search(admin, $"?recipient=student-1&eventType=FeedbackWindowOpened&status=Failed&fromUtc={day:O}&toUtc={day.AddDays(1):O}");
        var (_, byAddress) = await Search(admin, $"?recipient=STUDENT.one@IIT.test&eventType=feedback-window-opened&status=failed&fromUtc={day:O}&toUtc={day.AddDays(1):O}");
        var (_, none) = await Search(admin, $"?recipient=student-2&status=Sent");

        Assert.Equal([target.Id.ToString()], Ids(byId));
        Assert.Equal([target.Id.ToString()], Ids(byAddress));
        Assert.Empty(Ids(none));
    }

    [Fact]
    public async Task The_status_shown_is_the_current_delivery_state_and_the_failure_reason_is_safe()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        var email = await Host.EmailOfAsync(student);
        var id = await AcceptOneAsync(Results("pub-30", student));
        Host.Emails.Behavior = message => new InvalidOperationException($"{message.To} was refused: {message.Body}");

        await Host.FlushNotificationsAsync();
        var (_, retrying) = await Search(admin, "?status=Retrying");
        Host.Recover();
        Host.Advance(TimeSpan.FromMinutes(1));
        await Host.FlushNotificationsAsync();
        var (_, sent) = await Search(admin, "?status=Sent");

        Assert.Equal([id.ToString()], Ids(retrying));
        Assert.Equal("InvalidOperationException", retrying.GetProperty("items")[0].GetProperty("failureCode").GetString());
        Assert.Equal([id.ToString()], Ids(sent));
        Assert.False(sent.GetProperty("items")[0].TryGetProperty("failureCode", out _));
        Assert.NotNull(sent.GetProperty("items")[0].GetProperty("sentAtUtc").GetString());
        Assert.DoesNotContain(email + " was refused", retrying.GetRawText());
    }

    [Fact]
    public async Task An_ordinary_detail_shows_the_exact_destination_and_content_even_after_a_template_edit_and_an_address_change()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        var originalEmail = await Host.EmailOfAsync(student);
        var id = await AcceptOneAsync(Results("pub-31", student));
        await Host.FlushNotificationsAsync();

        await SendAsync(admin, HttpMethod.Put, $"{Templates}/results-published/email", new { expectedRevision = 1, subject = "Edited {{courseName}}", body = "Edited {{termName}}", enabled = true });
        var newEmail = PortalApiFactory.UniqueEmail("moved");
        await Host.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == student);
            user.Email = newEmail;
            user.NormalizedEmail = newEmail.ToUpperInvariant();
            await db.SaveChangesAsync();
        });

        var (status, detail) = await SendAsync(admin, HttpMethod.Get, $"{History}/{id}");

        Assert.Equal(HttpStatusCode.OK, status);
        var notification = detail.GetProperty("notification");
        Assert.Equal(originalEmail, notification.GetProperty("destination").GetString());
        Assert.False(notification.GetProperty("contentWithheld").GetBoolean());
        Assert.Equal("Results published for Algorithms", detail.GetProperty("subject").GetString());
        Assert.Contains("Results for Algorithms (Fall 2026) have been published.", detail.GetProperty("body").GetString());
        Assert.Equal(1, detail.GetProperty("templateRevision").GetInt32());
        Assert.Equal("pub-31", detail.GetProperty("occurrenceId").GetString());
        var attempt = Assert.Single(detail.GetProperty("attempts").EnumerateArray());
        Assert.Equal("Accepted", attempt.GetProperty("outcome").GetString());
        Assert.NotNull(attempt.GetProperty("providerAcceptedAtUtc").GetString());
        Assert.Equal(1, attempt.GetProperty("attemptNumber").GetInt32());
    }

    [Fact]
    public async Task A_security_message_exposes_only_safe_facts_and_says_its_content_is_withheld()
    {
        using var admin = await AdminAsync();
        var email = PortalApiFactory.UniqueEmail("student");
        await Host.CreateUserAsync(email, PortalRoles.Student);
        using var client = Host.CreatePortalClient();
        var proof = await PasswordRecoveryTests.RequestProofAsync(Host, client, email);

        var (_, list) = await Search(admin, "?eventType=password-reset");
        var id = Ids(list).Single();
        var (status, detail) = await SendAsync(admin, HttpMethod.Get, $"{History}/{id}");

        Assert.Equal(HttpStatusCode.OK, status);
        var raw = detail.GetRawText() + list.GetRawText();
        Assert.True(list.GetProperty("items")[0].GetProperty("contentWithheld").GetBoolean());
        Assert.True(detail.GetProperty("notification").GetProperty("contentWithheld").GetBoolean());
        Assert.False(detail.TryGetProperty("subject", out _));
        Assert.False(detail.TryGetProperty("body", out _));
        Assert.DoesNotContain(proof, raw);
        Assert.DoesNotContain("reset-password", raw);
        Assert.DoesNotContain("Reset your IIT Academic Portal password", raw);
        Assert.Equal("Accepted", detail.GetProperty("attempts")[0].GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task History_rows_never_contain_message_content()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        await Host.RequestAsync(Results("pub-32", student));

        var (_, list) = await Search(admin);

        Assert.False(list.GetProperty("items")[0].TryGetProperty("subject", out _));
        Assert.False(list.GetProperty("items")[0].TryGetProperty("body", out _));
    }

    [Fact]
    public async Task History_survives_removal_of_the_recipient_account()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        var email = await Host.EmailOfAsync(student);
        var id = await AcceptOneAsync(Results("pub-33", student));
        await Host.FlushNotificationsAsync();
        await Host.WithDbAsync(async db =>
        {
            db.Users.Remove(await db.Users.SingleAsync(u => u.Id == student));
            await db.SaveChangesAsync();
        });

        var (_, byId) = await Search(admin, $"?recipient={student}");
        var (status, detail) = await SendAsync(admin, HttpMethod.Get, $"{History}/{id}");

        Assert.Equal([id.ToString()], Ids(byId));
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(email, detail.GetProperty("notification").GetProperty("destination").GetString());
        Assert.Equal(student, detail.GetProperty("notification").GetProperty("recipientUserId").GetString());
    }

    [Fact]
    public async Task An_unknown_notification_is_not_found_and_history_has_no_update_or_delete_route()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-34", student));

        var (missing, _) = await SendAsync(admin, HttpMethod.Get, $"{History}/{Guid.NewGuid()}");
        using var delete = await admin.SendAsync(HttpMethod.Delete, $"{History}/{id}");
        using var put = await admin.SendAsync(HttpMethod.Put, $"{History}/{id}", new { status = "Sent" });
        using var patch = await admin.SendAsync(HttpMethod.Patch, $"{History}/{id}", new { status = "Sent" });

        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.All([delete, put, patch], r => Assert.True(r.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotFound, $"{r.StatusCode}"));
        Assert.Single(await Host.RecordsAsync());
    }

    [Fact]
    public async Task Every_history_route_denies_anonymous_callers_and_every_non_Admin_active_role_without_disclosing_data()
    {
        var student = await Host.CreateRecipientAsync();
        var id = await AcceptOneAsync(Results("pub-35", student));
        var email = await Host.EmailOfAsync(student);
        var routes = new (HttpMethod Method, string Url)[]
        {
            (HttpMethod.Get, History),
            (HttpMethod.Get, $"{History}/{id}"),
            (HttpMethod.Post, $"{History}/{id}/resends"),
        };
        using var anonymous = Host.CreatePortalClient();
        var both = PortalApiFactory.UniqueEmail("both");
        await Host.CreateUserAsync(both, PortalRoles.Admin, PortalRoles.Teacher);
        await Host.SetDefaultRoleAsync(both, PortalRoles.Teacher);
        using var adminActingAsTeacher = Host.CreatePortalClient();
        await adminActingAsTeacher.SignInSuccessfullyAsync(both);

        foreach (var (method, url) in routes)
        {
            var headers = new Dictionary<string, string> { ["Idempotency-Key"] = "k1" };
            using var unauthenticated = await anonymous.SendAsync(method, url, headers: headers);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            foreach (var role in new[] { PortalRoles.Student, PortalRoles.Teacher, PortalRoles.Coordinator })
            {
                var roleEmail = PortalApiFactory.UniqueEmail(role.ToLowerInvariant());
                await Host.CreateUserAsync(roleEmail, role);
                using var client = Host.CreatePortalClient();
                await client.SignInSuccessfullyAsync(roleEmail);
                using var forbidden = await client.SendAsync(method, url, headers: headers);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.DoesNotContain(email, await forbidden.Content.ReadAsStringAsync());
            }

            using var wrongActiveRole = await adminActingAsTeacher.SendAsync(method, url, headers: headers);
            Assert.Equal(HttpStatusCode.Forbidden, wrongActiveRole.StatusCode);
        }

        Assert.Single(await Host.RecordsAsync());
    }
}
