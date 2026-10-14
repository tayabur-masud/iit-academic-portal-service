using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SoftDeleteConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Upazilas_DistrictId_NormalizedName",
                table: "Upazilas");

            migrationBuilder.DropIndex(
                name: "IX_Semesters_NormalizedName",
                table: "Semesters");

            migrationBuilder.DropIndex(
                name: "IX_Halls_NormalizedCode",
                table: "Halls");

            migrationBuilder.DropIndex(
                name: "IX_Halls_NormalizedName",
                table: "Halls");

            migrationBuilder.DropIndex(
                name: "IX_ExaminationTypes_NormalizedName",
                table: "ExaminationTypes");

            migrationBuilder.DropIndex(
                name: "IX_EducationGroups_NormalizedName",
                table: "EducationGroups");

            migrationBuilder.DropIndex(
                name: "IX_EducationBoards_NormalizedCode",
                table: "EducationBoards");

            migrationBuilder.DropIndex(
                name: "IX_EducationBoards_NormalizedName",
                table: "EducationBoards");

            migrationBuilder.DropIndex(
                name: "IX_EducationalInstitutions_NormalizedCode",
                table: "EducationalInstitutions");

            migrationBuilder.DropIndex(
                name: "IX_EducationalInstitutions_NormalizedName",
                table: "EducationalInstitutions");

            migrationBuilder.DropIndex(
                name: "IX_Divisions_NormalizedName",
                table: "Divisions");

            migrationBuilder.DropIndex(
                name: "IX_Districts_DivisionId_NormalizedName",
                table: "Districts");

            migrationBuilder.DropIndex(
                name: "IX_Designations_NormalizedCode",
                table: "Designations");

            migrationBuilder.DropIndex(
                name: "IX_Designations_NormalizedName",
                table: "Designations");

            migrationBuilder.DropIndex(
                name: "IX_Degrees_NormalizedCode",
                table: "Degrees");

            migrationBuilder.DropIndex(
                name: "IX_Degrees_NormalizedName",
                table: "Degrees");

            migrationBuilder.DropIndex(
                name: "IX_Batches_DegreeId_NormalizedName",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_AcademicSessions_NormalizedName",
                table: "AcademicSessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Upazilas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Upazilas",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Upazilas",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Semesters",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Semesters",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Semesters",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Halls",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Halls",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Halls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "ExaminationTypes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "ExaminationTypes",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "ExaminationTypes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "EducationGroups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "EducationGroups",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EducationGroups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "EducationBoards",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "EducationBoards",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EducationBoards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "EducationalInstitutions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "EducationalInstitutions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "EducationalInstitutions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Divisions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Divisions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Divisions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Districts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Districts",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Districts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Designations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Designations",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Designations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Degrees",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Degrees",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Degrees",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Batches",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "Batches",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Batches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "AcademicSessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedByUserId",
                table: "AcademicSessions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "AcademicSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Upazilas_DistrictId_NormalizedName",
                table: "Upazilas",
                columns: new[] { "DistrictId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_NormalizedName",
                table: "Semesters",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Halls_NormalizedCode",
                table: "Halls",
                column: "NormalizedCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Halls_NormalizedName",
                table: "Halls",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypes_NormalizedName",
                table: "ExaminationTypes",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EducationGroups_NormalizedName",
                table: "EducationGroups",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EducationBoards_NormalizedCode",
                table: "EducationBoards",
                column: "NormalizedCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EducationBoards_NormalizedName",
                table: "EducationBoards",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EducationalInstitutions_NormalizedCode",
                table: "EducationalInstitutions",
                column: "NormalizedCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_EducationalInstitutions_NormalizedName",
                table: "EducationalInstitutions",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_NormalizedName",
                table: "Divisions",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Districts_DivisionId_NormalizedName",
                table: "Districts",
                columns: new[] { "DivisionId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Designations_NormalizedCode",
                table: "Designations",
                column: "NormalizedCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Designations_NormalizedName",
                table: "Designations",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Degrees_NormalizedCode",
                table: "Degrees",
                column: "NormalizedCode",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Degrees_NormalizedName",
                table: "Degrees",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_DegreeId_NormalizedName",
                table: "Batches",
                columns: new[] { "DegreeId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_AcademicSessions_NormalizedName",
                table: "AcademicSessions",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Upazilas_DistrictId_NormalizedName",
                table: "Upazilas");

            migrationBuilder.DropIndex(
                name: "IX_Semesters_NormalizedName",
                table: "Semesters");

            migrationBuilder.DropIndex(
                name: "IX_Halls_NormalizedCode",
                table: "Halls");

            migrationBuilder.DropIndex(
                name: "IX_Halls_NormalizedName",
                table: "Halls");

            migrationBuilder.DropIndex(
                name: "IX_ExaminationTypes_NormalizedName",
                table: "ExaminationTypes");

            migrationBuilder.DropIndex(
                name: "IX_EducationGroups_NormalizedName",
                table: "EducationGroups");

            migrationBuilder.DropIndex(
                name: "IX_EducationBoards_NormalizedCode",
                table: "EducationBoards");

            migrationBuilder.DropIndex(
                name: "IX_EducationBoards_NormalizedName",
                table: "EducationBoards");

            migrationBuilder.DropIndex(
                name: "IX_EducationalInstitutions_NormalizedCode",
                table: "EducationalInstitutions");

            migrationBuilder.DropIndex(
                name: "IX_EducationalInstitutions_NormalizedName",
                table: "EducationalInstitutions");

            migrationBuilder.DropIndex(
                name: "IX_Divisions_NormalizedName",
                table: "Divisions");

            migrationBuilder.DropIndex(
                name: "IX_Districts_DivisionId_NormalizedName",
                table: "Districts");

            migrationBuilder.DropIndex(
                name: "IX_Designations_NormalizedCode",
                table: "Designations");

            migrationBuilder.DropIndex(
                name: "IX_Designations_NormalizedName",
                table: "Designations");

            migrationBuilder.DropIndex(
                name: "IX_Degrees_NormalizedCode",
                table: "Degrees");

            migrationBuilder.DropIndex(
                name: "IX_Degrees_NormalizedName",
                table: "Degrees");

            migrationBuilder.DropIndex(
                name: "IX_Batches_DegreeId_NormalizedName",
                table: "Batches");

            migrationBuilder.DropIndex(
                name: "IX_AcademicSessions_NormalizedName",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Upazilas");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Halls");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "ExaminationTypes");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EducationGroups");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EducationBoards");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "EducationalInstitutions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Divisions");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Districts");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Designations");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "AcademicSessions");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "AcademicSessions");

            migrationBuilder.CreateIndex(
                name: "IX_Upazilas_DistrictId_NormalizedName",
                table: "Upazilas",
                columns: new[] { "DistrictId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_NormalizedName",
                table: "Semesters",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Halls_NormalizedCode",
                table: "Halls",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Halls_NormalizedName",
                table: "Halls",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypes_NormalizedName",
                table: "ExaminationTypes",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationGroups_NormalizedName",
                table: "EducationGroups",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationBoards_NormalizedCode",
                table: "EducationBoards",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationBoards_NormalizedName",
                table: "EducationBoards",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationalInstitutions_NormalizedCode",
                table: "EducationalInstitutions",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EducationalInstitutions_NormalizedName",
                table: "EducationalInstitutions",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_NormalizedName",
                table: "Divisions",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Districts_DivisionId_NormalizedName",
                table: "Districts",
                columns: new[] { "DivisionId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_NormalizedCode",
                table: "Designations",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Designations_NormalizedName",
                table: "Designations",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Degrees_NormalizedCode",
                table: "Degrees",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Degrees_NormalizedName",
                table: "Degrees",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_DegreeId_NormalizedName",
                table: "Batches",
                columns: new[] { "DegreeId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AcademicSessions_NormalizedName",
                table: "AcademicSessions",
                column: "NormalizedName",
                unique: true);
        }
    }
}
