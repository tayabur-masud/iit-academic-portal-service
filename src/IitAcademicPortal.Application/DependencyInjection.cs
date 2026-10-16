using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace IitAcademicPortal.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PasswordRecoveryOptions>()
            .Bind(configuration.GetSection(PasswordRecoveryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditMetrics>();
        services.AddScoped<IAuditEventRecorder, AuditEventRecorder>();
        services.AddScoped<AuditReviewService>();
        services.AddSingleton<ConfigurationResources>();
        services.AddScoped<CatalogService>();
        services.AddScoped<GeographyImportService>();
        services.AddScoped<TeacherProvisioningService>();
        services.AddScoped<TeacherProfileService>();
        services.AddScoped<TeacherRoleService>();
        services.AddScoped<TeacherDirectoryService>();
        services.AddScoped<TeacherLifecycleService>();
        services.AddScoped<ITeacherCoordinatorRoleSynchronizer, TeacherCoordinatorRoleSynchronizer>();
        services.AddSingleton<NotificationTemplateRenderer>();
        services.AddSingleton<NotificationMetrics>();
        services.AddSingleton<INotificationFailureSignal, LoggingNotificationFailureSignal>();
        services.AddScoped<INotificationRequestService, NotificationRequestService>();
        services.AddScoped<NotificationTemplateService>();
        services.AddScoped<NotificationHistoryService>();
        services.AddScoped<NotificationResendService>();
        services.AddScoped<FeedbackReminderPolicyService>();
        services.AddScoped<FeedbackReminderScheduler>();
        services.AddScoped<IAcademicNotificationProducers, AcademicNotificationProducers>();

        // The owning workflow's contract per security event. Password reset is real; the others have no secure-link owner yet, so
        // their notifications fail visibly (and can be resent later) rather than invent a proof flow.
        services.AddScoped<ISecurityContentProvider, PasswordResetContentProvider>();
        services.AddScoped<ISecurityContentProvider>(_ => new UnavailableSecurityContentProvider(NotificationEventType.TeacherInvitation, "SetupLinkProviderMissing"));
        services.AddScoped<ISecurityContentProvider>(_ => new UnavailableSecurityContentProvider(NotificationEventType.StudentInvitation, "SetupLinkProviderMissing"));
        services.AddScoped<ISecurityContentProvider>(_ => new UnavailableSecurityContentProvider(NotificationEventType.EmailConfirmation, "ConfirmationProviderMissing"));

        // Until the feedback-window owner supplies a source there are no windows, so no reminders are scheduled.
        services.TryAddScoped<IFeedbackWindowSource, NoFeedbackWindowSource>();
        services.AddScoped<PortalAuthenticationService>();
        services.AddScoped<PasswordRecoveryService>();
        return services;
    }
}
