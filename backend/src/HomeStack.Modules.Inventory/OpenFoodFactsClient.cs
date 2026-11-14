using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace HomeStack.Modules.Inventory;

/// <summary>Looks up product info by barcode in the free Open Food Facts database.</summary>
public class OpenFoodFactsClient(HttpClient http, ILogger<OpenFoodFactsClient> log)
{
    public async Task<BarcodeSuggestion?> LookupAsync(string barcode, CancellationToken ct)
    {
        if (!barcode.All(char.IsAsciiDigit)) return null;
        try
        {
            var res = await http.GetFromJsonAsync<OffResponse>(
                $"api/v2/product/{barcode}.json?fields=product_name,brands,image_front_small_url,categories_tags,quantity", ct);
            var p = res?.Product;
            if (res?.Status != 1 || p is null || string.IsNullOrWhiteSpace(p.ProductName)) return null;
            return new BarcodeSuggestion(
                p.ProductName.Trim(),
                p.Brands?.Split(',')[0].Trim() is { Length: > 0 } b ? b : null,
                MapCategory(p.CategoriesTags ?? []),
                p.ImageFrontSmallUrl,
                p.Quantity);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            log.LogWarning(ex, "Open Food Facts lookup failed for {Barcode}", barcode);
            return null;
        }
    }

    // Ordered most-specific first.
    private static readonly (string Category, string[] Tags)[] Rules =
    [
        ("cheese", ["cheeses"]),
        ("eggs", ["eggs"]),
        ("ground-meat", ["minced-meats", "ground-beef", "ground-meats"]),
        ("poultry", ["poultry", "chicken", "turkey"]),
        ("seafood", ["fishes", "seafood", "fish"]),
        ("deli", ["hams", "sausages", "cold-cuts", "bacon", "charcuterie"]),
        ("frozen", ["frozen-foods", "frozen-desserts", "ice-creams"]),
        ("meat", ["meats", "beef", "pork"]),
        ("dairy", ["dairies", "milks", "yogurts", "creams", "butters"]),
        ("bakery", ["breads", "pastries", "bakery"]),
        ("canned", ["canned-foods", "canned"]),
        ("condiments", ["condiments", "sauces", "spreads", "dressings"]),
        ("beverages", ["beverages", "drinks"]),
        ("snacks", ["snacks", "sweet-snacks", "salty-snacks", "biscuits", "chocolates"]),
        ("fruit", ["fruits"]),
        ("vegetables", ["vegetables"]),
        ("dry-goods", ["pastas", "rices", "cereals", "flours", "legumes", "breakfast-cereals", "cereals-and-potatoes"]),
    ];

    public static string MapCategory(IEnumerable<string> tags)
    {
        var set = tags.Select(t => t.Contains(':') ? t[(t.IndexOf(':') + 1)..] : t).ToHashSet();
        foreach (var (category, keys) in Rules)
            if (keys.Any(set.Contains)) return category;
        return "other";
    }

    private record OffResponse(int Status, OffProduct? Product);
    private record OffProduct(
        [property: JsonPropertyName("product_name")] string? ProductName,
        string? Brands,
        [property: JsonPropertyName("image_front_small_url")] string? ImageFrontSmallUrl,
        [property: JsonPropertyName("categories_tags")] string[]? CategoriesTags,
        string? Quantity);
}
