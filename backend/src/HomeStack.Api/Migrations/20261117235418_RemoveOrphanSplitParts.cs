using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOrphanSplitParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Re-splitting used to leave the old split parts behind, belonging to nothing.
            migrationBuilder.Sql("DELETE FROM expenses_split_parts WHERE ReceiptId IS NULL AND LineId IS NULL AND RecurringId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
