using HomeStack.Modules.Inventory;

namespace HomeStack.Modules.Meals;

/// <summary>Links recipe ingredients to inventory products by name, conservatively.</summary>
public static class ProductMatcher
{
    /// <summary>Always assumed to be in the house.</summary>
    public static readonly HashSet<string> Staples = new(StringComparer.OrdinalIgnoreCase)
    {
        "water", "salt", "pepper", "black pepper", "salt and pepper", "kosher salt", "sea salt", "ice", "ice cubes", "cold water", "warm water", "boiling water",
    };

    public static bool IsStaple(string name) => Staples.Contains(IngredientParser.CoreName(name));

    /// <summary>
    /// Best product for an ingredient name: exact match first, then the product whose name
    /// is contained in the ingredient ("boneless chicken thighs" ⊇ "chicken thighs"), then
    /// an ingredient contained in a product with at most one extra word ("milk" ⊆ "2% milk").
    /// </summary>
    public static ProductDto? Match(string ingredientName, IEnumerable<ProductDto> products)
    {
        var ing = Tokens(IngredientParser.CoreName(ingredientName));
        if (ing.Count == 0) return null;

        ProductDto? best = null;
        var bestScore = int.MaxValue;
        foreach (var p in products)
        {
            var prod = Tokens(p.Name);
            if (prod.Count == 0) continue;
            int score;
            if (prod.SetEquals(ing)) score = 0;
            else if (prod.IsSubsetOf(ing)) score = 10 + (ing.Count - prod.Count);
            else if (ing.IsSubsetOf(prod) && prod.Count - ing.Count <= 1 && !IsModifierOnly(prod, ing)) score = 20 + (prod.Count - ing.Count);
            else continue;
            if (score < bestScore) { best = p; bestScore = score; }
        }
        return best;
    }

    // "peanut butter" shouldn't satisfy "butter": the extra word changes what it is.
    private static readonly HashSet<string> Changers = ["peanut", "almond", "apple", "coconut", "soy", "oat", "rice", "corn", "cream", "sour", "baking", "powdered", "brown", "hot", "sweet"];
    private static bool IsModifierOnly(HashSet<string> prod, HashSet<string> ing) => prod.Except(ing).Any(Changers.Contains);

    public static HashSet<string> Tokens(string s) =>
        s.ToLowerInvariant().Split([' ', '-', ',', '/', '(', ')', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Any(char.IsLetter))
            .Select(Singular).ToHashSet();

    private static string Singular(string w) =>
        w.Length <= 3 ? w
        : w.EndsWith("ies") ? w[..^3] + "y"
        : w.EndsWith("oes") ? w[..^2]
        : w.EndsWith("ches") || w.EndsWith("shes") || w.EndsWith("xes") ? w[..^2]
        : w.EndsWith("ss") ? w
        : w.EndsWith('s') ? w[..^1]
        : w;
}
