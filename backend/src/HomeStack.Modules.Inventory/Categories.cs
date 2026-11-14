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

    // Ordered most-specific first. Mirrors the frontend's guessCategory.
    private static readonly (string Category, System.Text.RegularExpressions.Regex Pattern)[] Guesses =
        new (string, string)[]
        {
            ("cheese", @"chees|cheddar|mozzarella|parmesan|brie|feta|gouda|ricotta"),
            ("eggs", @"\beggs?\b"),
            ("ground-meat", @"ground|mince"),
            ("poultry", @"chicken|turkey|duck|wings?\b|drumstick"),
            ("seafood", @"salmon|fish|shrimp|prawn|\bcod\b|tilapia|scallop|crab|lobster"),
            ("deli", @"\bham\b|bacon|salami|sausage|deli|pepperoni|prosciutto|hot ?dog"),
            ("meat", @"beef|steak|pork|lamb|ribs?\b|chops?\b|roast|veal|brisket"),
            ("leftovers", @"leftover"),
            ("frozen", @"frozen|ice cream|pizza|popsicle"),
            ("dairy", @"milk|yog(h)?urt|cream|butter|kefir"),
            ("bakery", @"bread|bagel|buns?\b|tortilla|muffin|croissant|pita|naan|rolls?\b"),
            ("canned", @"\bcan(ned)?\b|soup|broth|stock\b"),
            ("condiments", @"ketchup|mustard|mayo|sauce|dressing|\bjam\b|salsa|relish|syrup|honey|vinegar|\boil\b"),
            ("beverages", @"juice|soda|water|coffee|\btea\b|beer|wine|kombucha"),
            ("snacks", @"chips|cookie|cracker|chocolate|snack|candy|popcorn|pretzel"),
            ("fruit", @"apple|banana|berr|grape|orange|lemon|lime|melon|peach|pear|plum|mango|pineapple|kiwi|cherr|avocado|fruit"),
            ("vegetables", @"lettuce|spinach|carrot|broccoli|onion|pepper|tomato|potato|cucumber|celery|kale|mushroom|zucchini|cabbage|garlic|corn|\bpeas?\b|veg|salad|herb|cilantro|parsley|basil|ginger|scallion"),
            ("dry-goods", @"rice|pasta|spaghetti|noodle|flour|\boats?\b|cereal|sugar|lentil|quinoa|couscous|baking|beans|spice|cumin|paprika|cinnamon|yeast"),
        }.Select(g => (g.Item1, new System.Text.RegularExpressions.Regex(g.Item2, System.Text.RegularExpressions.RegexOptions.IgnoreCase))).ToArray();

    /// <summary>Best-guess category from a free-text name, or "other".</summary>
    public static string Guess(string name) => Guesses.FirstOrDefault(g => g.Pattern.IsMatch(name)).Category ?? "other";
}
