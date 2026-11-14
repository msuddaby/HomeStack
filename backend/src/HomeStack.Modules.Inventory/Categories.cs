namespace HomeStack.Modules.Inventory;

/// <summary>Shelf-life guidance in days (null = not recommended). Loosely based on USDA FoodKeeper.</summary>
public record CategoryInfo(
    string Id, string Label, string Emoji, LocationKind DefaultLocation,
    int? PantryDays, int? FridgeDays, int? FreezerDays, int ThawedDays)
{
    public bool Freezable => FreezerDays is not null;
}

public static class Categories
{
    public static readonly IReadOnlyList<CategoryInfo> All =
    [
        new("dairy", "Dairy", "🥛", LocationKind.Fridge, null, 7, 90, 3),
        new("cheese", "Cheese", "🧀", LocationKind.Fridge, null, 21, 180, 7),
        new("eggs", "Eggs", "🥚", LocationKind.Fridge, null, 28, null, 1),
        new("meat", "Meat (cuts)", "🥩", LocationKind.Fridge, null, 4, 180, 3),
        new("ground-meat", "Ground meat", "🍔", LocationKind.Fridge, null, 2, 120, 2),
        new("poultry", "Poultry", "🍗", LocationKind.Fridge, null, 2, 270, 2),
        new("seafood", "Seafood", "🐟", LocationKind.Fridge, null, 2, 180, 2),
        new("deli", "Deli & cured", "🥓", LocationKind.Fridge, null, 5, 60, 3),
        new("leftovers", "Leftovers", "🍲", LocationKind.Fridge, null, 4, 90, 2),
        new("fruit", "Fruit", "🍎", LocationKind.Fridge, 5, 10, 240, 2),
        new("vegetables", "Vegetables", "🥦", LocationKind.Fridge, 7, 7, 240, 2),
        new("bakery", "Bread & bakery", "🍞", LocationKind.Pantry, 5, 10, 90, 3),
        new("frozen", "Frozen meals", "🧊", LocationKind.Freezer, null, 3, 240, 2),
        new("dry-goods", "Dry goods", "🌾", LocationKind.Pantry, 365, null, 365, 30),
        new("canned", "Canned & jarred", "🥫", LocationKind.Pantry, 730, 5, null, 3),
        new("condiments", "Condiments & sauces", "🧂", LocationKind.Fridge, 365, 180, null, 7),
        new("beverages", "Beverages", "🧃", LocationKind.Pantry, 270, 10, null, 7),
        new("snacks", "Snacks", "🍪", LocationKind.Pantry, 90, null, 180, 7),
        new("other", "Other", "📦", LocationKind.Pantry, 180, 7, 180, 3),
    ];

    private static readonly Dictionary<string, CategoryInfo> ById = All.ToDictionary(c => c.Id);

    public static bool Exists(string id) => ById.ContainsKey(id);
    public static CategoryInfo Get(string? id) => id is not null && ById.TryGetValue(id, out var c) ? c : ById["other"];

    /// <summary>Days the product lasts in this kind of location (product overrides win), or null if unknown.</summary>
    public static int? ShelfLifeDays(Product p, LocationKind kind)
    {
        var c = Get(p.Category);
        return kind switch
        {
            LocationKind.Fridge => p.FridgeDays ?? c.FridgeDays,
            LocationKind.Freezer => p.FreezerDays ?? c.FreezerDays,
            _ => p.PantryDays ?? c.PantryDays,
        };
    }

    public static bool IsFreezable(Product p) => ShelfLifeDays(p, LocationKind.Freezer) is not null;
}
