using System.Globalization;
using IitAcademicPortal.Domain.Notifications;

namespace IitAcademicPortal.Application.Notifications;

/// <summary>
/// The internal contract the owners of results publication and feedback windows call after their action has committed. These
/// workflows do not exist yet (no course, publication, or feedback feature is in this service), so the contract is tested with fake
/// owners only. Their end-to-end integration is not complete until each owner calls it and passes its own acceptance tests.
/// </summary>
/// <remarks>
/// Each method needs a stable occurrence identifier the owner can rebuild, and the eligible recipients by stable user ID. None of
/// them accepts marks, feedback text, ratings, submission identifiers, or submission state; the closing reminder is not requested by
/// an owner at all but by <see cref="FeedbackReminderScheduler"/> from the owner's authoritative window state.
/// </remarks>
public interface IAcademicNotificationProducers
{
    /// <summary>
    /// A publication has committed: tell the students entitled to those published results. Finalization alone must not call this.
    /// The message points to authorized portal access and contains no marks.
    /// </summary>
    Task<NotificationRequestResult> ResultsPublishedAsync(
        string publicationOccurrenceId, string courseLabel, string termLabel, IReadOnlyList<string> authorizedStudentUserIds, CancellationToken cancellationToken);

    /// <summary>A feedback window has committed as open: tell its eligible students. It does not depend on submission state.</summary>
    Task<NotificationRequestResult> FeedbackWindowOpenedAsync(
        string windowOccurrenceId, string courseLabel, string windowLabel, DateTime closeAtUtc, IReadOnlyList<string> eligibleStudentUserIds, CancellationToken cancellationToken);
}

public sealed class AcademicNotificationProducers(INotificationRequestService notifications) : IAcademicNotificationProducers
{
    public Task<NotificationRequestResult> ResultsPublishedAsync(
        string publicationOccurrenceId, string courseLabel, string termLabel, IReadOnlyList<string> authorizedStudentUserIds, CancellationToken cancellationToken) =>
        notifications.RequestAsync(
            new NotificationEventRequest(
                NotificationEventType.ResultsPublished,
                publicationOccurrenceId,
                authorizedStudentUserIds,
                new Dictionary<string, string> { ["courseName"] = courseLabel, ["termName"] = termLabel }),
            cancellationToken);

    public Task<NotificationRequestResult> FeedbackWindowOpenedAsync(
        string windowOccurrenceId, string courseLabel, string windowLabel, DateTime closeAtUtc, IReadOnlyList<string> eligibleStudentUserIds, CancellationToken cancellationToken) =>
        notifications.RequestAsync(
            new NotificationEventRequest(
                NotificationEventType.FeedbackWindowOpened,
                windowOccurrenceId,
                eligibleStudentUserIds,
                new Dictionary<string, string>
                {
                    ["courseName"] = courseLabel,
                    ["windowName"] = windowLabel,
                    ["closesAtUtc"] = closeAtUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
                }),
            cancellationToken);
}
