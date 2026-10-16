using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedbackReminderPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadTimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackReminderPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackReminderSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurrenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    CloseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PolicyRevision = table.Column<int>(type: "integer", nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackReminderSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordRecoveryRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProtectedAddress = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordRecoveryRequests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplateRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    Placeholders = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplateRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationTemplateRevisions_NotificationTemplates_Templat~",
                        column: x => x.TemplateId,
                        principalTable: "NotificationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OccurrenceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RecipientUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Destination = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Channel = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TemplateRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    ContentWithheld = table.Column<bool>(type: "boolean", nullable: false),
                    ParentNotificationId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestIdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationRecords_NotificationRecords_ParentNotificationId",
                        column: x => x.ParentNotificationId,
                        principalTable: "NotificationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationRecords_NotificationTemplateRevisions_TemplateR~",
                        column: x => x.TemplateRevisionId,
                        principalTable: "NotificationTemplateRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderAcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationAttempts_NotificationRecords_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "NotificationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationDispatchStates",
                columns: table => new
                {
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LeaseExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SendStartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationDispatchStates", x => x.NotificationId);
                    table.ForeignKey(
                        name: "FK_NotificationDispatchStates_NotificationRecords_Notification~",
                        column: x => x.NotificationId,
                        principalTable: "NotificationRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "Channel", "CreatedAtUtc", "CreatedByUserId", "CurrentRevisionId", "CurrentRevisionNumber", "Enabled", "EventType", "LastModifiedAtUtc", "LastModifiedByUserId" },
                values: new object[,]
                {
                    { new Guid("7a110000-0000-4000-8000-000000000001"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000001"), 1, true, "TeacherInvitation", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000002"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000002"), 1, true, "StudentInvitation", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000003"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000003"), 1, true, "EmailConfirmation", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000004"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000004"), 1, true, "PasswordReset", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000005"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000005"), 1, true, "ResultsPublished", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000006"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000006"), 1, true, "FeedbackWindowOpened", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { new Guid("7a110000-0000-4000-8000-000000000007"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000007"), 1, true, "FeedbackClosingReminder", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });

            migrationBuilder.InsertData(
                table: "NotificationTemplateRevisions",
                columns: new[] { "Id", "Body", "CreatedAtUtc", "CreatedByUserId", "LastModifiedAtUtc", "LastModifiedByUserId", "Placeholders", "RevisionNumber", "Subject", "TemplateId" },
                values: new object[,]
                {
                    { new Guid("7a120000-0000-4000-8000-000000000001"), "You have been added as a teacher on the IIT Academic Portal.\n\nUse this link to set up your account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\nIf you were not expecting this invitation, you can ignore this email.", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "actionLink,expiresIn", 1, "You are invited to the IIT Academic Portal", new Guid("7a110000-0000-4000-8000-000000000001") },
                    { new Guid("7a120000-0000-4000-8000-000000000002"), "You have been added as a student on the IIT Academic Portal.\n\nUse this link to set up your account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\nIf you were not expecting this invitation, you can ignore this email.", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "actionLink,expiresIn", 1, "You are invited to the IIT Academic Portal", new Guid("7a110000-0000-4000-8000-000000000002") },
                    { new Guid("7a120000-0000-4000-8000-000000000003"), "Please confirm that this is your email address for the IIT Academic Portal.\n\nUse this link to confirm it. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\nIf you did not ask for this, you can ignore this email.", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "actionLink,expiresIn", 1, "Confirm your email address for the IIT Academic Portal", new Guid("7a110000-0000-4000-8000-000000000003") },
                    { new Guid("7a120000-0000-4000-8000-000000000004"), "A password reset was requested for your IIT Academic Portal account.\n\nUse this link to choose a new password. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\nIf you did not request this, you can ignore this email. Your password will not change.", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "actionLink,expiresIn", 1, "Reset your IIT Academic Portal password", new Guid("7a110000-0000-4000-8000-000000000004") },
                    { new Guid("7a120000-0000-4000-8000-000000000005"), "Results for {{courseName}} ({{termName}}) have been published.\n\nSign in to the IIT Academic Portal to see your results:\n\n{{portalUrl}}", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "courseName,termName,portalUrl", 1, "Results published for {{courseName}}", new Guid("7a110000-0000-4000-8000-000000000005") },
                    { new Guid("7a120000-0000-4000-8000-000000000006"), "The feedback window \"{{windowName}}\" for {{courseName}} is now open and closes at {{closesAtUtc}}.\n\nSign in to the IIT Academic Portal to give your feedback:\n\n{{portalUrl}}", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "courseName,windowName,closesAtUtc,portalUrl", 1, "Feedback is open for {{courseName}}", new Guid("7a110000-0000-4000-8000-000000000006") },
                    { new Guid("7a120000-0000-4000-8000-000000000007"), "The feedback window \"{{windowName}}\" for {{courseName}} closes at {{closesAtUtc}}.\n\nSign in to the IIT Academic Portal to give your feedback:\n\n{{portalUrl}}", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "courseName,windowName,closesAtUtc,portalUrl", 1, "Feedback for {{courseName}} closes soon", new Guid("7a110000-0000-4000-8000-000000000007") }
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackReminderSchedules_OccurrenceId_RecipientUserId",
                table: "FeedbackReminderSchedules",
                columns: new[] { "OccurrenceId", "RecipientUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedbackReminderSchedules_State_DueAtUtc",
                table: "FeedbackReminderSchedules",
                columns: new[] { "State", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationAttempts_NotificationId_AttemptNumber",
                table: "NotificationAttempts",
                columns: new[] { "NotificationId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationDispatchStates_Status_NextAttemptAtUtc_LeaseExp~",
                table: "NotificationDispatchStates",
                columns: new[] { "Status", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_Destination",
                table: "NotificationRecords",
                column: "Destination");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_EventType",
                table: "NotificationRecords",
                column: "EventType");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_Occurrence",
                table: "NotificationRecords",
                columns: new[] { "EventType", "OccurrenceId", "RecipientUserId", "Channel" },
                unique: true,
                filter: "\"ParentNotificationId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_ParentNotificationId",
                table: "NotificationRecords",
                column: "ParentNotificationId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_RecipientUserId",
                table: "NotificationRecords",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_RequestedAtUtc_Id",
                table: "NotificationRecords",
                columns: new[] { "RequestedAtUtc", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_Resend",
                table: "NotificationRecords",
                columns: new[] { "ParentNotificationId", "RequestIdempotencyKey" },
                unique: true,
                filter: "\"ParentNotificationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationRecords_TemplateRevisionId",
                table: "NotificationRecords",
                column: "TemplateRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplateRevisions_TemplateId_RevisionNumber",
                table: "NotificationTemplateRevisions",
                columns: new[] { "TemplateId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_EventType_Channel",
                table: "NotificationTemplates",
                columns: new[] { "EventType", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveryRequests_ExpiresAtUtc",
                table: "PasswordRecoveryRequests",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordRecoveryRequests_LeaseExpiresAtUtc",
                table: "PasswordRecoveryRequests",
                column: "LeaseExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedbackReminderPolicies");

            migrationBuilder.DropTable(
                name: "FeedbackReminderSchedules");

            migrationBuilder.DropTable(
                name: "NotificationAttempts");

            migrationBuilder.DropTable(
                name: "NotificationDispatchStates");

            migrationBuilder.DropTable(
                name: "PasswordRecoveryRequests");

            migrationBuilder.DropTable(
                name: "NotificationRecords");

            migrationBuilder.DropTable(
                name: "NotificationTemplateRevisions");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");
        }
    }
}
