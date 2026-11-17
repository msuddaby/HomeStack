using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class MergePeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FormerMember",
                table: "expenses_people",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "NotSameAsMember",
                table: "expenses_people",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // People whose account was removed before this: unlink them (a removed then re-added member can then be merged).
            migrationBuilder.Sql("""
                UPDATE expenses_people SET UserId = NULL, Archived = 1, FormerMember = 1
                WHERE UserId IS NOT NULL AND UserId NOT IN (SELECT Id FROM users);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FormerMember",
                table: "expenses_people");

            migrationBuilder.DropColumn(
                name: "NotSameAsMember",
                table: "expenses_people");
        }
    }
}
