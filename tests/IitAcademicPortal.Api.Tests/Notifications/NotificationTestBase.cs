using System.Net;
using System.Text.Json;
using IitAcademicPortal.Api.Tests.Infrastructure;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Identity;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Api.Tests.Notifications;

/// <summary>A notification test host, an active-Admin client, and small helpers for notification requests and the Admin routes.</summary>
public abstract class NotificationTestBase : IDisposable
{
    protected const string Templates = "/api/admin/notification-templates";
    protected const string History = "/api/admin/notifications";
    protected const string ReminderSetting = "/api/admin/notification-settings/feedback-reminder";

    protected NotificationTestHost Host { get; } = new();

    /// <summary>The identifier of the account behind the most recent <see cref="AdminAsync"/> client.</summary>
    protected string AdminUserId { get; private set; } = string.Empty;

    public void Dispose() => Host.Dispose();

    protected async Task<PortalClient> AdminAsync()
    {
        var email = PortalApiFactory.UniqueEmail("admin");
        AdminUserId = await Host.CreateUserAsync(email, PortalRoles.Admin);
        var client = Host.CreatePortalClient();
        await client.SignInSuccessfullyAsync(email);
        return client;
    }

    protected static NotificationEventRequest Results(string occurrence, params string[] recipients) =>
        new(
            NotificationEventType.ResultsPublished,
            occurrence,
            recipients,
            new Dictionary<string, string> { ["courseName"] = "Algorithms", ["termName"] = "Fall 2026" });

    protected static async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(
        PortalClient client, HttpMethod method, string url, object? body = null, IDictionary<string, string>? headers = null)
    {
        using var response = await client.SendAsync(method, url, body, headers: headers);
        var text = await response.Content.ReadAsStringAsync();
        return (response.StatusCode, text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone());
    }

    protected static bool HasError(JsonElement problem, string field) =>
        problem.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out _);

    protected static string FirstError(JsonElement problem, string field) => problem.GetProperty("errors").GetProperty(field)[0].GetString()!;

    /// <summary>Accepts a notification and returns its identifier, failing the test if nothing was accepted.</summary>
    protected async Task<Guid> AcceptOneAsync(NotificationEventRequest request)
    {
        var before = (await Host.RecordsAsync()).Select(r => r.Id).ToHashSet();
        var result = await Host.RequestAsync(request);
        Assert.Equal(NotificationRequestStatus.Accepted, result.Status);
        return (await Host.RecordsAsync()).Single(r => !before.Contains(r.Id)).Id;
    }

    /// <summary>Changes dispatch state directly, as a crashed worker or an operator's recovery would leave it.</summary>
    protected async Task UpdateStateAsync(Guid id, Action<NotificationDispatchState> change)
    {
        await Host.WithDbAsync(async db =>
        {
            var state = await db.NotificationDispatchStates.SingleAsync(s => s.NotificationId == id);
            change(state);
            await db.SaveChangesAsync();
        });
    }
}
