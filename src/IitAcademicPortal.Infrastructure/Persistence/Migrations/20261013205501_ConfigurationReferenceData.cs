using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigurationReferenceData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AcademicSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AcademicSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Degrees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Degrees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Divisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Divisions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationalInstitutions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationalInstitutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationBoards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    NormalizedCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationBoards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EducationGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EducationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExaminationTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExaminationTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Semesters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Semesters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Districts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DivisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Districts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Districts_Divisions_DivisionId",
                        column: x => x.DivisionId,
                        principalTable: "Divisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DegreeId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartingSemesterId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcademicSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Batches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Batches_AcademicSessions_AcademicSessionId",
                        column: x => x.AcademicSessionId,
                        principalTable: "AcademicSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Batches_Degrees_DegreeId",
                        column: x => x.DegreeId,
                        principalTable: "Degrees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Batches_Semesters_StartingSemesterId",
                        column: x => x.StartingSemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Upazilas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Upazilas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Upazilas_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AcademicSessions_NormalizedName",
                table: "AcademicSessions",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_AcademicSessionId",
                table: "Batches",
                column: "AcademicSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_DegreeId_NormalizedName",
                table: "Batches",
                columns: new[] { "DegreeId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Batches_NormalizedName",
                table: "Batches",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Batches_StartingSemesterId",
                table: "Batches",
                column: "StartingSemesterId");

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
                name: "IX_Districts_DivisionId_NormalizedName",
                table: "Districts",
                columns: new[] { "DivisionId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Districts_NormalizedName",
                table: "Districts",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Divisions_NormalizedName",
                table: "Divisions",
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
                name: "IX_EducationGroups_NormalizedName",
                table: "EducationGroups",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExaminationTypes_NormalizedName",
                table: "ExaminationTypes",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_NormalizedName",
                table: "Semesters",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Upazilas_DistrictId_NormalizedName",
                table: "Upazilas",
                columns: new[] { "DistrictId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Upazilas_NormalizedName",
                table: "Upazilas",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Batches");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "EducationalInstitutions");

            migrationBuilder.DropTable(
                name: "EducationBoards");

            migrationBuilder.DropTable(
                name: "EducationGroups");

            migrationBuilder.DropTable(
                name: "ExaminationTypes");

            migrationBuilder.DropTable(
                name: "Upazilas");

            migrationBuilder.DropTable(
                name: "AcademicSessions");

            migrationBuilder.DropTable(
                name: "Degrees");

            migrationBuilder.DropTable(
                name: "Semesters");

            migrationBuilder.DropTable(
                name: "Districts");

            migrationBuilder.DropTable(
                name: "Divisions");
        }
    }
}
