using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DegreeCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditsToBeCompleted",
                table: "Degrees",
                type: "numeric(8,2)",
                precision: 8,
                scale: 2,
                nullable: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Degrees_CreditsToBeCompleted_Positive",
                table: "Degrees",
                sql: "\"CreditsToBeCompleted\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Degrees_CreditsToBeCompleted_Positive",
                table: "Degrees");

            migrationBuilder.DropColumn(
                name: "CreditsToBeCompleted",
                table: "Degrees");
        }
    }
}
