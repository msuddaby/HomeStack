using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class Expenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expenses_items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    SkipInventory = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expenses_stores",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expenses_item_codes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_item_codes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expenses_item_codes_expenses_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "expenses_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_expenses_item_codes_expenses_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "expenses_stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expenses_receipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    StoreId = table.Column<int>(type: "INTEGER", nullable: true),
                    PurchasedAt = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Subtotal = table.Column<decimal>(type: "TEXT", nullable: true),
                    Tax = table.Column<decimal>(type: "TEXT", nullable: true),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ImageIds = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expenses_receipts_expenses_stores_StoreId",
                        column: x => x.StoreId,
                        principalTable: "expenses_stores",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "expenses_receipt_lines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Code = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Raw = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "TEXT", nullable: true),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Discount = table.Column<decimal>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_receipt_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expenses_receipt_lines_expenses_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "expenses_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_expenses_receipt_lines_expenses_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "expenses_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_item_codes_HouseholdId_StoreId_Code",
                table: "expenses_item_codes",
                columns: new[] { "HouseholdId", "StoreId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_expenses_item_codes_ItemId",
                table: "expenses_item_codes",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_item_codes_StoreId",
                table: "expenses_item_codes",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_items_HouseholdId_Name",
                table: "expenses_items",
                columns: new[] { "HouseholdId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipt_lines_ItemId",
                table: "expenses_receipt_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipt_lines_ReceiptId",
                table: "expenses_receipt_lines",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipts_HouseholdId_PurchasedAt",
                table: "expenses_receipts",
                columns: new[] { "HouseholdId", "PurchasedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipts_StoreId",
                table: "expenses_receipts",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_stores_HouseholdId_Name",
                table: "expenses_stores",
                columns: new[] { "HouseholdId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "expenses_item_codes");

            migrationBuilder.DropTable(
                name: "expenses_receipt_lines");

            migrationBuilder.DropTable(
                name: "expenses_items");

            migrationBuilder.DropTable(
                name: "expenses_receipts");

            migrationBuilder.DropTable(
                name: "expenses_stores");
        }
    }
}
