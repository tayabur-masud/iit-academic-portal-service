using System.Reflection;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// The contract the owners of results publication and feedback windows call after their action commits. These workflows do not exist
/// in this service, so the owners here are fakes. These tests prove the notification side of the contract only: they do NOT show that
/// a real publication or feedback workflow calls it. Until each owner integrates it and passes its own acceptance tests, the
/// portal-wide behavior is not complete (spec FR-024).
/// </summary>
public sealed class AcademicNotificationProducerTests : NotificationTestBase
{
    /// <summary>A stand-in for the course and results owner: it notifies only when a publication has committed.</summary>
    private sealed class FakeResultsOwner(IAcademicNotificationProducers producers, IReadOnlyList<string> entitledStudents)
    {
        public bool Published { get; private set; }

        /// <summary>Finalizing results is not publishing them, so it must emit nothing.</summary>
        public void FinalizeResults() { }

        public async Task<NotificationRequestResult> PublishAsync(string publicationOccurrence)
        {
            Published = true;
            return await producers.ResultsPublishedAsync(publicationOccurrence, "Operating Systems", "Spring 2031", entitledStudents, CancellationToken.None);
        }
    }

    private IAcademicNotificationProducers Producers(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IAcademicNotificationProducers>();

    [Fact]
    public async Task Finalization_without_publication_sends_nothing_and_a_committed_publication_notifies_only_entitled_students()
    {
        var entitled = new[] { await Host.CreateRecipientAsync(), await Host.CreateRecipientAsync() };
        var other = await Host.CreateRecipientAsync();
        await using var scope = Host.Services.CreateAsyncScope();
        var owner = new FakeResultsOwner(Producers(scope), entitled);

        owner.FinalizeResults();
        await Host.FlushNotificationsAsync();
        Assert.Empty(await Host.RecordsAsync());
        Assert.Empty(Host.Emails.Messages);

        var result = await owner.PublishAsync("publication:os-spring-2031");
        await Host.FlushNotificationsAsync();

        Assert.Equal(2, result.Accepted);
        Assert.Equal(2, Host.Emails.Messages.Count);
        Assert.DoesNotContain(Host.Emails.Messages, m => m.To == Host.EmailOfAsync(other).Result);
        var records = await Host.RecordsAsync();
        Assert.All(records, r =>
        {
            Assert.Equal(NotificationEventType.ResultsPublished, r.EventType);
            Assert.Equal("publication:os-spring-2031", r.OccurrenceId);
        });
        Assert.Equal(entitled.Order(), records.Select(r => r.RecipientUserId).Order());
    }

    [Fact]
    public async Task The_results_message_points_to_the_portal_and_contains_no_marks_or_other_students_results()
    {
        var student = await Host.CreateRecipientAsync();
        await using var scope = Host.Services.CreateAsyncScope();

        await new FakeResultsOwner(Producers(scope), [student]).PublishAsync("publication:p1");
        await Host.FlushNotificationsAsync();

        var message = Assert.Single(Host.Emails.Messages);
        Assert.Contains("Operating Systems (Spring 2031)", message.Body);
        Assert.Contains("https://portal.test", message.Body);
        Assert.DoesNotContain("grade", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("mark", message.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("GPA", message.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Replaying_the_same_publication_creates_no_extra_notification()
    {
        var student = await Host.CreateRecipientAsync();
        await using var scope = Host.Services.CreateAsyncScope();
        var owner = new FakeResultsOwner(Producers(scope), [student]);

        await owner.PublishAsync("publication:p2");
        var replay = await owner.PublishAsync("publication:p2");
        await Host.FlushNotificationsAsync();

        Assert.Equal(1, replay.Duplicates);
        Assert.Single(await Host.RecordsAsync());
        Assert.Single(Host.Emails.Messages);
    }

    [Fact]
    public async Task A_feedback_window_opening_notifies_its_eligible_students_with_course_and_window_information_only()
    {
        var students = new[] { await Host.CreateRecipientAsync(), await Host.CreateRecipientAsync() };
        var close = new DateTime(2031, 5, 20, 17, 30, 0, DateTimeKind.Utc);
        await using var scope = Host.Services.CreateAsyncScope();

        var result = await Producers(scope).FeedbackWindowOpenedAsync("window:db-week-5", "Databases", "Week 5 feedback", close, students, CancellationToken.None);
        await Host.FlushNotificationsAsync();

        Assert.Equal(2, result.Accepted);
        Assert.Equal(2, Host.Emails.Messages.Count);
        Assert.All(Host.Emails.Messages, m =>
        {
            Assert.Equal("Feedback is open for Databases", m.Subject);
            Assert.Contains("Week 5 feedback", m.Body);
            Assert.Contains("2031-05-20 17:30 UTC", m.Body);
            Assert.DoesNotContain("submitted", m.Body, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task The_producer_contracts_have_no_input_for_marks_feedback_or_submission_state_and_no_channel_choice()
    {
        var forbidden = new[] { "mark", "grade", "score", "submi", "rating", "answer", "response", "comment", "feedbacktext", "channel" };
        var contracts = new[] { typeof(IAcademicNotificationProducers), typeof(NotificationEventRequest) };

        foreach (var type in contracts)
        {
            var names = type.GetMembers(BindingFlags.Public | BindingFlags.Instance)
                .Select(m => m.Name)
                .Concat(type.GetMethods().SelectMany(m => m.GetParameters().Select(p => p.Name ?? string.Empty)))
                .Select(n => n.ToLowerInvariant())
                .ToList();
            Assert.All(names, n => Assert.DoesNotContain(forbidden, f => n.Contains(f)));
        }

        // The context a producer may supply for ordinary events is fixed to labels and times.
        var allowed = NotificationTemplateCatalog.All.Where(d => !d.IsSecurity).SelectMany(d => d.ProducerContextKeys).Distinct().Order().ToList();
        Assert.Equal(["closesAtUtc", "courseName", "termName", "windowName"], allowed);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_request_carrying_feedback_data_in_its_context_is_refused_and_stores_nothing()
    {
        var student = await Host.CreateRecipientAsync();

        var result = await Host.RequestAsync(new NotificationEventRequest(
            NotificationEventType.FeedbackWindowOpened,
            "window:x",
            [student],
            new Dictionary<string, string>
            {
                ["courseName"] = "Databases", ["windowName"] = "W", ["closesAtUtc"] = "2031-01-01 00:00 UTC", ["submissionState"] = "submitted",
            }));

        Assert.Equal(NotificationRequestStatus.Invalid, result.Status);
        Assert.Empty(await Host.RecordsAsync());
    }

    [Fact]
    public async Task The_same_event_request_is_unchanged_whichever_provider_delivers_it()
    {
        // A producer states the event and recipients; it never names a channel or provider. Delivering through a different provider
        // (here the scripted fake, standing in for any adapter) needs no change to the request.
        var student = await Host.CreateRecipientAsync();
        var request = Results("publication:same", student);

        await Host.RequestAsync(request);
        await Host.FlushNotificationsAsync();

        Assert.Single(Host.Emails.Messages);
        Assert.Null(typeof(NotificationEventRequest).GetProperty("Channel"));
        Assert.Null(typeof(NotificationEventRequest).GetProperty("Provider"));
    }

    [Fact]
    public async Task The_academic_producers_are_contract_only_until_their_owners_integrate_them()
    {
        // No production code outside the notification capability calls the academic producers yet. This test fails the day one does,
        // as a reminder that the integration must come with that owner's own acceptance tests (FR-024).
        var callers = typeof(IAcademicNotificationProducers).Assembly.GetTypes()
            .Where(t => t.Namespace != typeof(AcademicNotificationProducers).Namespace)
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .Where(p => p.ParameterType == typeof(IAcademicNotificationProducers))
            .ToList();

        Assert.Empty(callers);
        await Task.CompletedTask;
    }
}
