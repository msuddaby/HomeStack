using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class Notifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ServerUrl = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AccessToken = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DigestHour = table.Column<int>(type: "INTEGER", nullable: false),
                    AppUrl = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LastSentOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    LastSentAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_settings", x => x.HouseholdId);
                    table.ForeignKey(
                        name: "FK_notification_settings_households_HouseholdId",
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
                name: "notification_settings");
        }
    }
}
