using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountCredentialSetupTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "NotificationTemplates",
                columns: new[] { "Id", "Channel", "CreatedAtUtc", "CreatedByUserId", "CurrentRevisionId", "CurrentRevisionNumber", "Enabled", "EventType", "LastModifiedAtUtc", "LastModifiedByUserId" },
                values: new object[] { new Guid("7a110000-0000-4000-8000-000000000008"), "Email", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new Guid("7a120000-0000-4000-8000-000000000008"), 1, true, "AccountCredentialSetup", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.InsertData(
                table: "NotificationTemplateRevisions",
                columns: new[] { "Id", "Body", "CreatedAtUtc", "CreatedByUserId", "LastModifiedAtUtc", "LastModifiedByUserId", "Placeholders", "RevisionNumber", "Subject", "TemplateId" },
                values: new object[] { new Guid("7a120000-0000-4000-8000-000000000008"), "An account has been created for you on the IIT Academic Portal.\n\nUse this link to choose your password and activate the account. It works once and expires in {{expiresIn}}:\n\n{{actionLink}}\n\nIf you were not expecting this, you can ignore this email. The account stays inactive until a password is set.", new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, new DateTime(2026, 10, 16, 0, 0, 0, 0, DateTimeKind.Utc), null, "actionLink,expiresIn", 1, "Set up your IIT Academic Portal account", new Guid("7a110000-0000-4000-8000-000000000008") });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "NotificationTemplateRevisions",
                keyColumn: "Id",
                keyValue: new Guid("7a120000-0000-4000-8000-000000000008"));

            migrationBuilder.DeleteData(
                table: "NotificationTemplates",
                keyColumn: "Id",
                keyValue: new Guid("7a110000-0000-4000-8000-000000000008"));
        }
    }
}
