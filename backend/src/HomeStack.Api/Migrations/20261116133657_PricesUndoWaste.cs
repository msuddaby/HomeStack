using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class PricesUndoWaste : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "UnitPrice",
                table: "inventory_items",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Snapshot",
                table: "inventory_events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Value",
                table: "inventory_events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_events_ItemId",
                table: "inventory_events",
                column: "ItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_inventory_events_ItemId",
                table: "inventory_events");

            migrationBuilder.DropColumn(
                name: "UnitPrice",
                table: "inventory_items");

            migrationBuilder.DropColumn(
                name: "Snapshot",
                table: "inventory_events");

            migrationBuilder.DropColumn(
                name: "Value",
                table: "inventory_events");
        }
    }
}
