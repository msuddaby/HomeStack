namespace HomeStack.Modules.Meals;

/// <summary>Recognises cooking units and converts within mass, volume and count families.</summary>
public static class Units
{
    public enum Family { Mass, Volume, Count }

    // Canonical unit -> (family, size in base unit: grams, millilitres, pieces)
    private static readonly Dictionary<string, (Family Family, double Base)> Canonical = new()
    {
        ["g"] = (Family.Mass, 1), ["kg"] = (Family.Mass, 1000), ["oz"] = (Family.Mass, 28.3495), ["lb"] = (Family.Mass, 453.592),
        ["ml"] = (Family.Volume, 1), ["l"] = (Family.Volume, 1000), ["tsp"] = (Family.Volume, 4.929), ["tbsp"] = (Family.Volume, 14.787),
        ["cup"] = (Family.Volume, 236.6), ["fl oz"] = (Family.Volume, 29.57), ["pint"] = (Family.Volume, 473.2), ["quart"] = (Family.Volume, 946.4),
        ["pc"] = (Family.Count, 1), ["dozen"] = (Family.Count, 12),
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["g"] = "g", ["gram"] = "g", ["grams"] = "g", ["gr"] = "g",
        ["kg"] = "kg", ["kilo"] = "kg", ["kilos"] = "kg", ["kilogram"] = "kg", ["kilograms"] = "kg",
        ["oz"] = "oz", ["ounce"] = "oz", ["ounces"] = "oz",
        ["lb"] = "lb", ["lbs"] = "lb", ["pound"] = "lb", ["pounds"] = "lb",
        ["ml"] = "ml", ["millilitre"] = "ml", ["milliliter"] = "ml", ["millilitres"] = "ml", ["milliliters"] = "ml",
        ["l"] = "l", ["litre"] = "l", ["liter"] = "l", ["litres"] = "l", ["liters"] = "l",
        ["tsp"] = "tsp", ["teaspoon"] = "tsp", ["teaspoons"] = "tsp", ["t"] = "tsp",
        ["tbsp"] = "tbsp", ["tbs"] = "tbsp", ["tablespoon"] = "tbsp", ["tablespoons"] = "tbsp", ["T"] = "tbsp",
        ["cup"] = "cup", ["cups"] = "cup", ["c"] = "cup",
        ["fl oz"] = "fl oz", ["pint"] = "pint", ["pints"] = "pint", ["quart"] = "quart", ["quarts"] = "quart",
        ["pc"] = "pc", ["pcs"] = "pc", ["piece"] = "pc", ["pieces"] = "pc", ["whole"] = "pc", ["each"] = "pc", ["ea"] = "pc",
        ["dozen"] = "dozen",
    };

    /// <summary>Units that describe a package rather than an amount; kept as-is, not convertible.</summary>
    public static readonly HashSet<string> Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        "can", "cans", "jar", "jars", "package", "packages", "pkg", "pack", "packs", "bag", "bags", "box", "boxes", "bottle", "bottles",
        "bunch", "bunches", "head", "heads", "clove", "cloves", "slice", "slices", "stick", "sticks", "sprig", "sprigs", "pinch", "dash", "handful", "splash", "drizzle",
    };

    /// <summary>Canonical form of a unit word, or null if it isn't a measurement unit.</summary>
    public static string? Normalize(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        var u = unit.Trim().TrimEnd('.');
        // "T" (tablespoon) vs "t" (teaspoon) is case-sensitive by convention.
        if (u == "T") return "tbsp";
        return Aliases.TryGetValue(u, out var c) ? c : null;
    }

    /// <summary>Convert an amount between units of the same family. Null/empty units count as pieces.</summary>
    public static double? Convert(double amount, string? from, string? to)
    {
        var f = Normalize(from) ?? (string.IsNullOrWhiteSpace(from) ? "pc" : null);
        var t = Normalize(to) ?? (string.IsNullOrWhiteSpace(to) ? "pc" : null);
        if (f is null || t is null)
            return string.Equals(from?.Trim(), to?.Trim(), StringComparison.OrdinalIgnoreCase) ? amount : null;
        var (ff, fb) = Canonical[f];
        var (tf, tb) = Canonical[t];
        return ff == tf ? amount * fb / tb : null;
    }
}
