using IitAcademicPortal.Application.Notifications;
using IitAcademicPortal.Domain.Notifications;
using Microsoft.EntityFrameworkCore;

namespace IitAcademicPortal.Infrastructure.Persistence;

public partial class PortalDbContext
{
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();

    public DbSet<NotificationTemplateRevision> NotificationTemplateRevisions => Set<NotificationTemplateRevision>();

    public DbSet<NotificationRecord> NotificationRecords => Set<NotificationRecord>();

    public DbSet<NotificationDispatchState> NotificationDispatchStates => Set<NotificationDispatchState>();

    public DbSet<NotificationAttempt> NotificationAttempts => Set<NotificationAttempt>();

    public DbSet<PasswordRecoveryRequest> PasswordRecoveryRequests => Set<PasswordRecoveryRequest>();

    public DbSet<FeedbackReminderPolicy> FeedbackReminderPolicies => Set<FeedbackReminderPolicy>();

    public DbSet<FeedbackReminderSchedule> FeedbackReminderSchedules => Set<FeedbackReminderSchedule>();

    private const string RootsOnly = "\"ParentNotificationId\" IS NULL";
    private const string ResendsOnly = "\"ParentNotificationId\" IS NOT NULL";

    /// <summary>
    /// Notifications. History (records, revisions, attempts) is append-only and restrictive: no cascade deletes and no account
    /// foreign key, so it survives removal of an account, and the runtime role cannot update or delete those tables (see
    /// runtime-grants.sql). Only the dispatch state, the transient recovery input, and the reminder schedule are mutable.
    /// </summary>
    private static void ConfigureNotifications(ModelBuilder builder)
    {
        ConfigureNotificationTables(builder);
        SeedNotificationTemplates(builder);
    }

    // One email template per supported event, each with an immutable first revision, with fixed identifiers so every database is
    // seeded identically. Security templates start enabled and ordinary ones carry no inferred preference.
    private static void SeedNotificationTemplates(ModelBuilder builder)
    {
        var seededAt = new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc);
        builder.Entity<NotificationTemplate>().HasData(NotificationTemplateCatalog.All.Select(d => new
        {
            Id = NotificationTemplateCatalog.TemplateId(d.EventType),
            d.EventType,
            Channel = NotificationChannel.Email,
            Enabled = true,
            CurrentRevisionId = (Guid?)NotificationTemplateCatalog.InitialRevisionId(d.EventType),
            CurrentRevisionNumber = 1,
            CreatedAtUtc = seededAt,
            CreatedByUserId = (string?)null,
            LastModifiedAtUtc = seededAt,
            LastModifiedByUserId = (string?)null,
        }));
        builder.Entity<NotificationTemplateRevision>().HasData(NotificationTemplateCatalog.All.Select(d => new
        {
            Id = NotificationTemplateCatalog.InitialRevisionId(d.EventType),
            TemplateId = NotificationTemplateCatalog.TemplateId(d.EventType),
            RevisionNumber = 1,
            Subject = d.DefaultSubject,
            Body = d.DefaultBody,
            Placeholders = string.Join(',', d.Placeholders),
            CreatedAtUtc = seededAt,
            CreatedByUserId = (string?)null,
            LastModifiedAtUtc = seededAt,
            LastModifiedByUserId = (string?)null,
        }));
    }

    private static void ConfigureNotificationTables(ModelBuilder builder)
    {
        builder.Entity<NotificationTemplate>(template =>
        {
            template.ToTable("NotificationTemplates");
            template.HasKey(t => t.Id);
            template.Property(t => t.Id).ValueGeneratedNever();
            template.Property(t => t.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
            template.Property(t => t.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
            template.HasIndex(t => new { t.EventType, t.Channel }).IsUnique();
        });

        builder.Entity<NotificationTemplateRevision>(revision =>
        {
            revision.ToTable("NotificationTemplateRevisions");
            revision.HasKey(r => r.Id);
            revision.Property(r => r.Id).ValueGeneratedNever();
            revision.Property(r => r.Subject).HasMaxLength(NotificationTemplateRevision.MaxSubjectLength).IsRequired();
            revision.Property(r => r.Body).HasMaxLength(NotificationTemplateRevision.MaxBodyLength).IsRequired();
            revision.Property(r => r.Placeholders).HasMaxLength(1000).IsRequired();
            revision.HasIndex(r => new { r.TemplateId, r.RevisionNumber }).IsUnique();
            revision.HasOne<NotificationTemplate>().WithMany().HasForeignKey(r => r.TemplateId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NotificationRecord>(record =>
        {
            record.ToTable("NotificationRecords");
            record.HasKey(r => r.Id);
            record.Property(r => r.Id).ValueGeneratedNever();
            record.Property(r => r.EventType).HasConversion<string>().HasMaxLength(32).IsRequired();
            record.Property(r => r.OccurrenceId).HasMaxLength(NotificationRecord.MaxOccurrenceIdLength).IsRequired();
            record.Property(r => r.RecipientUserId).HasMaxLength(Domain.AuditedEntity.MaxUserIdLength).IsRequired();
            record.Property(r => r.Destination).HasMaxLength(NotificationRecord.MaxDestinationLength);
            record.Property(r => r.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
            record.Property(r => r.RequestedAtUtc).HasConversion(UtcTimes).IsRequired();
            record.Property(r => r.Subject).HasMaxLength(NotificationTemplateRevision.MaxSubjectLength);
            record.Property(r => r.Body).HasMaxLength(NotificationTemplateRevision.MaxBodyLength);
            record.Property(r => r.RequestIdempotencyKey).HasMaxLength(NotificationRecord.MaxIdempotencyKeyLength);

            // An original execution is unique per source occurrence, recipient, and channel; a resend has its own key.
            record.HasIndex(r => new { r.EventType, r.OccurrenceId, r.RecipientUserId, r.Channel })
                .IsUnique().HasFilter(RootsOnly).HasDatabaseName("IX_NotificationRecords_Occurrence");
            record.HasIndex(r => new { r.ParentNotificationId, r.RequestIdempotencyKey })
                .IsUnique().HasFilter(ResendsOnly).HasDatabaseName("IX_NotificationRecords_Resend");

            // Newest-first keyset paging, plus the supported filters.
            record.HasIndex(r => new { r.RequestedAtUtc, r.Id }).IsDescending(true, true).HasDatabaseName("IX_NotificationRecords_RequestedAtUtc_Id");
            record.HasIndex(r => r.EventType);
            record.HasIndex(r => r.RecipientUserId);
            record.HasIndex(r => r.Destination);
            record.HasIndex(r => r.ParentNotificationId);

            record.HasOne<NotificationTemplateRevision>().WithMany().HasForeignKey(r => r.TemplateRevisionId).OnDelete(DeleteBehavior.Restrict);
            record.HasOne<NotificationRecord>().WithMany().HasForeignKey(r => r.ParentNotificationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NotificationDispatchState>(state =>
        {
            state.ToTable("NotificationDispatchStates");
            state.HasKey(s => s.NotificationId);
            state.Property(s => s.NotificationId).ValueGeneratedNever();
            state.Property(s => s.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
            state.Property(s => s.NextAttemptAtUtc).HasConversion(UtcTimes);
            state.Property(s => s.LeaseExpiresAtUtc).HasConversion(UtcTimes);
            state.Property(s => s.SendStartedAtUtc).HasConversion(UtcTimes);
            state.Property(s => s.SentAtUtc).HasConversion(UtcTimes);
            state.Property(s => s.UpdatedAtUtc).HasConversion(UtcTimes).IsRequired();
            state.Property(s => s.FailureCode).HasMaxLength(NotificationDispatchState.MaxFailureCodeLength);
            state.HasIndex(s => new { s.Status, s.NextAttemptAtUtc, s.LeaseExpiresAtUtc });
            state.HasOne<NotificationRecord>().WithOne().HasForeignKey<NotificationDispatchState>(s => s.NotificationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NotificationAttempt>(attempt =>
        {
            attempt.ToTable("NotificationAttempts");
            attempt.HasKey(a => a.Id);
            attempt.Property(a => a.Id).ValueGeneratedNever();
            attempt.Property(a => a.StartedAtUtc).HasConversion(UtcTimes).IsRequired();
            attempt.Property(a => a.CompletedAtUtc).HasConversion(UtcTimes).IsRequired();
            attempt.Property(a => a.ProviderAcceptedAtUtc).HasConversion(UtcTimes);
            attempt.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(24).IsRequired();
            attempt.Property(a => a.FailureCode).HasMaxLength(NotificationDispatchState.MaxFailureCodeLength);
            attempt.HasIndex(a => new { a.NotificationId, a.AttemptNumber }).IsUnique();
            attempt.HasOne<NotificationRecord>().WithMany().HasForeignKey(a => a.NotificationId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<PasswordRecoveryRequest>(request =>
        {
            request.ToTable("PasswordRecoveryRequests");
            request.HasKey(r => r.Id);
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.ProtectedAddress).HasMaxLength(2000).IsRequired();
            request.Property(r => r.RequestedAtUtc).HasConversion(UtcTimes).IsRequired();
            request.Property(r => r.ExpiresAtUtc).HasConversion(UtcTimes).IsRequired();
            request.Property(r => r.LeaseExpiresAtUtc).HasConversion(UtcTimes);
            request.HasIndex(r => r.ExpiresAtUtc);
            request.HasIndex(r => r.LeaseExpiresAtUtc);
        });

        builder.Entity<FeedbackReminderPolicy>(policy =>
        {
            policy.ToTable("FeedbackReminderPolicies");
            policy.HasKey(p => p.Id);
            policy.Property(p => p.Id).ValueGeneratedNever();
        });

        builder.Entity<FeedbackReminderSchedule>(schedule =>
        {
            schedule.ToTable("FeedbackReminderSchedules");
            schedule.HasKey(s => s.Id);
            schedule.Property(s => s.Id).ValueGeneratedNever();
            schedule.Property(s => s.OccurrenceId).HasMaxLength(NotificationRecord.MaxOccurrenceIdLength).IsRequired();
            schedule.Property(s => s.RecipientUserId).HasMaxLength(Domain.AuditedEntity.MaxUserIdLength).IsRequired();
            schedule.Property(s => s.CloseAtUtc).HasConversion(UtcTimes).IsRequired();
            schedule.Property(s => s.DueAtUtc).HasConversion(UtcTimes).IsRequired();
            schedule.Property(s => s.UpdatedAtUtc).HasConversion(UtcTimes).IsRequired();
            schedule.Property(s => s.State).HasConversion<string>().HasMaxLength(16).IsRequired();
            schedule.HasIndex(s => new { s.OccurrenceId, s.RecipientUserId }).IsUnique();
            schedule.HasIndex(s => new { s.State, s.DueAtUtc });
        });
    }
}
