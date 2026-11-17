using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class ExpensesPackSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "PackQuantity",
                table: "expenses_items",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackUnit",
                table: "expenses_items",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PackQuantity",
                table: "expenses_items");

            migrationBuilder.DropColumn(
                name: "PackUnit",
                table: "expenses_items");
        }
    }
}
