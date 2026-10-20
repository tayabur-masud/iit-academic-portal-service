using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CityCorporationsDegreeMinimumCgpaAndGrades : Migration
    {
        /// <summary>
        /// The ten initial Grade bands with stable ids. Insert-only: a band that already exists (by id or by live letter grade) is
        /// left as it is, so running this again never overwrites an Admin's edit or brings back a deleted band.
        /// </summary>
        public const string InitialGradesSql = """
            INSERT INTO "Grades" ("Id", "Name", "NormalizedName", "IsActive", "MarksLowerBound", "MarksUpperBound", "UpperBoundInclusive", "NumericGrade", "Comment")
            VALUES
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000001', 'A+', 'A+', true, 80, 100, true, 4.00, 'Excellent'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000002', 'A', 'A', true, 75, 80, false, 3.75, 'Better'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000003', 'A-', 'A-', true, 70, 75, false, 3.50, 'Good'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000004', 'B+', 'B+', true, 65, 70, false, 3.25, 'Above Average'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000005', 'B', 'B', true, 60, 65, false, 3.00, 'Average'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000006', 'B-', 'B-', true, 55, 60, false, 2.75, 'Below Average'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000007', 'C+', 'C+', true, 50, 55, false, 2.50, 'Satisfactory'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000008', 'C', 'C', true, 45, 50, false, 2.25, 'Not satisfactory'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-000000000009', 'D', 'D', true, 40, 45, false, 2.00, 'Pass'),
            ('5b0c6a1e-7d3f-5e2a-9c4b-00000000000a', 'F', 'F', true, 0, 40, false, 0.00, 'Fail')
            ON CONFLICT DO NOTHING;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MinimumCgpaForCompletion",
                table: "Degrees",
                type: "numeric(3,2)",
                precision: 3,
                scale: 2,
                nullable: false,
                defaultValue: 2.50m);

            // 2.50 only backfills the degrees that already exist; new degrees must always state their own minimum.
            migrationBuilder.Sql("ALTER TABLE \"Degrees\" ALTER COLUMN \"MinimumCgpaForCompletion\" DROP DEFAULT;");

            migrationBuilder.CreateTable(
                name: "CityCorporations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DistrictId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CityCorporations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CityCorporations_Districts_DistrictId",
                        column: x => x.DistrictId,
                        principalTable: "Districts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarksLowerBound = table.Column<decimal>(type: "numeric", nullable: false),
                    MarksUpperBound = table.Column<decimal>(type: "numeric", nullable: false),
                    UpperBoundInclusive = table.Column<bool>(type: "boolean", nullable: false),
                    NumericGrade = table.Column<decimal>(type: "numeric(3,2)", precision: 3, scale: 2, nullable: false),
                    Comment = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    CreatedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    LastModifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    LastModifiedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    NormalizedName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grades", x => x.Id);
                    table.CheckConstraint("CK_Grades_Comment_NotBlank", "btrim(\"Comment\") <> ''");
                    table.CheckConstraint("CK_Grades_Marks_Range", "\"MarksLowerBound\" >= 0 AND \"MarksUpperBound\" <= 100 AND \"MarksLowerBound\" < \"MarksUpperBound\"");
                    table.CheckConstraint("CK_Grades_NumericGrade_Range", "\"NumericGrade\" >= 0 AND \"NumericGrade\" <= 4");
                    table.CheckConstraint("CK_Grades_UpperBoundInclusive_At100", "NOT \"UpperBoundInclusive\" OR \"MarksUpperBound\" = 100");
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Degrees_MinimumCgpaForCompletion_Range",
                table: "Degrees",
                sql: "\"MinimumCgpaForCompletion\" > 0 AND \"MinimumCgpaForCompletion\" <= 4");

            migrationBuilder.CreateIndex(
                name: "IX_CityCorporations_DistrictId_NormalizedName",
                table: "CityCorporations",
                columns: new[] { "DistrictId", "NormalizedName" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CityCorporations_NormalizedName",
                table: "CityCorporations",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Grades_NormalizedName",
                table: "Grades",
                column: "NormalizedName",
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.Sql(InitialGradesSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CityCorporations");

            migrationBuilder.DropTable(
                name: "Grades");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Degrees_MinimumCgpaForCompletion_Range",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "MinimumCgpaForCompletion",
                table: "Degrees");
        }
    }
}
