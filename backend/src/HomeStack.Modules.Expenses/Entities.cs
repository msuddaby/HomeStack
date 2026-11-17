namespace HomeStack.Modules.Expenses;

public enum ReceiptSource { Scan, Manual }

/// <summary>Where money was spent ("Costco", "Walmart"). Matched by name, ignoring case.</summary>
public class Store
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Something bought again and again ("Tide Pods"), whichever store sold it. Its purchases are the receipt lines
/// that point at it, which gives how often it's bought and what it cost where.
/// </summary>
public class TrackedItem
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>An <see cref="ExpenseCategories"/> id.</summary>
    public string Category { get; set; } = "other";
    /// <summary>The inventory product it went into, when it was food added to inventory.</summary>
    public int? ProductId { get; set; }
    /// <summary>
    /// Food the household doesn't keep in inventory (a case of pop, snacks). Set from the last receipt review:
    /// unticking "Put away" remembers it, ticking it again clears it.
    /// </summary>
    public bool SkipInventory { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A store's own number for a tracked item (Costco item number, Walmart UPC). Same store + code = same item.</summary>
public class ItemCode
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int StoreId { get; set; }
    public required string Code { get; set; }
    public int ItemId { get; set; }
}

/// <summary>One shopping trip or expense: a scanned receipt, or an amount entered by hand.</summary>
public class Receipt
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int? StoreId { get; set; }
    public Store? Store { get; set; }
    public DateOnly PurchasedAt { get; set; }
    public decimal? Subtotal { get; set; }
    public decimal? Tax { get; set; }
    public decimal Total { get; set; }
    public string? Note { get; set; }
    /// <summary>Photos of the receipt (core StoredImages), top to bottom.</summary>
    public List<Guid> ImageIds { get; set; } = [];
    public ReceiptSource Source { get; set; }
    public int UserId { get; set; }
    public List<ReceiptLine> Lines { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class ReceiptLine
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int Sort { get; set; }
    /// <summary>Null for lines that aren't tracked (e.g. a manual expense).</summary>
    public int? ItemId { get; set; }
    public TrackedItem? Item { get; set; }
    /// <summary>The store's item number as printed.</summary>
    public string? Code { get; set; }
    public required string Name { get; set; }
    /// <summary>The line as printed on the receipt.</summary>
    public string? Raw { get; set; }
    public string Category { get; set; } = "other";
    public double Quantity { get; set; } = 1;
    public string? Unit { get; set; }
    public decimal? UnitPrice { get; set; }
    /// <summary>What was paid for the line, after any discount. Negative for a refund.</summary>
    public decimal Amount { get; set; }
    /// <summary>Instant savings / coupon taken off this line, as a positive number.</summary>
    public decimal? Discount { get; set; }
}

public record ExpenseCategory(string Id, string Label, string Emoji);

/// <summary>Spending categories (separate from inventory's food categories).</summary>
public static class ExpenseCategories
{
    public static readonly IReadOnlyList<ExpenseCategory> All =
    [
        new("groceries", "Groceries", "🛒"),
        new("snacks", "Snacks & drinks", "🥤"),
        new("alcohol", "Alcohol", "🍷"),
        new("household", "Household & cleaning", "🧽"),
        new("personal-care", "Personal care", "🧴"),
        new("health", "Health & pharmacy", "💊"),
        new("baby", "Baby & kids", "🍼"),
        new("pets", "Pets", "🐾"),
        new("clothing", "Clothing", "👕"),
        new("home", "Home & garden", "🪴"),
        new("electronics", "Electronics", "🔌"),
        new("dining", "Eating out", "🍽️"),
        new("fuel", "Fuel", "⛽"),
        new("fees", "Deposits & fees", "🧾"),
        new("other", "Other", "📦"),
    ];

    private static readonly HashSet<string> Ids = All.Select(c => c.Id).ToHashSet();
    public static bool Exists(string? id) => id is not null && Ids.Contains(id);
}
