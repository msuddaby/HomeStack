using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HomeStack.Api.Migrations
{
    /// <inheritdoc />
    public partial class Meals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meals_recipes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Servings = table.Column<int>(type: "INTEGER", nullable: false),
                    PrepMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    CookMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Steps = table.Column<string>(type: "TEXT", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meals_recipes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "meals_plan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HouseholdId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Slot = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    Servings = table.Column<int>(type: "INTEGER", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    CookedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meals_plan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meals_plan_meals_recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "meals_recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "meals_recipe_ingredients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RecipeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Sort = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Quantity = table.Column<double>(type: "REAL", nullable: true),
                    Unit = table.Column<string>(type: "TEXT", nullable: true),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Optional = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    LinkLocked = table.Column<bool>(type: "INTEGER", nullable: false),
                    Staple = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meals_recipe_ingredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meals_recipe_ingredients_meals_recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "meals_recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_meals_plan_HouseholdId_Date",
                table: "meals_plan",
                columns: new[] { "HouseholdId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_meals_plan_RecipeId",
                table: "meals_plan",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_meals_recipe_ingredients_RecipeId",
                table: "meals_recipe_ingredients",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_meals_recipes_HouseholdId",
                table: "meals_recipes",
                column: "HouseholdId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meals_plan");

            migrationBuilder.DropTable(
                name: "meals_recipe_ingredients");

            migrationBuilder.DropTable(
                name: "meals_recipes");
        }
    }
}
