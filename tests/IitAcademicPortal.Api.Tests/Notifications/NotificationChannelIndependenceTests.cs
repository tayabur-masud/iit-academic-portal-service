using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// SC-009: an existing producer triggers its unchanged event and a second, non-email channel delivers it too, with no change to what the
/// producer sends. The extra channel exists only in these tests: it is a fake adapter plus a template row inserted here. Production has one
/// channel (email), one adapter, and seven email templates, which the last tests pin.
/// </summary>
public sealed class NotificationChannelIndependenceTests
{
    /// <summary>A stand-in for a future channel (SMS, in-app): it records what it is given and can be made to fail.</summary>
    private sealed class FakeChannel : IDeliveryChannel
    {
        public ConcurrentQueue<ChannelMessage> Delivered { get; } = new();

        public int Attempts => Volatile.Read(ref attempts);

        public Func<ChannelMessage, Exception?>? Behavior { get; set; }

        private int attempts;

        public NotificationChannel Channel => NotificationChannel.Test;

        public Task SendAsync(ChannelMessage message, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attempts);
            if (Behavior?.Invoke(message) is { } failure)
            {
                throw failure;
            }

            Delivered.Enqueue(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ChannelHost : NotificationTestHost
    {
        public FakeChannel Fake { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services => services.AddScoped<IDeliveryChannel>(_ => Fake));
        }
    }

    /// <summary>Gives the test channel a template for an event, with the same wording as the email one.</summary>
    private static async Task AddTestChannelTemplateAsync(NotificationTestHost host, NotificationEventType eventType)
    {
        var definition = NotificationTemplateCatalog.For(eventType);
        await host.WithDbAsync(async db =>
        {
            var template = NotificationTemplate.Create(eventType, NotificationChannel.Test);
            var revision = NotificationTemplateRevision.Create(template.Id, 1, definition.DefaultSubject, definition.DefaultBody, definition.Placeholders);
            template.Publish(revision, enabled: true);
            db.NotificationTemplates.Add(template);
            db.NotificationTemplateRevisions.Add(revision);
            await db.SaveChangesAsync();
        });
    }

    private static async Task<(ChannelHost Host, string Student)> ArrangeAsync()
    {
        var host = new ChannelHost();
        await AddTestChannelTemplateAsync(host, NotificationEventType.ResultsPublished);
        return (host, await host.CreateRecipientAsync());
    }

    private static async Task ResultsPublishedAsync(NotificationTestHost host, string student, string occurrence = "publication:ci-1")
    {
        // The existing producer contract, called exactly as it is today: no channel, no provider, no wording.
        await using var scope = host.Services.CreateAsyncScope();
        var producers = scope.ServiceProvider.GetRequiredService<IAcademicNotificationProducers>();
        var result = await producers.ResultsPublishedAsync(occurrence, "Compilers", "Autumn 2031", [student], CancellationToken.None);
        Assert.Equal(NotificationRequestStatus.Accepted, result.Status);
    }

    [Fact]
    public async Task The_same_producer_call_is_delivered_by_email_and_by_the_extra_channel_with_the_same_content()
    {
        var (host, student) = await ArrangeAsync();
        using var _ = host;

        await ResultsPublishedAsync(host, student);
        await host.FlushNotificationsAsync();

        var records = await host.RecordsAsync();
        Assert.Equal([NotificationChannel.Email, NotificationChannel.Test], records.Select(r => r.Channel).Order());
        Assert.All(records, r =>
        {
            Assert.Equal("publication:ci-1", r.OccurrenceId);
            Assert.Equal(student, r.RecipientUserId);
        });
        var email = Assert.Single(host.Emails.Messages);
        var other = Assert.Single(host.Fake.Delivered);
        Assert.Equal(email.To, other.Destination);
        Assert.Equal(email.Subject, other.Subject);
        Assert.Equal(email.Body, other.Text);
        Assert.Contains("Results for Compilers (Autumn 2031) have been published.", other.Text);
        Assert.NotNull(email.HtmlBody);
        foreach (var record in records)
        {
            Assert.Equal(NotificationStatus.Sent, (await host.StateAsync(record.Id)).Status);
        }
    }

    [Fact]
    public async Task What_the_producer_sends_is_identical_with_and_without_the_extra_channel()
    {
        using var plain = new NotificationTestHost();
        var plainStudent = await plain.CreateRecipientAsync();
        var (extra, extraStudent) = await ArrangeAsync();
        using var _ = extra;

        await ResultsPublishedAsync(plain, plainStudent, "publication:same");
        await ResultsPublishedAsync(extra, extraStudent, "publication:same");

        var withoutExtra = Assert.Single(await plain.RecordsAsync());
        var emailWithExtra = (await extra.RecordsAsync()).Single(r => r.Channel == NotificationChannel.Email);
        Assert.Equal(
            (withoutExtra.EventType, withoutExtra.OccurrenceId, withoutExtra.Subject, withoutExtra.Body, withoutExtra.ContentWithheld),
            (emailWithExtra.EventType, emailWithExtra.OccurrenceId, emailWithExtra.Subject, emailWithExtra.Body, emailWithExtra.ContentWithheld));
    }

    [Fact]
    public void The_producer_contracts_have_no_way_to_name_a_channel_so_adding_one_cannot_change_a_producer()
    {
        var parameters = typeof(IAcademicNotificationProducers).GetMethods().SelectMany(m => m.GetParameters()).Select(p => p.ParameterType);
        var members = typeof(NotificationEventRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.PropertyType);

        Assert.DoesNotContain(parameters.Concat(members), t => t == typeof(NotificationChannel) || t == typeof(IDeliveryChannel));
        Assert.DoesNotContain(typeof(INotificationRequestService).GetMethods().SelectMany(m => m.GetParameters()), p => p.ParameterType == typeof(NotificationChannel));
    }

    [Fact]
    public async Task An_existing_account_feature_triggers_the_extra_channel_too_and_neither_channel_stores_content_or_proof()
    {
        var host = new ChannelHost();
        using var _ = host;
        await AddTestChannelTemplateAsync(host, NotificationEventType.PasswordReset);
        var email = PortalApiFactory.UniqueEmail("student");
        var userId = await host.CreateUserAsync(email, PortalRoles.Student);
        using var client = host.CreatePortalClient();

        // Password recovery's public request and response are unchanged; only the number of notifications it produces differs.
        var proof = await PasswordRecoveryTests.RequestProofAsync(host, client, email);
        await host.FlushNotificationsAsync();

        var records = (await host.RecordsAsync()).Where(r => r.RecipientUserId == userId).ToList();
        Assert.Equal(2, records.Count);
        Assert.All(records, r =>
        {
            Assert.True(r.ContentWithheld);
            Assert.Null(r.Subject);
            Assert.Null(r.Body);
        });
        Assert.Single(host.Fake.Delivered);
        Assert.Contains("reset-password?email=", host.Fake.Delivered.Single().Text);
        Assert.DoesNotContain(proof, host.Logs.Everything);
    }

    [Fact]
    public async Task A_replay_creates_nothing_new_on_either_channel()
    {
        var (host, student) = await ArrangeAsync();
        using var _ = host;

        await ResultsPublishedAsync(host, student, "publication:replay");
        await ResultsPublishedAsync(host, student, "publication:replay");
        await host.FlushNotificationsAsync();
        await ResultsPublishedAsync(host, student, "publication:replay");
        await host.FlushNotificationsAsync();

        Assert.Equal(2, (await host.RecordsAsync()).Count);
        Assert.Single(host.Emails.Messages);
        Assert.Single(host.Fake.Delivered);
    }

    [Fact]
    public async Task A_failure_on_one_channel_never_affects_the_other()
    {
        var (host, student) = await ArrangeAsync();
        using var _ = host;
        host.Fake.Behavior = _ => NotificationTestHost.Permanent("Rejected");
        await ResultsPublishedAsync(host, student, "publication:isolated-1");
        await host.FlushNotificationsAsync();

        var first = await host.RecordsAsync();
        var emailState = await host.StateAsync(first.Single(r => r.Channel == NotificationChannel.Email).Id);
        var otherState = await host.StateAsync(first.Single(r => r.Channel == NotificationChannel.Test).Id);
        Assert.Equal(NotificationStatus.Sent, emailState.Status);
        Assert.Equal(NotificationStatus.Failed, otherState.Status);
        Assert.Equal("Rejected", otherState.FailureCode);

        host.Fake.Behavior = null;
        host.Outage();
        var second = await host.CreateRecipientAsync();
        await ResultsPublishedAsync(host, second, "publication:isolated-2");
        await host.FlushNotificationsAsync();

        var records = (await host.RecordsAsync()).Where(r => r.RecipientUserId == second).ToList();
        Assert.Equal(NotificationStatus.Retrying, (await host.StateAsync(records.Single(r => r.Channel == NotificationChannel.Email).Id)).Status);
        Assert.Equal(NotificationStatus.Sent, (await host.StateAsync(records.Single(r => r.Channel == NotificationChannel.Test).Id)).Status);
    }

    [Fact]
    public async Task A_failed_notification_on_the_extra_channel_is_resent_on_that_channel_only()
    {
        var (host, student) = await ArrangeAsync();
        using var _ = host;
        host.Fake.Behavior = _ => NotificationTestHost.Permanent("Rejected");
        await ResultsPublishedAsync(host, student, "publication:resend");
        await host.FlushNotificationsAsync();
        var failed = (await host.RecordsAsync()).Single(r => r.Channel == NotificationChannel.Test);
        host.Fake.Behavior = null;
        var adminEmail = PortalApiFactory.UniqueEmail("admin");
        await host.CreateUserAsync(adminEmail, PortalRoles.Admin);
        using var admin = host.CreatePortalClient();
        await admin.SignInSuccessfullyAsync(adminEmail);

        using var response = await admin.SendAsync(
            HttpMethod.Post, $"/api/admin/notifications/{failed.Id}/resends", headers: new Dictionary<string, string> { ["Idempotency-Key"] = "k-1" });
        await host.FlushNotificationsAsync();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var child = (await host.RecordsAsync()).Single(r => r.ParentNotificationId == failed.Id);
        Assert.Equal(NotificationChannel.Test, child.Channel);
        Assert.Single(host.Fake.Delivered);
        Assert.Single(host.Emails.Messages);
        Assert.Equal("Test", (await PortalClientJson(admin, $"/api/admin/notifications/{child.Id}")).GetProperty("notification").GetProperty("channel").GetString());
    }

    private static async Task<System.Text.Json.JsonElement> PortalClientJson(PortalClient client, string url) =>
        System.Text.Json.JsonDocument.Parse(await (await client.GetAsync(url)).Content.ReadAsStringAsync()).RootElement.Clone();

    // --- Production stays email-only ---

    [Fact]
    public async Task A_channel_template_without_a_registered_adapter_creates_no_notification()
    {
        using var host = new NotificationTestHost();
        await AddTestChannelTemplateAsync(host, NotificationEventType.ResultsPublished);
        var student = await host.CreateRecipientAsync();

        await ResultsPublishedAsync(host, student);
        await host.FlushNotificationsAsync();

        Assert.Equal(NotificationChannel.Email, Assert.Single(await host.RecordsAsync()).Channel);
        Assert.Single(host.Emails.Messages);
    }

    [Fact]
    public async Task A_notification_for_a_channel_with_no_adapter_fails_visibly_instead_of_being_lost()
    {
        using var host = new NotificationTestHost();
        var student = await host.CreateRecipientAsync();
        var revisionId = NotificationTemplateCatalog.InitialRevisionId(NotificationEventType.ResultsPublished);
        var orphan = NotificationRecord.Create(
            NotificationEventType.ResultsPublished, "orphan", student, "x@iit.test", revisionId, DateTime.UtcNow, "S", "B", channel: NotificationChannel.Test);
        await host.WithDbAsync(async db =>
        {
            db.NotificationRecords.Add(orphan);
            db.NotificationDispatchStates.Add(NotificationDispatchState.Create(orphan.Id, NotificationStatus.Pending, DateTime.UtcNow.AddSeconds(-5)));
            await db.SaveChangesAsync();
        });

        await host.FlushNotificationsAsync();

        var state = await host.StateAsync(orphan.Id);
        Assert.Equal(NotificationStatus.Failed, state.Status);
        Assert.Equal("ChannelUnavailable", state.FailureCode);
    }

    [Fact]
    public async Task Production_delivers_through_exactly_one_channel_email_and_seeds_only_email_templates()
    {
        using var host = new NotificationTestHost();
        using var admin = host.CreatePortalClient();
        var adminEmail = PortalApiFactory.UniqueEmail("admin");
        await host.CreateUserAsync(adminEmail, PortalRoles.Admin);
        await admin.SignInSuccessfullyAsync(adminEmail);

        await using var scope = host.Services.CreateAsyncScope();
        var channels = scope.ServiceProvider.GetServices<IDeliveryChannel>().ToList();
        await host.WithDbAsync(async db =>
        {
            Assert.Equal(7, await db.NotificationTemplates.CountAsync());
            Assert.All(await db.NotificationTemplates.AsNoTracking().ToListAsync(), t => Assert.Equal(NotificationChannel.Email, t.Channel));
        });

        Assert.Equal(NotificationChannel.Email, Assert.Single(channels).Channel);
        Assert.DoesNotContain(NotificationTemplateCatalog.All, d => d.GetType().GetProperties().Any(p => p.PropertyType == typeof(NotificationChannel)));
        using var listed = await admin.GetAsync("/api/admin/notification-templates");
        var templates = System.Text.Json.JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(7, templates.GetArrayLength());
        Assert.All(templates.EnumerateArray(), t => Assert.Equal("Email", t.GetProperty("channel").GetString()));
    }

    [Fact]
    public async Task The_admin_template_screens_list_only_email_templates_even_when_another_channel_has_one()
    {
        var (host, _) = await ArrangeAsync();
        using var _ = host;
        var adminEmail = PortalApiFactory.UniqueEmail("admin");
        await host.CreateUserAsync(adminEmail, PortalRoles.Admin);
        using var admin = host.CreatePortalClient();
        await admin.SignInSuccessfullyAsync(adminEmail);

        using var listed = await admin.GetAsync("/api/admin/notification-templates");
        using var one = await admin.GetAsync("/api/admin/notification-templates/results-published/email");

        var templates = System.Text.Json.JsonDocument.Parse(await listed.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(7, templates.GetArrayLength());
        Assert.Equal("Email", System.Text.Json.JsonDocument.Parse(await one.Content.ReadAsStringAsync()).RootElement.GetProperty("channel").GetString());
    }
}
