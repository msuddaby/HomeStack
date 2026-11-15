using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class Ai : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_settings",
                columns: table => new
                {
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    ApiKey = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    VisionModel = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_settings", x => x.HouseholdId);
                    table.ForeignKey(
                        name: "FK_ai_settings_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_settings");
        }
    }
}
