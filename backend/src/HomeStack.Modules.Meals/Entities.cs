namespace HomeStack.Modules.Meals;

public enum MealSlot { Breakfast, Lunch, Dinner, Snack }

public class Recipe
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public int Servings { get; set; } = 4;
    public int? PrepMinutes { get; set; }
    public int? CookMinutes { get; set; }
    public string? SourceUrl { get; set; }
    public string? ImageUrl { get; set; }
    /// <summary>Lines starting with "## " are section headings.</summary>
    public List<string> Steps { get; set; } = [];
    public List<string> Tags { get; set; } = [];
    public string? Notes { get; set; }
    public List<RecipeIngredient> Ingredients { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class RecipeIngredient
{
    public int Id { get; set; }
    public int RecipeId { get; set; }
    public int Sort { get; set; }
    /// <summary>The line as written, e.g. "2 cups flour, sifted".</summary>
    public required string Text { get; set; }
    public required string Name { get; set; }
    public double? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
    public bool Optional { get; set; }
    /// <summary>Linked inventory product. Auto-matched by name unless <see cref="LinkLocked"/>.</summary>
    public int? ProductId { get; set; }
    /// <summary>User chose the link (or chose "not tracked"); don't auto-relink.</summary>
    public bool LinkLocked { get; set; }
    /// <summary>Always assume it's in the house (salt, oil, ...).</summary>
    public bool Staple { get; set; }
}

public class MealPlanEntry
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public DateOnly Date { get; set; }
    public MealSlot Slot { get; set; } = MealSlot.Dinner;
    public int? RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    /// <summary>Free text for meals without a recipe ("Takeout", "Leftovers").</summary>
    public string? Title { get; set; }
    public int? Servings { get; set; }
    public string? Note { get; set; }
    public DateTime? CookedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
