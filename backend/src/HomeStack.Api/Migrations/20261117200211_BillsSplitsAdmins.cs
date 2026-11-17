using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class BillsSplitsAdmins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAdmin",
                table: "users",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Existing households: whoever set it up (its first user) becomes admin.
            migrationBuilder.Sql("UPDATE users SET IsAdmin = 1 WHERE Id IN (SELECT MIN(Id) FROM users GROUP BY HouseholdId);");

            migrationBuilder.AddColumn<DateOnly>(
                name: "BillPaidOn",
                table: "expenses_receipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DocumentId",
                table: "expenses_receipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "expenses_receipts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaidByPersonId",
                table: "expenses_receipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurringId",
                table: "expenses_receipts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "expenses_people",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_people", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "expenses_recurring",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    NextDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DayOfMonth = table.Column<int>(type: "INTEGER", nullable: false),
                    PaidByPersonId = table.Column<int>(type: "INTEGER", nullable: true),
                    Active = table.Column<bool>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_recurring", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Data = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_files", x => x.Id);
                    table.ForeignKey(
                        name: "FK_files_households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expenses_shares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptId = table.Column<int>(type: "INTEGER", nullable: false),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReceivedByUserId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_shares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expenses_shares_expenses_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "expenses_people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_shares_expenses_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "expenses_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "expenses_split_parts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PersonId = table.Column<int>(type: "INTEGER", nullable: false),
                    Weight = table.Column<double>(type: "REAL", nullable: false),
                    ReceiptId = table.Column<int>(type: "INTEGER", nullable: true),
                    LineId = table.Column<int>(type: "INTEGER", nullable: true),
                    RecurringId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses_split_parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_expenses_split_parts_expenses_people_PersonId",
                        column: x => x.PersonId,
                        principalTable: "expenses_people",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_expenses_split_parts_expenses_receipt_lines_LineId",
                        column: x => x.LineId,
                        principalTable: "expenses_receipt_lines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_expenses_split_parts_expenses_receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "expenses_receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_expenses_split_parts_expenses_recurring_RecurringId",
                        column: x => x.RecurringId,
                        principalTable: "expenses_recurring",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipts_PaidByPersonId",
                table: "expenses_receipts",
                column: "PaidByPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_receipts_RecurringId",
                table: "expenses_receipts",
                column: "RecurringId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_people_HouseholdId",
                table: "expenses_people",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_recurring_HouseholdId_NextDate",
                table: "expenses_recurring",
                columns: new[] { "HouseholdId", "NextDate" });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_shares_PersonId",
                table: "expenses_shares",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_shares_ReceiptId",
                table: "expenses_shares",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_split_parts_LineId",
                table: "expenses_split_parts",
                column: "LineId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_split_parts_PersonId",
                table: "expenses_split_parts",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_split_parts_ReceiptId",
                table: "expenses_split_parts",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_expenses_split_parts_RecurringId",
                table: "expenses_split_parts",
                column: "RecurringId");

            migrationBuilder.CreateIndex(
                name: "IX_files_HouseholdId",
                table: "files",
                column: "HouseholdId");

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_receipts_expenses_people_PaidByPersonId",
                table: "expenses_receipts",
                column: "PaidByPersonId",
                principalTable: "expenses_people",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_expenses_receipts_expenses_recurring_RecurringId",
                table: "expenses_receipts",
                column: "RecurringId",
                principalTable: "expenses_recurring",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expenses_receipts_expenses_people_PaidByPersonId",
                table: "expenses_receipts");

            migrationBuilder.DropForeignKey(
                name: "FK_expenses_receipts_expenses_recurring_RecurringId",
                table: "expenses_receipts");

            migrationBuilder.DropTable(
                name: "expenses_shares");

            migrationBuilder.DropTable(
                name: "expenses_split_parts");

            migrationBuilder.DropTable(
                name: "files");

            migrationBuilder.DropTable(
                name: "expenses_people");

            migrationBuilder.DropTable(
                name: "expenses_recurring");

            migrationBuilder.DropIndex(
                name: "IX_expenses_receipts_PaidByPersonId",
                table: "expenses_receipts");

            migrationBuilder.DropIndex(
                name: "IX_expenses_receipts_RecurringId",
                table: "expenses_receipts");

            migrationBuilder.DropColumn(
                name: "IsAdmin",
                table: "users");

            migrationBuilder.DropColumn(
                name: "BillPaidOn",
                table: "expenses_receipts");

            migrationBuilder.DropColumn(
                name: "DocumentId",
                table: "expenses_receipts");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "expenses_receipts");

            migrationBuilder.DropColumn(
                name: "PaidByPersonId",
                table: "expenses_receipts");

            migrationBuilder.DropColumn(
                name: "RecurringId",
                table: "expenses_receipts");
        }
    }
}
