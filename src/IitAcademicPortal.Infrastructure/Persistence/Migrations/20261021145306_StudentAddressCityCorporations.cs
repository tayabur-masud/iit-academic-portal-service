using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentAddressCityCorporations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MailingCityCorporationId",
                table: "StudentProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PermanentCityCorporationId",
                table: "StudentProfiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_MailingCityCorporationId",
                table: "StudentProfiles",
                column: "MailingCityCorporationId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_PermanentCityCorporationId",
                table: "StudentProfiles",
                column: "PermanentCityCorporationId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StudentProfiles_MailingLocality_AtMostOne",
                table: "StudentProfiles",
                sql: "\"MailingUpazilaId\" IS NULL OR \"MailingCityCorporationId\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_StudentProfiles_PermanentLocality_AtMostOne",
                table: "StudentProfiles",
                sql: "\"PermanentUpazilaId\" IS NULL OR \"PermanentCityCorporationId\" IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentProfiles_CityCorporations_MailingCityCorporationId",
                table: "StudentProfiles",
                column: "MailingCityCorporationId",
                principalTable: "CityCorporations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentProfiles_CityCorporations_PermanentCityCorporationId",
                table: "StudentProfiles",
                column: "PermanentCityCorporationId",
                principalTable: "CityCorporations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentProfiles_CityCorporations_MailingCityCorporationId",
                table: "StudentProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentProfiles_CityCorporations_PermanentCityCorporationId",
                table: "StudentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_StudentProfiles_MailingCityCorporationId",
                table: "StudentProfiles");

            migrationBuilder.DropIndex(
                name: "IX_StudentProfiles_PermanentCityCorporationId",
                table: "StudentProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StudentProfiles_MailingLocality_AtMostOne",
                table: "StudentProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StudentProfiles_PermanentLocality_AtMostOne",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "MailingCityCorporationId",
                table: "StudentProfiles");

            migrationBuilder.DropColumn(
                name: "PermanentCityCorporationId",
                table: "StudentProfiles");
        }
    }
}
