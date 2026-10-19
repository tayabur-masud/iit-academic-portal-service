using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentHistoryCatalogReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BoardId",
                table: "StudentAcademicHistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GroupId",
                table: "StudentAcademicHistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InstitutionId",
                table: "StudentAcademicHistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SchoolOrCollegeId",
                table: "StudentAcademicHistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UniversityId",
                table: "StudentAcademicHistory",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_BoardId",
                table: "StudentAcademicHistory",
                column: "BoardId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_GroupId",
                table: "StudentAcademicHistory",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_InstitutionId",
                table: "StudentAcademicHistory",
                column: "InstitutionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_SchoolOrCollegeId",
                table: "StudentAcademicHistory",
                column: "SchoolOrCollegeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_UniversityId",
                table: "StudentAcademicHistory",
                column: "UniversityId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAcademicHistory_EducationBoards_BoardId",
                table: "StudentAcademicHistory",
                column: "BoardId",
                principalTable: "EducationBoards",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAcademicHistory_EducationGroups_GroupId",
                table: "StudentAcademicHistory",
                column: "GroupId",
                principalTable: "EducationGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_InstitutionId",
                table: "StudentAcademicHistory",
                column: "InstitutionId",
                principalTable: "EducationalInstitutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_SchoolOrColl~",
                table: "StudentAcademicHistory",
                column: "SchoolOrCollegeId",
                principalTable: "EducationalInstitutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_UniversityId",
                table: "StudentAcademicHistory",
                column: "UniversityId",
                principalTable: "EducationalInstitutions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentAcademicHistory_EducationBoards_BoardId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAcademicHistory_EducationGroups_GroupId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_InstitutionId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_SchoolOrColl~",
                table: "StudentAcademicHistory");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAcademicHistory_EducationalInstitutions_UniversityId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropIndex(
                name: "IX_StudentAcademicHistory_BoardId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropIndex(
                name: "IX_StudentAcademicHistory_GroupId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropIndex(
                name: "IX_StudentAcademicHistory_InstitutionId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropIndex(
                name: "IX_StudentAcademicHistory_SchoolOrCollegeId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropIndex(
                name: "IX_StudentAcademicHistory_UniversityId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropColumn(
                name: "BoardId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropColumn(
                name: "InstitutionId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropColumn(
                name: "SchoolOrCollegeId",
                table: "StudentAcademicHistory");

            migrationBuilder.DropColumn(
                name: "UniversityId",
                table: "StudentAcademicHistory");
        }
    }
}
