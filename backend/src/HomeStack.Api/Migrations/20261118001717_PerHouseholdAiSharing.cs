using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerHouseholdAiSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "UseSharedAi",
                table: "households",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Sharing with everyone becomes sharing with each household, so nothing changes until the admin unticks some.
            migrationBuilder.Sql("""
                UPDATE "households" SET "UseSharedAi" = 1
                WHERE EXISTS (SELECT 1 FROM "instance_settings" WHERE "ShareAi" = 1);
                """);

            migrationBuilder.DropTable(
                name: "instance_settings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UseSharedAi",
                table: "households");

            migrationBuilder.CreateTable(
                name: "instance_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    ShareAi = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_settings", x => x.Id);
                });
        }
    }
}
