using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StudentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    ApplicantName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FatherName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MotherName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: true),
                    PlaceOfBirth = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MaritalStatus = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Nationality = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ApplicantMobile = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MailingAddressDetails = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    MailingUpazilaId = table.Column<Guid>(type: "uuid", nullable: true),
                    MailingCellNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    PermanentAddressDetails = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PermanentUpazilaId = table.Column<Guid>(type: "uuid", nullable: true),
                    PermanentCellNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    GuardianName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GuardianRelationship = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GuardianAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    GuardianCellNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_Upazilas_MailingUpazilaId",
                        column: x => x.MailingUpazilaId,
                        principalTable: "Upazilas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentProfiles_Upazilas_PermanentUpazilaId",
                        column: x => x.PermanentUpazilaId,
                        principalTable: "Upazilas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentAcademicHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Examination = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SchoolOrCollege = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Board = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Group = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    University = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Institution = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Year = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MarksObtained = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    MarksPercentage = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Cgpa = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Class = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Division = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAcademicHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAcademicHistory_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentAdmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    DegreeId = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdmissionSerialNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AdmissionExamRoll = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ClassRoll = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AssignedHallId = table.Column<Guid>(type: "uuid", nullable: true),
                    RegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    NormalizedRegistrationNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAdmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAdmissions_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAdmissions_Degrees_DegreeId",
                        column: x => x.DegreeId,
                        principalTable: "Degrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAdmissions_Halls_AssignedHallId",
                        column: x => x.AssignedHallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentAdmissions_StudentProfiles_StudentProfileId",
                        column: x => x.StudentProfileId,
                        principalTable: "StudentProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAcademicHistory_StudentProfileId_SortOrder",
                table: "StudentAcademicHistory",
                columns: new[] { "StudentProfileId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAdmissions_AssignedHallId",
                table: "StudentAdmissions",
                column: "AssignedHallId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAdmissions_BatchId",
                table: "StudentAdmissions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAdmissions_DegreeId",
                table: "StudentAdmissions",
                column: "DegreeId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAdmissions_NormalizedRegistrationNumber",
                table: "StudentAdmissions",
                column: "NormalizedRegistrationNumber",
                unique: true,
                filter: "\"NormalizedRegistrationNumber\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAdmissions_StudentProfileId_DegreeId_BatchId",
                table: "StudentAdmissions",
                columns: new[] { "StudentProfileId", "DegreeId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_MailingUpazilaId",
                table: "StudentProfiles",
                column: "MailingUpazilaId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_PermanentUpazilaId",
                table: "StudentProfiles",
                column: "PermanentUpazilaId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentProfiles_UserId",
                table: "StudentProfiles",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentAcademicHistory");

            migrationBuilder.DropTable(
                name: "StudentAdmissions");

            migrationBuilder.DropTable(
                name: "StudentProfiles");
        }
    }
}
