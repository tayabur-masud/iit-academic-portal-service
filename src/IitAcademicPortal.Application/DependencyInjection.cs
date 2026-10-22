using IitAcademicPortal.Application.Abstractions;
using IitAcademicPortal.Application.Auditing;
using IitAcademicPortal.Application.Authentication;
using IitAcademicPortal.Application.Configuration;
using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Application.PasswordRecovery;
using IitAcademicPortal.Application.Staff;
using IitAcademicPortal.Application.Students;
using IitAcademicPortal.Application.Teachers;
using IitAcademicPortal.Application.UserManagement;
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

        services.AddOptions<AccountCredentialSetupOptions>().Bind(configuration.GetSection(AccountCredentialSetupOptions.SectionName));
        services.AddOptions<NotificationOptions>().Bind(configuration.GetSection(NotificationOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditMetrics>();
        services.AddScoped<IAuditEventRecorder, AuditEventRecorder>();
        services.AddScoped<AuditReviewService>();
        services.AddSingleton<ConfigurationResources>();
        services.AddScoped<CatalogService>();
        services.AddScoped<GradeScaleService>();
        services.AddScoped<GeographyImportService>();
        services.AddScoped<TeacherProvisioningService>();
        services.AddScoped<TeacherProfileService>();
        services.AddScoped<TeacherRoleService>();
        services.AddScoped<TeacherDirectoryService>();
        services.AddScoped<StaffProfileService>();
        services.AddScoped<StaffLifecycleService>();
        services.AddScoped<TeacherLifecycleService>();
        services.AddScoped<ITeacherCoordinatorRoleSynchronizer, TeacherCoordinatorRoleSynchronizer>();
        services.AddScoped<UserDirectoryService>();
        services.AddScoped<UserProvisioningService>();
        services.AddScoped<UserAccessService>();
        services.AddScoped<AccountCredentialSetupService>();
        services.AddScoped<StudentAdmissionService>();
        services.AddScoped<GraduationEligibilityService>();

        // Published credits and CGPA belong to the academic-results owner. Until it registers its adapter, eligibility reports them unavailable.
        services.TryAddScoped<IPublishedAdmissionResultsProvider, UnavailablePublishedAdmissionResultsProvider>();
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

        // The owning workflow's contract per security event. Password reset and the common account setup (every new account, whatever
        // its owner) are real, each with its own token purpose. The older per-role invitation events and email confirmation have no
        // producer and no secure-link owner, so a notification for them fails visibly rather than invent a proof flow.
        services.AddScoped<ISecurityContentProvider, PasswordResetContentProvider>();
        services.AddScoped<ISecurityContentProvider, AccountCredentialSetupSecurityContentProvider>();
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
