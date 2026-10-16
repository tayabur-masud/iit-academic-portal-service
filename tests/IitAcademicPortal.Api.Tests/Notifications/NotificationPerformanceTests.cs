using System.Diagnostics;
using System.Net;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Notifications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>
/// Repeatable validation of SC-002 and SC-003 against a fixed workload. Nothing here is random except the generated addresses, so the same
/// numbers come out on the same machine and the thresholds leave a wide margin.
///
/// <para><b>SC-002 workload</b> (originating-action overhead under an email outage): 110 sequential Admin teacher creations per
/// configuration (the first 10 are warm-up and not counted), each of which requests a teacher invitation after it commits; and 110
/// sequential <c>ResultsPublished</c> producer calls for one recipient. Each runs against a fresh host in two configurations: notifications
/// disabled (a no-op request service) and notifications enabled while every provider call fails with a retryable error. The worker
/// is driven every 10 actions in the outage run so retries are happening during the measurement. The threshold is the 95th-percentile
/// time with notifications enabled minus the same percentile with them disabled, at most 200 ms, and every action must keep its normal
/// outcome (201 and 202).</para>
///
/// <para><b>SC-003 workload</b> (time to provider submission, healthy provider): 500 immediate notifications (5 events of 100 recipients each,
/// accepted together) delivered by a worker that behaves exactly like the real one: 50 per pass, and after a pass that did not fill the batch it
/// waits the 5-second poll interval (the test clock advances 5 s; a full batch goes straight on). Latency is the time from acceptance to the
/// provider accepting the message, on the host clock (real time plus the simulated waits). At least 95% must be within 60 seconds. The default
/// worker drains at most 50 notifications per pass, so a burst of N takes about N/50 passes back to back; that bound, not the database, is
/// what limits throughput, and is stated in the quickstart.</para>
/// </summary>
public sealed class NotificationPerformanceTests(ITestOutputHelper output)
{
    private const int Warmup = 10;
    private const int Measured = 100;
    private static readonly TimeSpan MaxOverhead = TimeSpan.FromMilliseconds(200);

    /// <summary>Notifications switched off: the request returns at once and stores nothing.</summary>
    private sealed class NoNotifications : INotificationRequestService
    {
        public Task<NotificationRequestResult> RequestAsync(NotificationEventRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new NotificationRequestResult(NotificationRequestStatus.Accepted));
    }

    private sealed class DisabledHost : NotificationTestHost
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<INotificationRequestService>();
                services.AddScoped<INotificationRequestService, NoNotifications>();
            });
        }
    }

    private static TimeSpan Percentile95(IReadOnlyList<TimeSpan> samples)
    {
        var sorted = samples.Order().ToList();
        return sorted[(int)Math.Ceiling(0.95 * sorted.Count) - 1];
    }

    private static async Task<(PortalClient Admin, string AdminEmail)> SignedInAdminAsync(NotificationTestHost host)
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        await host.CreateUserAsync(email, PortalRoles.Admin);
        var client = host.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return (client, email);
    }

    /// <summary>Creates teachers one after another and returns how long each took. Every one must succeed (201).</summary>
    private static async Task<List<TimeSpan>> TeacherCreationsAsync(NotificationTestHost host, Func<int, Task>? between = null)
    {
        var (admin, _) = await SignedInAdminAsync(host);
        using var _ = admin;
        var times = new List<TimeSpan>();
        for (var i = 0; i < Warmup + Measured; i++)
        {
            var started = Stopwatch.GetTimestamp();
            using var response = await admin.SendAsync(HttpMethod.Post, "/api/admin/teachers", new { displayName = $"Teacher {i}", email = PortalApiFactory.UniqueEmail("t") });
            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            if (i >= Warmup)
            {
                times.Add(elapsed);
            }

            if (between is not null)
            {
                await between(i);
            }
        }

        return times;
    }

    private static async Task<List<TimeSpan>> ProducerCallsAsync(NotificationTestHost host, Func<int, Task>? between = null)
    {
        var student = await host.CreateRecipientAsync();
        var times = new List<TimeSpan>();
        for (var i = 0; i < Warmup + Measured; i++)
        {
            var request = new NotificationEventRequest(
                NotificationEventType.ResultsPublished, $"perf-{i}", [student],
                new Dictionary<string, string> { ["courseName"] = "Databases", ["termName"] = "Fall 2031" });
            var started = Stopwatch.GetTimestamp();
            var result = await host.RequestAsync(request);
            var elapsed = Stopwatch.GetElapsedTime(started);
            Assert.Equal(NotificationRequestStatus.Accepted, result.Status);
            if (i >= Warmup)
            {
                times.Add(elapsed);
            }

            if (between is not null)
            {
                await between(i);
            }
        }

        return times;
    }

    [Fact]
    public async Task During_an_email_outage_creating_a_teacher_stays_successful_and_its_95th_percentile_grows_by_no_more_than_200_ms()
    {
        using var disabled = new DisabledHost();
        var baseline = await TeacherCreationsAsync(disabled);

        using var enabled = new NotificationTestHost();
        enabled.Outage();
        var withOutage = await TeacherCreationsAsync(enabled, async i =>
        {
            // The worker runs during the measurement, failing and scheduling retries, as it would in production.
            if (i % 10 == 9)
            {
                await enabled.FlushNotificationsAsync();
                enabled.Advance(TimeSpan.FromSeconds(30));
            }
        });

        var overhead = Percentile95(withOutage) - Percentile95(baseline);
        output.WriteLine($"Teacher creation, {Measured} measured per run: p95 notifications disabled {Percentile95(baseline).TotalMilliseconds:F1} ms, " +
                         $"p95 during outage {Percentile95(withOutage).TotalMilliseconds:F1} ms, overhead {overhead.TotalMilliseconds:F1} ms (limit {MaxOverhead.TotalMilliseconds:F0} ms)");

        Assert.Equal(Measured, withOutage.Count);
        Assert.True(overhead <= MaxOverhead, $"p95 overhead was {overhead.TotalMilliseconds:F1} ms, over the {MaxOverhead.TotalMilliseconds:F0} ms limit.");
        Assert.Equal(Warmup + Measured, (await enabled.RecordsAsync()).Count);
        Assert.Empty(enabled.Emails.Messages);
    }

    [Fact]
    public async Task During_an_email_outage_a_producer_call_stays_successful_and_its_95th_percentile_grows_by_no_more_than_200_ms()
    {
        using var disabled = new DisabledHost();
        var baseline = await ProducerCallsAsync(disabled);

        using var enabled = new NotificationTestHost();
        enabled.Outage();
        var withOutage = await ProducerCallsAsync(enabled, async i =>
        {
            if (i % 10 == 9)
            {
                await enabled.FlushNotificationsAsync();
                enabled.Advance(TimeSpan.FromSeconds(30));
            }
        });

        var overhead = Percentile95(withOutage) - Percentile95(baseline);
        output.WriteLine($"Producer call, {Measured} measured per run: p95 notifications disabled {Percentile95(baseline).TotalMilliseconds:F1} ms, " +
                         $"p95 during outage {Percentile95(withOutage).TotalMilliseconds:F1} ms, overhead {overhead.TotalMilliseconds:F1} ms (limit {MaxOverhead.TotalMilliseconds:F0} ms)");

        Assert.True(overhead <= MaxOverhead, $"p95 overhead was {overhead.TotalMilliseconds:F1} ms, over the {MaxOverhead.TotalMilliseconds:F0} ms limit.");
        Assert.Equal(Warmup + Measured, (await enabled.RecordsAsync()).Count);
    }

    [Fact]
    public async Task A_rejected_action_stays_rejected_during_an_outage_and_requests_nothing()
    {
        using var host = new NotificationTestHost();
        host.Outage();
        var (admin, _) = await SignedInAdminAsync(host);
        using var _ = admin;
        var email = PortalApiFactory.UniqueEmail("t");
        using var created = await admin.SendAsync(HttpMethod.Post, "/api/admin/teachers", new { displayName = "First", email });
        var before = (await host.RecordsAsync()).Count;

        for (var i = 0; i < 20; i++)
        {
            using var duplicate = await admin.SendAsync(HttpMethod.Post, "/api/admin/teachers", new { displayName = "Again", email });
            Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        }

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(before, (await host.RecordsAsync()).Count);
    }

    [Fact]
    public async Task With_a_healthy_provider_at_least_95_percent_of_500_immediate_notifications_reach_the_provider_within_60_seconds()
    {
        using var host = new NotificationTestHost();
        var settings = host.Services.GetRequiredService<IOptions<NotificationDispatchOptions>>().Value;
        var recipients = new List<string>();
        for (var i = 0; i < 100; i++)
        {
            recipients.Add(await host.CreateRecipientAsync());
        }

        // Five events accepted together: 500 immediate notifications.
        for (var event_ = 0; event_ < 5; event_++)
        {
            var result = await host.RequestAsync(new NotificationEventRequest(
                NotificationEventType.ResultsPublished, $"burst-{event_}", recipients,
                new Dictionary<string, string> { ["courseName"] = $"Course {event_}", ["termName"] = "Fall 2031" }));
            Assert.Equal(100, result.Accepted);
        }

        // A worker that behaves like the real one: a pass claims up to BatchSize; after a pass that did not fill the batch it waits the poll interval.
        var passes = 0;
        var processor = host.Services.GetRequiredService<NotificationDispatchProcessor>();
        while (passes < 100)
        {
            var handled = await processor.ProcessOnceAsync(CancellationToken.None);
            passes++;
            if (handled == 0)
            {
                break;
            }

            if (handled < settings.BatchSize)
            {
                host.Advance(settings.PollInterval);
            }
        }

        var records = await host.RecordsAsync();
        var latencies = new List<TimeSpan>();
        foreach (var record in records)
        {
            var state = await host.StateAsync(record.Id);
            Assert.Equal(NotificationStatus.Sent, state.Status);
            latencies.Add(state.SentAtUtc!.Value - record.RequestedAtUtc);
        }

        var within = latencies.Count(l => l <= TimeSpan.FromSeconds(60));
        var share = within / (double)latencies.Count;
        output.WriteLine($"Burst of {latencies.Count} accepted at once, batch {settings.BatchSize}, poll {settings.PollInterval.TotalSeconds:F0} s: {passes} passes, " +
                         $"p95 {Percentile95(latencies).TotalSeconds:F2} s, max {latencies.Max().TotalSeconds:F2} s, {share:P1} within 60 s");

        Assert.Equal(500, records.Count);
        Assert.Equal(500, host.Emails.Messages.Count);
        Assert.True(share >= 0.95, $"Only {share:P1} reached the provider within 60 seconds.");
    }

    [Fact]
    public void The_default_worker_drains_a_burst_well_inside_60_seconds_by_construction()
    {
        var defaults = new NotificationDispatchOptions();

        // A burst of N takes ceil(N / BatchSize) full passes with no wait between them, then one poll interval to notice it is done.
        // So the largest burst that is guaranteed within 60 s of the last pass starting is far above the 500 used here.
        var perSecond = defaults.BatchSize / defaults.PollInterval.TotalSeconds;
        var burstWithin60Seconds = defaults.BatchSize * (int)(60 / defaults.PollInterval.TotalSeconds);

        output.WriteLine($"Default worker: batch {defaults.BatchSize}, poll {defaults.PollInterval.TotalSeconds:F0} s -> sustained {perSecond:F0} per second when every pass is full; " +
                         $"{burstWithin60Seconds} notifications are guaranteed to be picked up inside 60 s even if every pass waits the full poll interval");
        Assert.True(burstWithin60Seconds >= 500);
        Assert.True(perSecond >= 10);
    }
}
