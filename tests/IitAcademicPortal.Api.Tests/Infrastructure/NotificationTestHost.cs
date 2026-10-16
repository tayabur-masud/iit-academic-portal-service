using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using IitAcademicPortal.Infrastructure.Notifications;
using IitAcademicPortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IitAcademicPortal.Api.Tests.Infrastructure;

/// <summary>Captures the operational signals notification work raises, so tests can prove the alarm fires and carries no secret.</summary>
public sealed class RecordingFailureSignal : INotificationFailureSignal
{
    public ConcurrentQueue<string> PersistenceFailures { get; } = new();

    public ConcurrentQueue<string> Exhausted { get; } = new();

    public void PersistenceFailed(NotificationEventType eventType, string occurrenceId, int recipientCount, string failureCode) =>
        PersistenceFailures.Enqueue($"{eventType}|{occurrenceId}|{recipientCount}|{failureCode}");

    public void DeliveryExhausted(Guid notificationId, NotificationEventType eventType, string failureCode) =>
        Exhausted.Enqueue($"{notificationId}|{eventType}|{failureCode}");
}

/// <summary>The failure switch of one test host. Shared by that host's stores, which are created per request scope.</summary>
public sealed class NotificationFault
{
    public volatile bool FailAccept;
}

/// <summary>Wraps the real store so a test can make accepting new work fail, as a database outage would.</summary>
public sealed class FailableNotificationStore(INotificationStore inner, NotificationFault fault) : INotificationStore
{
    private bool FailAccept => fault.FailAccept;

    public Task<IReadOnlyList<TemplateSnapshot>> GetTemplatesAsync(NotificationEventType eventType, CancellationToken cancellationToken) =>
        inner.GetTemplatesAsync(eventType, cancellationToken);

    public Task<AcceptResult> AcceptAsync(NotificationRecord record, NotificationDispatchState state, CancellationToken cancellationToken) =>
        FailAccept ? throw new InvalidOperationException("The notification store is unavailable.") : inner.AcceptAsync(record, state, cancellationToken);

    public Task<ClaimResult> ClaimDueAsync(DateTime nowUtc, TimeSpan lease, int batchSize, CancellationToken cancellationToken) =>
        inner.ClaimDueAsync(nowUtc, lease, batchSize, cancellationToken);

    public Task<bool> BeginSendAsync(Guid notificationId, DateTime nowUtc, CancellationToken cancellationToken) =>
        inner.BeginSendAsync(notificationId, nowUtc, cancellationToken);

    public Task CompleteAttemptAsync(AttemptCompletion completion, CancellationToken cancellationToken) =>
        inner.CompleteAttemptAsync(completion, cancellationToken);

    public Task AddRecoveryRequestAsync(PasswordRecoveryRequest request, CancellationToken cancellationToken) =>
        FailAccept ? throw new InvalidOperationException("The notification store is unavailable.") : inner.AddRecoveryRequestAsync(request, cancellationToken);

    public Task<int> DeleteExpiredRecoveryRequestsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
        inner.DeleteExpiredRecoveryRequestsAsync(nowUtc, cancellationToken);

    public Task<IReadOnlyList<RecoveryRequestClaim>> ClaimRecoveryRequestsAsync(
        DateTime nowUtc, TimeSpan lease, int batchSize, int maxAttempts, CancellationToken cancellationToken) =>
        inner.ClaimRecoveryRequestsAsync(nowUtc, lease, batchSize, maxAttempts, cancellationToken);

    public Task DeleteRecoveryRequestAsync(Guid requestId, CancellationToken cancellationToken) =>
        inner.DeleteRecoveryRequestAsync(requestId, cancellationToken);
}

/// <summary>
/// A fake feedback-window owner. It reports only what a real owner may: occurrence, labels, close time, status, and the eligible
/// students. Feedback submissions are not part of it, so the scheduler cannot depend on them.
/// </summary>
public sealed class FakeFeedbackWindowSource : IFeedbackWindowSource
{
    private readonly object gate = new();
    private List<FeedbackWindowSnapshot> windows = [];

    public void Set(params FeedbackWindowSnapshot[] snapshots)
    {
        lock (gate)
        {
            windows = [.. snapshots];
        }
    }

    public Task<IReadOnlyList<FeedbackWindowSnapshot>> GetWindowsAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<FeedbackWindowSnapshot>>([.. windows]);
        }
    }
}

/// <summary>
/// A real API host for notification tests: the worker is off and driven by <see cref="PortalApiFactory.FlushNotificationsAsync"/>,
/// time is controllable through <see cref="PortalApiFactory.Clock"/>, Data Protection keys are isolated, the provider is a scripted
/// fake (<see cref="PortalApiFactory.Emails"/>) so no Brevo credentials are needed, and store failures and signals are observable.
/// </summary>
public class NotificationTestHost : PortalApiFactory
{
    public RecordingFailureSignal Signals { get; } = new();

    /// <summary>The feedback-window owner used by the reminder scheduler in this host.</summary>
    public FakeFeedbackWindowSource Windows { get; } = new();

    /// <summary>Set <c>Fault.FailAccept</c> to make accepting new work fail, as a database outage would.</summary>
    public NotificationFault Fault { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationFailureSignal>();
            services.AddSingleton<INotificationFailureSignal>(Signals);

            services.RemoveAll<IFeedbackWindowSource>();
            services.AddSingleton<IFeedbackWindowSource>(Windows);

            services.RemoveAll<INotificationStore>();
            services.AddScoped<INotificationStore>(provider =>
                new FailableNotificationStore(new NotificationStore(provider.GetRequiredService<IDbContextFactory<PortalDbContext>>()), Fault));
        });
    }

    /// <summary>Requests a notification the way a producer does, in its own scope.</summary>
    public async Task<NotificationRequestResult> RequestAsync(NotificationEventRequest request)
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationRequestService>().RequestAsync(request, CancellationToken.None);
    }

    public async Task<List<NotificationRecord>> RecordsAsync()
    {
        var records = new List<NotificationRecord>();
        await WithDbAsync(async db => records.AddRange(await db.NotificationRecords.AsNoTracking().OrderBy(r => r.RequestedAtUtc).ToListAsync()));
        return records;
    }

    public async Task<NotificationDispatchState> StateAsync(Guid notificationId)
    {
        NotificationDispatchState? state = null;
        await WithDbAsync(async db => state = await db.NotificationDispatchStates.AsNoTracking().SingleAsync(s => s.NotificationId == notificationId));
        return state!;
    }

    public async Task<List<NotificationAttempt>> AttemptsAsync(Guid notificationId)
    {
        var attempts = new List<NotificationAttempt>();
        await WithDbAsync(async db => attempts.AddRange(
            await db.NotificationAttempts.AsNoTracking().Where(a => a.NotificationId == notificationId).OrderBy(a => a.AttemptNumber).ToListAsync()));
        return attempts;
    }

    /// <summary>One reminder-scheduler pass, as the worker runs it.</summary>
    public async Task<ReminderPassResult> RunSchedulerAsync()
    {
        await using var scope = Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<FeedbackReminderScheduler>().ProcessAsync(CancellationToken.None);
    }

    public async Task<List<FeedbackReminderSchedule>> SchedulesAsync()
    {
        var schedules = new List<FeedbackReminderSchedule>();
        await WithDbAsync(async db => schedules.AddRange(await db.FeedbackReminderSchedules.AsNoTracking().OrderBy(s => s.OccurrenceId).ThenBy(s => s.RecipientUserId).ToListAsync()));
        return schedules;
    }

    /// <summary>Creates an account and returns its stable identifier.</summary>
    public Task<string> CreateRecipientAsync(string role = PortalRoles.Student, string prefix = "student") =>
        CreateUserAsync(UniqueEmail(prefix), role);

    public async Task<string> EmailOfAsync(string userId)
    {
        var email = string.Empty;
        await WithDbAsync(async db => email = (await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId)).Email!);
        return email;
    }

    /// <summary>Moves time forward so retries and leases come due without waiting.</summary>
    public void Advance(TimeSpan by) => Clock.Advance(by);

    public static EmailDeliveryException Retryable(string code = "Http503") => new(EmailFailureKind.Retryable, code);

    public static EmailDeliveryException Permanent(string code = "Http400") => new(EmailFailureKind.Permanent, code);

    public static EmailDeliveryException Uncertain(string code = "Timeout") => new(EmailFailureKind.Uncertain, code);

    /// <summary>A provider outage: every send fails with a retryable error until <see cref="Recover"/> is called.</summary>
    public void Outage() => Emails.Behavior = _ => Retryable();

    public void Recover() => Emails.Behavior = null;

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    public static void AssertNoSecret(IEnumerable<string> texts, params string[] secrets)
    {
        foreach (var text in texts)
        {
            foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
            {
                Assert.DoesNotContain(secret, text, StringComparison.Ordinal);
            }
        }
    }

    public static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;
}
