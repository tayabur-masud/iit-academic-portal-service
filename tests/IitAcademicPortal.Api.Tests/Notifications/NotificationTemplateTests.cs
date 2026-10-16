using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// US2: Admins maintain the seven email templates. Each wording change is an immutable revision, validation is strict, security
/// templates cannot be disabled, stale edits are refused, and a preview is synthetic, sends nothing, and stores nothing.
/// </summary>
public sealed class NotificationTemplateTests : NotificationTestBase
{
    private static string Route(string slug) => $"{Templates}/{slug}/email";

    private static object Edit(int revision, string subject, string body, bool enabled = true) =>
        new { expectedRevision = revision, subject, body, enabled };

    private async Task<int> RevisionCountAsync(NotificationEventType eventType)
    {
        var count = 0;
        await Host.WithDbAsync(async db =>
            count = await db.NotificationTemplateRevisions.CountAsync(r => db.NotificationTemplates.Any(t => t.Id == r.TemplateId && t.EventType == eventType)));
        return count;
    }

    [Fact]
    public async Task The_list_shows_the_seven_email_templates_with_their_revision_state_and_placeholders_and_no_proof_values()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Get, Templates);

        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.EnumerateArray().ToList();
        Assert.Equal(7, items.Count);
        Assert.Equal(
            ["TeacherInvitation", "StudentInvitation", "EmailConfirmation", "PasswordReset", "ResultsPublished", "FeedbackWindowOpened", "FeedbackClosingReminder"],
            items.Select(i => i.GetProperty("eventType").GetString()));
        Assert.All(items, i =>
        {
            Assert.Equal("Email", i.GetProperty("channel").GetString());
            Assert.Equal(1, i.GetProperty("revision").GetInt32());
            Assert.True(i.GetProperty("enabled").GetBoolean());
            Assert.NotEmpty(i.GetProperty("subject").GetString()!);
        });
        var reset = items.Single(i => i.GetProperty("eventType").GetString() == "PasswordReset");
        Assert.True(reset.GetProperty("isSecurity").GetBoolean());
        Assert.Equal("password-reset", reset.GetProperty("slug").GetString());
        Assert.Contains("actionLink", reset.GetProperty("requiredPlaceholders").EnumerateArray().Select(p => p.GetString()));
        Assert.Contains("{{actionLink}}", reset.GetProperty("body").GetString());
        var results = items.Single(i => i.GetProperty("eventType").GetString() == "ResultsPublished");
        Assert.False(results.GetProperty("isSecurity").GetBoolean());
        Assert.Empty(results.GetProperty("requiredPlaceholders").EnumerateArray());
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("PasswordReset")]
    public async Task One_template_can_be_read_by_its_slug_or_name_and_an_unknown_one_is_not_found(string slug)
    {
        using var admin = await AdminAsync();

        var (found, body) = await SendAsync(admin, HttpMethod.Get, Route(slug));
        var (missing, _) = await SendAsync(admin, HttpMethod.Get, Route("not-an-event"));

        Assert.Equal(HttpStatusCode.OK, found);
        Assert.Equal("Reset your IIT Academic Portal password", body.GetProperty("subject").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing);
    }

    [Fact]
    public async Task A_valid_edit_creates_a_new_immutable_revision_that_later_notifications_use_while_earlier_ones_keep_their_wording()
    {
        using var admin = await AdminAsync();
        var student = await Host.CreateRecipientAsync();
        var earlier = await AcceptOneAsync(Results("pub-20", student));

        var (status, body) = await SendAsync(admin, HttpMethod.Put, Route("results-published"),
            Edit(1, "New: {{courseName}} results", "Dear student, {{courseName}} ({{termName}}) is out. See {{portalUrl}}"));
        var later = await AcceptOneAsync(Results("pub-21", await Host.CreateRecipientAsync()));
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("revision").GetInt32());
        var records = await Host.RecordsAsync();
        var first = records.Single(r => r.Id == earlier);
        var second = records.Single(r => r.Id == later);
        Assert.Equal("Results published for Algorithms", first.Subject);
        Assert.Equal("New: Algorithms results", second.Subject);
        Assert.Contains("Dear student, Algorithms (Fall 2026) is out.", second.Body);
        Assert.NotEqual(first.TemplateRevisionId, second.TemplateRevisionId);
        Assert.Equal(2, await RevisionCountAsync(NotificationEventType.ResultsPublished));

        // The earlier notification still delivers its own accepted wording, not the edited one.
        Assert.Contains(Host.Emails.Messages, m => m.Subject == "Results published for Algorithms");
        Assert.Contains(Host.Emails.Messages, m => m.Subject == "New: Algorithms results");
    }

    [Fact]
    public async Task The_old_revision_row_is_kept_unchanged_after_an_edit()
    {
        using var admin = await AdminAsync();
        await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "Edited {{courseName}}", "Edited {{termName}}"));

        await Host.WithDbAsync(async db =>
        {
            var revisions = await db.NotificationTemplateRevisions.AsNoTracking()
                .Where(r => db.NotificationTemplates.Any(t => t.Id == r.TemplateId && t.EventType == NotificationEventType.ResultsPublished))
                .OrderBy(r => r.RevisionNumber).ToListAsync();
            Assert.Equal([1, 2], revisions.Select(r => r.RevisionNumber));
            Assert.Equal("Results published for {{courseName}}", revisions[0].Subject);
            Assert.Equal("Edited {{courseName}}", revisions[1].Subject);
            Assert.Equal(AdminUserId, revisions[1].CreatedByUserId);
        });
    }

    [Theory]
    [InlineData("Hello {{secretValue}}", "body", "not a supported placeholder")]
    [InlineData("Hello {{courseName", "body", "not written correctly")]
    [InlineData("Hello <b>{{courseName}}</b>", "body", "markup")]
    [InlineData("Hello {% if x %}{{courseName}}{% endif %}", "body", "Expressions")]
    [InlineData("Hello ${courseName} {{termName}}", "body", "Expressions")]
    [InlineData("<script>alert(1)</script>{{courseName}}", "body", "markup")]
    [InlineData("   ", "body", "Enter the message text")]
    public async Task Invalid_body_wording_is_rejected_with_a_field_error_and_the_last_valid_revision_stays(string body, string field, string messagePart)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "Subject {{courseName}}", body));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains(messagePart, FirstError(problem, field), StringComparison.OrdinalIgnoreCase);
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route("results-published"));
        Assert.Equal(1, current.GetProperty("revision").GetInt32());
        Assert.Equal(1, await RevisionCountAsync(NotificationEventType.ResultsPublished));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Line one\nLine two")]
    [InlineData("Hi {{nope}}")]
    [InlineData("<i>Hi</i>")]
    public async Task Invalid_subject_wording_is_rejected(string subject)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, subject, "Body {{courseName}}"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "subject"));
    }

    [Fact]
    public async Task Over_long_wording_is_rejected()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route("results-published"),
            Edit(1, new string('s', 201), new string('b', 20_001)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "subject"));
        Assert.True(HasError(problem, "body"));
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("teacher-invitation")]
    [InlineData("student-invitation")]
    [InlineData("email-confirmation")]
    public async Task A_security_template_must_keep_its_action_link(string slug)
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route(slug), Edit(1, "Subject", "There is no link in this message."));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("{{actionLink}}", FirstError(problem, "body"));
    }

    [Fact]
    public async Task A_security_template_accepts_new_wording_that_keeps_the_link_and_other_placeholders()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Put, Route("password-reset"),
            Edit(1, "Your password", "Reset it here: {{ actionLink }} (valid for {{expiresIn}})"));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("revision").GetInt32());
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("teacher-invitation")]
    [InlineData("student-invitation")]
    [InlineData("email-confirmation")]
    public async Task A_security_template_cannot_be_disabled_and_stays_enabled(string slug)
    {
        using var admin = await AdminAsync();
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route(slug));

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route(slug),
            Edit(1, current.GetProperty("subject").GetString()!, current.GetProperty("body").GetString()!, enabled: false));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "enabled"));
        var (_, after) = await SendAsync(admin, HttpMethod.Get, Route(slug));
        Assert.True(after.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, after.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task An_ordinary_template_can_be_disabled_and_later_matching_events_are_suppressed_without_a_backlog_on_re_enabling()
    {
        using var admin = await AdminAsync();
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route("results-published"));
        var (subject, text) = (current.GetProperty("subject").GetString()!, current.GetProperty("body").GetString()!);

        var (disabled, body) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, subject, text, enabled: false));
        var student = await Host.CreateRecipientAsync();
        var suppressed = await Host.RequestAsync(Results("pub-22", student));
        var (enabled, _) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, subject, text, enabled: true));
        await Host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.OK, disabled);
        Assert.False(body.GetProperty("enabled").GetBoolean());
        Assert.Equal(1, body.GetProperty("revision").GetInt32());
        Assert.Equal(1, suppressed.Suppressed);
        Assert.Equal(HttpStatusCode.OK, enabled);
        Assert.Empty(Host.Emails.Messages);
        var record = Assert.Single(await Host.RecordsAsync());
        Assert.Equal(NotificationStatus.Suppressed, (await Host.StateAsync(record.Id)).Status);
        Assert.Equal(1, await RevisionCountAsync(NotificationEventType.ResultsPublished));
    }

    [Fact]
    public async Task A_stale_revision_is_refused_with_a_conflict_and_the_current_revision_is_kept()
    {
        using var admin = await AdminAsync();
        await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "First {{courseName}}", "First {{termName}}"));

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "Second {{courseName}}", "Second {{termName}}"));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("StaleRevision", problem.GetProperty("code").GetString());
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route("results-published"));
        Assert.Equal("First {{courseName}}", current.GetProperty("subject").GetString());
        Assert.Equal(2, current.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task The_revision_being_edited_and_the_enabled_state_are_required()
    {
        using var admin = await AdminAsync();

        var (status, problem) = await SendAsync(admin, HttpMethod.Put, Route("results-published"), new { subject = "S {{courseName}}", body = "B {{termName}}" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.True(HasError(problem, "expectedRevision"));
        Assert.True(HasError(problem, "enabled"));
    }

    [Fact]
    public async Task Saving_without_a_change_creates_no_revision_and_no_audit_event()
    {
        using var admin = await AdminAsync();
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route("results-published"));

        var (status, body) = await SendAsync(admin, HttpMethod.Put, Route("results-published"),
            Edit(1, current.GetProperty("subject").GetString()!, current.GetProperty("body").GetString()!));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("revision").GetInt32());
        Assert.Equal(1, await RevisionCountAsync(NotificationEventType.ResultsPublished));
        Assert.Empty(await Host.AuditEventsAsync("notification.template.updated"));
    }

    [Fact]
    public async Task A_template_change_is_audited_safely_without_the_wording()
    {
        using var admin = await AdminAsync();

        await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "Confidential subject {{courseName}}", "Confidential body {{termName}}", enabled: false));

        var audit = Assert.Single(await Host.AuditEventsAsync("notification.template.updated"));
        Assert.Equal(AdminUserId, audit.ActorUserId);
        Assert.Equal("NotificationTemplate", audit.EntityType);
        Assert.Contains("ResultsPublished", audit.MetadataJson);
        Assert.Contains("revision", audit.ChangesJson);
        Assert.Contains("enabled", audit.ChangesJson);
        Assert.DoesNotContain("Confidential", audit.ChangesJson);
        Assert.DoesNotContain("Confidential", audit.MetadataJson);
    }

    [Fact]
    public async Task A_failed_edit_leaves_no_audit_event()
    {
        using var admin = await AdminAsync();

        await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(1, "S {{bad}}", "B"));
        await SendAsync(admin, HttpMethod.Put, Route("password-reset"), Edit(1, "S", "B", enabled: false));
        await SendAsync(admin, HttpMethod.Put, Route("results-published"), Edit(9, "S {{courseName}}", "B {{termName}}"));

        Assert.Empty(await Host.AuditEventsAsync("notification.template.updated"));
    }

    [Fact]
    public async Task A_preview_uses_synthetic_values_sends_nothing_and_stores_nothing()
    {
        using var admin = await AdminAsync();
        var revisionsBefore = await RevisionCountAsync(NotificationEventType.ResultsPublished);

        var (status, body) = await SendAsync(admin, HttpMethod.Post, $"{Route("results-published")}/previews",
            new { subject = "Results for {{courseName}}", body = "{{courseName}} / {{termName}} / {{portalUrl}}" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Results for Sample Course", body.GetProperty("subject").GetString());
        Assert.Equal("Sample Course / Sample Term / https://portal.example.invalid", body.GetProperty("body").GetString());
        Assert.True(body.GetProperty("synthetic").GetBoolean());
        await Host.FlushNotificationsAsync();
        Assert.Equal(0, Host.Emails.Attempts);
        Assert.Empty(await Host.RecordsAsync());
        Assert.Equal(revisionsBefore, await RevisionCountAsync(NotificationEventType.ResultsPublished));
        Assert.Empty(await Host.AuditEventsAsync("notification.template.updated"));
    }

    [Fact]
    public async Task An_ordinary_preview_may_use_Admin_supplied_sample_values()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Post, $"{Route("feedback-window-opened")}/previews",
            new { subject = "{{courseName}}", body = "{{windowName}} closes {{closesAtUtc}}", sampleValues = new { courseName = "CS 101", windowName = "Week 5" } });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("CS 101", body.GetProperty("subject").GetString());
        Assert.StartsWith("Week 5 closes ", body.GetProperty("body").GetString());
    }

    [Theory]
    [InlineData("password-reset")]
    [InlineData("teacher-invitation")]
    [InlineData("student-invitation")]
    [InlineData("email-confirmation")]
    public async Task A_security_preview_shows_only_a_visibly_synthetic_nonfunctional_link_even_if_a_real_looking_one_is_supplied(string slug)
    {
        using var admin = await AdminAsync();
        var (_, current) = await SendAsync(admin, HttpMethod.Get, Route(slug));

        var (status, body) = await SendAsync(admin, HttpMethod.Post, $"{Route(slug)}/previews",
            new
            {
                subject = current.GetProperty("subject").GetString(),
                body = current.GetProperty("body").GetString(),
                sampleValues = new { actionLink = "https://evil.example/reset?proof=REAL" },
            });

        Assert.Equal(HttpStatusCode.OK, status);
        var text = body.GetProperty("body").GetString()!;
        Assert.Contains(NotificationTemplateCatalog.SyntheticActionLink, text);
        Assert.DoesNotContain("evil.example", text);
        Assert.DoesNotContain("proof=", text);
        Assert.DoesNotContain("http", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, Host.Emails.Attempts);
        Assert.Empty(await Host.RecordsAsync());
        await Host.WithDbAsync(async db => Assert.Empty(await db.PasswordRecoveryRequests.ToListAsync()));
    }

    [Fact]
    public async Task A_preview_applies_the_same_validation_as_saving()
    {
        using var admin = await AdminAsync();

        var (unsupported, unsupportedProblem) = await SendAsync(admin, HttpMethod.Post, $"{Route("results-published")}/previews",
            new { subject = "S", body = "{{unknown}}" });
        var (noLink, noLinkProblem) = await SendAsync(admin, HttpMethod.Post, $"{Route("password-reset")}/previews",
            new { subject = "S", body = "no link" });
        var (badSample, badSampleProblem) = await SendAsync(admin, HttpMethod.Post, $"{Route("results-published")}/previews",
            new { subject = "S {{courseName}}", body = "B", sampleValues = new { evil = "x", courseName = "a\nb" } });

        Assert.Equal(HttpStatusCode.BadRequest, unsupported);
        Assert.True(HasError(unsupportedProblem, "body"));
        Assert.Equal(HttpStatusCode.BadRequest, noLink);
        Assert.True(HasError(noLinkProblem, "body"));
        Assert.Equal(HttpStatusCode.BadRequest, badSample);
        Assert.Equal(2, badSampleProblem.GetProperty("errors").GetProperty("sampleValues").GetArrayLength());
    }

    [Fact]
    public async Task Markup_in_sample_values_is_treated_as_plain_text_and_never_interpreted()
    {
        using var admin = await AdminAsync();

        var (status, body) = await SendAsync(admin, HttpMethod.Post, $"{Route("results-published")}/previews",
            new { subject = "S {{courseName}}", body = "{{courseName}}", sampleValues = new { courseName = "<script>x</script> {{termName}}" } });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("<script>x</script> {{termName}}", body.GetProperty("body").GetString());
    }

    [Fact]
    public async Task Unknown_fields_in_a_template_request_are_rejected()
    {
        using var admin = await AdminAsync();

        var (status, _) = await SendAsync(admin, HttpMethod.Put, Route("results-published"),
            new { expectedRevision = 1, subject = "S {{courseName}}", body = "B {{termName}}", enabled = true, recipient = "x@iit.test", channel = "Sms" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task Every_template_route_denies_anonymous_callers_and_every_non_Admin_active_role()
    {
        var routes = new (HttpMethod Method, string Url, object? Body)[]
        {
            (HttpMethod.Get, Templates, null),
            (HttpMethod.Get, Route("password-reset"), null),
            (HttpMethod.Put, Route("results-published"), Edit(1, "S {{courseName}}", "B {{termName}}")),
            (HttpMethod.Post, $"{Route("results-published")}/previews", new { subject = "S", body = "B" }),
        };
        using var anonymous = Host.CreatePortalClient();
        var email = PortalApiFactory.UniqueEmail("both");
        await Host.CreateUserAsync(email, PortalRoles.Admin, PortalRoles.Teacher);
        await Host.SetDefaultRoleAsync(email, PortalRoles.Teacher);
        using var adminActingAsTeacher = Host.CreatePortalClient();
        await adminActingAsTeacher.SignInSuccessfullyAsync(email);

        foreach (var (method, url, requestBody) in routes)
        {
            using var denied = await anonymous.SendAsync(method, url, requestBody);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            foreach (var role in new[] { PortalRoles.Student, PortalRoles.Teacher, PortalRoles.Coordinator })
            {
                var emailForRole = PortalApiFactory.UniqueEmail(role.ToLowerInvariant());
                await Host.CreateUserAsync(emailForRole, role);
                using var client = Host.CreatePortalClient();
                await client.SignInSuccessfullyAsync(emailForRole);
                using var forbidden = await client.SendAsync(method, url, requestBody);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.DoesNotContain("Reset your IIT Academic Portal password", await forbidden.Content.ReadAsStringAsync());
            }

            using var wrongActiveRole = await adminActingAsTeacher.SendAsync(method, url, requestBody);
            Assert.Equal(HttpStatusCode.Forbidden, wrongActiveRole.StatusCode);
        }

        // Nothing was changed by the denied attempts.
        Assert.Equal(1, await RevisionCountAsync(NotificationEventType.ResultsPublished));
    }

    [Fact]
    public async Task A_template_write_without_the_anti_forgery_token_is_rejected()
    {
        using var admin = await AdminAsync();

        using var response = await admin.SendAsync(
            HttpMethod.Put, Route("results-published"), Edit(1, "S {{courseName}}", "B {{termName}}"), withAntiforgery: false);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, await RevisionCountAsync(NotificationEventType.ResultsPublished));
    }
}
