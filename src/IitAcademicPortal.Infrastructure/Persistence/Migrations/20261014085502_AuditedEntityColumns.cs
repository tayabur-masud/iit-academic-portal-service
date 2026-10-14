using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AuditedEntityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Upazilas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Upazilas",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Upazilas",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Upazilas",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Semesters",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Semesters",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Semesters",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Semesters",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Halls",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Halls",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Halls",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Halls",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "ExaminationTypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "ExaminationTypes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "ExaminationTypes",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "ExaminationTypes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "EducationGroups",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "EducationGroups",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "EducationGroups",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "EducationGroups",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "EducationBoards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "EducationBoards",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "EducationBoards",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "EducationBoards",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "EducationalInstitutions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "EducationalInstitutions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "EducationalInstitutions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "EducationalInstitutions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Divisions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Divisions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Divisions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Divisions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Districts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Districts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Districts",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Districts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Designations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Designations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Designations",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Designations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Degrees",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Degrees",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Degrees",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Degrees",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Batches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Batches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Batches",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "Batches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AcademicSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "AcademicSessions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "AcademicSessions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.AddColumn<string>(
                name: "LastModifiedByUserId",
                table: "AcademicSessions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "LastModifiedAtUtc",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "LastModifiedByUserId",
                table: "AcademicSessions");
        }
    }
}
