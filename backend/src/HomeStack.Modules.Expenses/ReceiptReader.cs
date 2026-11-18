using System.Text;
using System.Text.RegularExpressions;
using HomeStack.Core;
using HomeStack.Core.Ai;
using HomeStack.Modules.Inventory;

namespace HomeStack.Modules.Expenses;

/// <summary>
/// Reads receipt photos with the household's AI into every line bought (food and everything else), with item
/// numbers, prices and discounts, linked to items bought before. A suggestion to review; nothing is saved here.
/// </summary>
public partial class ReceiptReader(AiService ai, ExpensesService expenses, InventoryService inventory, TimeProvider time)
{
    public const int MaxImages = 4;
    public const long MaxImageBytes = 8 * 1024 * 1024;
    public static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/webp"];

    // Shape the model fills in; everything is optional and checked afterwards.
    private record Reply(string? Store, string? Date, decimal? Subtotal, decimal? Tax, decimal? Total, List<ReplyLine>? Lines, string? Note);
    private record ReplyLine(string? Code, string? Raw, string? Name, double? Quantity, string? Unit, decimal? UnitPrice, decimal? Amount,
        decimal? Discount, string? Category, bool? Food, string? FoodCategory, string? Location, int? SameAs,
        double? PackQuantity, string? PackUnit);

    /// <param name="document">The images are pages of a PDF (usually an online order's receipt or invoice), not photos.</param>
    public async Task<ReceiptDraftDto> ReadAsync(IReadOnlyList<AiImage> images, bool document, CancellationToken ct)
    {
        var user = new StringBuilder()
            .AppendLine(document
                ? (images.Count > 1 ? $"These {images.Count} images are the pages of one receipt, in order." : "This image is a receipt.")
                  + " It's a PDF, probably an online order (receipt, invoice or order confirmation)."
                : images.Count > 1 ? $"These {images.Count} photos are one receipt, top to bottom." : "This photo is a receipt.")
            .AppendLine("List every item bought on it.")
            .AppendLine(Shape)
            .ToString();
        // Long Costco receipts run to 60+ lines with item numbers: leave room.
        var reply = await ai.JsonAsync<Reply>(new AiPrompt(await SystemPromptAsync(), user, images, MaxTokens: 16000), ct);
        return await ToDraftAsync(reply);
    }

    private const string Shape = """
        Reply with JSON only, in this shape:
        {"store": "Costco", "date": "YYYY-MM-DD", "subtotal": 151.23, "tax": 4.10, "total": 155.33,
         "lines": [{"code": "1234567", "raw": "1234567 KS DETERGENT 4.5L 18.99", "name": "Kirkland laundry detergent 4.5 L",
                    "quantity": 1, "unit": null, "unitPrice": 18.99, "amount": 15.99, "discount": 3.00,
                    "category": "household", "food": false, "foodCategory": null, "location": null, "sameAs": null,
                    "packQuantity": null, "packUnit": null}],
         "note": null}
        """;

    private async Task<string> SystemPromptAsync()
    {
        var sb = new StringBuilder("""
            You read shopping receipts for a household's expense tracker. It records every purchase so the household can
            see what they spend, how often they buy things and where they're cheapest.

            Rules:
            - One entry per item bought: food and everything else (cleaning, toiletries, clothing, electronics...).
              Leave out only subtotal, total, tax, payment, change, points, membership-number and survey lines.
            - "code": the store's item number for the line, exactly as printed, digits only. Costco prints a 4-7 digit
              item number before the description; Walmart prints a 12-digit UPC after it. null when there's none.
            - "raw": the line as printed.
            - "name": what a person would call it, expanding abbreviations ("KS DETRGNT 4.5L" -> "Kirkland laundry detergent 4.5 L",
              "GV 2% MLK 4L" -> "Milk 2% 4 L"). For things that aren't food keep brand, variety and size: they tell
              products apart.
            - "quantity": how many ("2 @ 3.99" means 2), or the weight for things sold by weight ("1.25 kg @ 8.80/kg" ->
              1.25 with unit "kg"). "unit" is null for single items.
            - "unitPrice": the price of one (or per weight unit) before discounts.
            - "amount": what was paid for the line after its discounts, as a number. Negative for returns.
            - "discount": savings taken off this item, as a positive number, else null. Costco prints instant savings as
              their own line right after the item, like "TPD/1234567 3.00-" or "/1234567 3.00-": fold it into the item
              with that number and don't list it separately. Same for coupons that name an item.
            - The same item number on several lines is one entry: add up quantity and amount.
            - Deposits, environmental fees and bag charges: their own entry, category "fees".
            - "category": exactly one expense category id from the list below. Snacks, candy, chips, pop, juice boxes and
              other treats are "snacks", not "groceries"; groceries are ingredients and everyday meals.
            - "food": true for food and drink that gets put away at home, else false. When true, also give
              "foodCategory" (one food category id below) and "location" ("fridge", "freezer" or "pantry").
            - "packQuantity"/"packUnit": the total amount in ONE of this line, only when the line itself prints it
              ("SALSA 2X1L" -> 2, "L"; "OLIVE OIL 3L" -> 3, "L"; "RICE 10KG" -> 10, "kg"; "EGGS 24CT" -> 24, null).
              Units: g, kg, ml, L, oz, lb. null when no size is printed. Don't guess from what the product usually is.
            - "sameAs": the id of a known item (list below) when this line is clearly the same product bought at another
              store: same brand, variety and size. Otherwise null. Don't guess.
            - Online orders (a PDF receipt, invoice or order confirmation): list only what was charged. Leave out items
              marked unavailable, out of stock, cancelled or refunded; a substitution is the item that was delivered,
              at its price. Delivery, service and bag fees are "fees"; a driver tip is its own entry, category "other".
              "date" is the order or delivery date printed on it. Item numbers are often missing: "code" null then.
            - "store": the store's short name ("Costco", "Walmart", "Superstore"), also for online orders
              ("Walmart.ca" -> "Walmart"). "tax": all tax lines added up.
            - Never invent lines. If you can't read one, leave it out. If the image isn't a receipt, return no lines and
              explain in "note".

            Expense category ids:
            """);
        sb.AppendLine();
        foreach (var c in ExpenseCategories.All) sb.AppendLine($"- {c.Id}: {c.Label}");
        sb.AppendLine().AppendLine("Food category ids:");
        foreach (var c in Categories.All) sb.AppendLine($"- {c.Id}: {c.Label}");

        var stores = await expenses.GetStoreNamesAsync();
        if (stores.Count > 0)
            sb.AppendLine().AppendLine("Stores this household uses. If the receipt is from one, use that exact name: " + string.Join("; ", stores));

        var known = await expenses.GetKnownItemsAsync(300);
        if (known.Count > 0)
        {
            sb.AppendLine().AppendLine("Known items (id, name, stores bought at), for \"sameAs\":");
            foreach (var (id, name, at) in known) sb.AppendLine($"- {id}: {name} ({string.Join(", ", at)})");
        }

        var products = (await inventory.GetAllProductsAsync()).Select(p => p.Name).Take(300).ToList();
        if (products.Count > 0)
            sb.AppendLine().AppendLine("Food names this household already uses. When a food line is the same thing, use the exact name: "
                + string.Join("; ", products));
        return sb.ToString();
    }

    private async Task<ReceiptDraftDto> ToDraftAsync(Reply reply)
    {
        var today = time.Today();
        var storeName = Clean(reply.Store, 80);
        var store = await expenses.FindStoreAsync(storeName);
        storeName = store?.Name ?? storeName;

        var lines = new List<(int Key, string? Code, string? Raw, string Name, string Category, double Quantity, string? Unit,
            decimal? UnitPrice, decimal Amount, decimal? Discount, int? ItemId, int? SuggestionId, DraftFoodDto? Food)>();
        foreach (var r in reply.Lines ?? [])
        {
            if (Clean(r.Name, 120) is not { } name) continue;
            var quantity = r.Quantity is > 0 and <= 10000 ? Math.Round(r.Quantity.Value, 3) : 1;
            var unitPrice = Money(r.UnitPrice);
            var amount = Money(r.Amount) ?? (unitPrice is { } u ? Math.Round(u * (decimal)quantity, 2) : null);
            if (amount is null) continue;
            var code = r.Code is null ? null : NotCode().Replace(r.Code, "") is { Length: > 0 and <= 40 } c ? c : null;
            var category = ExpenseCategories.Exists(r.Category) ? r.Category! : r.Food == true ? "groceries" : "other";

            var itemId = await expenses.FindItemIdAsync(store?.Id, code, name);
            int? suggestion = itemId is null && r.SameAs is int s ? s : null;

            DraftFoodDto? food = null;
            if (r.Food == true)
            {
                var product = await inventory.FindByNameAsync(name);
                var foodCategory = product?.Category ?? (r.FoodCategory is { } fc && Categories.Exists(fc) ? fc : Categories.Guess(name));
                var location = Enum.TryParse<LocationKind>(r.Location, ignoreCase: true, out var l) && l != LocationKind.Other
                    ? l : Categories.Get(foodCategory).DefaultLocation;
                // A size printed on the line ("SALSA 2X1L"), else just what the receipt says (1, or 2.1 kg).
                food = r.PackQuantity is > 0 and <= 100000 && Clean(r.PackUnit, 20) is var pu
                    ? new DraftFoodDto(product?.Name ?? name, foodCategory, location, product, true,
                        Math.Round(quantity * r.PackQuantity.Value, 3), pu, PackSource.Printed)
                    : new DraftFoodDto(product?.Name ?? name, foodCategory, location, product, true, quantity, Clean(r.Unit, 20), PackSource.Receipt);
            }
            lines.Add((lines.Count, code, Clean(r.Raw, 200), name, category, quantity, Clean(r.Unit, 20), unitPrice, amount.Value,
                Money(r.Discount) is > 0 and var d ? d : null, itemId, suggestion, food));
        }

        // Look up the linked and suggested items in one go; a suggested id the AI made up is dropped here.
        var refs = await expenses.GetItemRefsAsync(lines.SelectMany(l => new[] { l.ItemId, l.SuggestionId }).OfType<int>());
        var draftLines = lines.Select(l =>
        {
            var item = l.ItemId is int i ? refs.GetValueOrDefault(i) : null;
            var food = l.Food is { } f
                ? f with { Add = item?.SkipInventory != true } // left out of inventory last time (a case of pop): start unticked
                : null;
            // What one of these was last time beats anything on the receipt: the household entered it.
            if (food is not null && item?.PackQuantity is double pack)
                food = food with { Quantity = Math.Round(l.Quantity * pack, 3), Unit = item.PackUnit, AmountFrom = PackSource.Remembered };
            return new DraftLineDto(l.Key, l.Code, l.Raw, l.Name, l.Category, l.Quantity, l.Unit, l.UnitPrice, l.Amount, l.Discount,
                item, l.SuggestionId is int s ? refs.GetValueOrDefault(s) : null, food);
        }).ToList();

        DateOnly? date = DateOnly.TryParse(reply.Date, out var dt) && dt <= today && dt >= today.AddYears(-2) ? dt : null;
        draftLines = await MarkAlreadyPutAwayAsync(draftLines, date ?? today);
        var total = Money(reply.Total) ?? (draftLines.Count > 0 ? draftLines.Sum(l => l.Amount) + (Money(reply.Tax) ?? 0) : null);
        return new ReceiptDraftDto(storeName, date, Money(reply.Subtotal), Money(reply.Tax), total, draftLines, Clean(reply.Note, 300),
            await expenses.FindDuplicateAsync(store?.Id, date, total));
    }

    /// <summary>
    /// Food already put away from the shopping list on this trip (an unpriced item added since the day before the
    /// receipt) starts unticked, so scanning the receipt afterwards doesn't add it twice.
    /// </summary>
    private async Task<List<DraftLineDto>> MarkAlreadyPutAwayAsync(List<DraftLineDto> lines, DateOnly bought)
    {
        static int? ProductOf(DraftLineDto l) => l.Food?.Product?.Id ?? l.Item?.ProductId;
        var recent = await inventory.GetRecentUnpricedAsync(lines.Select(ProductOf).OfType<int>().Distinct().ToList(), bought.AddDays(-1));
        var claimed = new HashSet<int>();
        return lines.Select(l =>
        {
            // One receipt line per put-away item: two lines of milk don't both match the one carton.
            if (l.Food is not { } f || ProductOf(l) is not int pid || !recent.TryGetValue(pid, out var item) || !claimed.Add(item.Id)) return l;
            return l with { Food = f with { Add = false, AlreadyPutAway = new AlreadyPutAwayDto(item.Id, item.PurchasedAt, item.Quantity, item.Unit, item.Location.Name) } };
        }).ToList();
    }

    private static decimal? Money(decimal? d) => d is { } v && Math.Abs(v) < 1_000_000 ? Math.Round(v, 2) : null;

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max];
    }

    [GeneratedRegex("[^0-9A-Za-z]")]
    private static partial Regex NotCode();
}
