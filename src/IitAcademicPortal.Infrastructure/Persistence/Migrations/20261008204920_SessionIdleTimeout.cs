using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IitAcademicPortal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionIdleTimeout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "LastActivityAt",
                table: "AuthSessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "AuthSessions"
                SET "LastActivityAt" = CAST(EXTRACT(EPOCH FROM "CreatedAt") * 10000000 AS bigint) + 621355968000000000;
                """);

            migrationBuilder.AlterColumn<long>(
                name: "LastActivityAt",
                table: "AuthSessions",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastActivityAt",
                table: "AuthSessions");
        }
    }
}
