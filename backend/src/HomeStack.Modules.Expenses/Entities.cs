namespace HomeStack.Modules.Expenses;

public enum ReceiptSource { Scan, Manual, Bill, Recurring }

/// <summary>
/// Someone an expense can be split with: a household member (<see cref="UserId"/> set, created automatically)
/// or someone who isn't on HomeStack, by name.
/// </summary>
public class Person
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public int? UserId { get; set; }
    /// <summary>Hidden from pickers; their history stays.</summary>
    public bool Archived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One person's part of an expense or a line. Weights are shared out in proportion: equal splits are 1 each,
/// custom amounts are stored as the amounts themselves.
/// </summary>
public class SplitPart
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public double Weight { get; set; } = 1;
    // Exactly one owner: a whole receipt (its default split), one line (an override), or a recurring payment.
    public int? ReceiptId { get; set; }
    public int? LineId { get; set; }
    public int? RecurringId { get; set; }
}

/// <summary>What one person owes whoever paid an expense, worked out from the split. An admin marks it received.</summary>
public class Share
{
    public int Id { get; set; }
    public int ReceiptId { get; set; }
    public int PersonId { get; set; }
    public decimal Amount { get; set; }
    public DateTime? ReceivedAt { get; set; }
    public int? ReceivedByUserId { get; set; }
    /// <summary>When an admin last sent them a reminder about it.</summary>
    public DateTime? RemindedAt { get; set; }
}

public enum Frequency { Weekly, Biweekly, Monthly, Yearly }

/// <summary>
/// A payment that comes round again. With an <see cref="Amount"/> (rent) it's recorded automatically on the day.
/// Without one (electricity) it's an expected bill: it shows as missing until that period's bill is uploaded.
/// </summary>
public class Recurring
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>Who it's paid to ("Enmax", "Landlord"); bills from them are matched to it.</summary>
    public string? Payee { get; set; }
    public string Category { get; set; } = "other";
    public decimal? Amount { get; set; }
    public Frequency Frequency { get; set; } = Frequency.Monthly;
    /// <summary>The next date it's due (fixed) or expected (bills).</summary>
    public DateOnly NextDate { get; set; }
    /// <summary>Monthly/yearly: the day of the month it falls on (31 = last day in shorter months).</summary>
    public int DayOfMonth { get; set; }
    public int? PaidByPersonId { get; set; }
    public bool Active { get; set; } = true;
    public string? Note { get; set; }
    /// <summary>How each occurrence is split (rent between roommates).</summary>
    public List<SplitPart> Split { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

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
    /// <summary>
    /// How much one of these is, for putting away ("PACE SALSA" = 2 L), when the receipt doesn't say.
    /// Learned from what was put away last time; editable on the item.
    /// </summary>
    public double? PackQuantity { get; set; }
    public string? PackUnit { get; set; }
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

    // ---- Splitting ----
    /// <summary>Who paid; everyone else's share is owed to them. Null: not split.</summary>
    public int? PaidByPersonId { get; set; }
    /// <summary>The default split for every line (a line can override it).</summary>
    public List<SplitPart> Split { get; set; } = [];
    public List<Share> Shares { get; set; } = [];

    // ---- Bills ----
    public DateOnly? DueDate { get; set; }
    /// <summary>When the bill itself was paid to the company (separate from members paying their shares).</summary>
    public DateOnly? BillPaidOn { get; set; }
    /// <summary>When members were told this bill is overdue (once per bill).</summary>
    public DateOnly? OverdueNotifiedOn { get; set; }
    /// <summary>The recurring payment or expected bill this is an occurrence of.</summary>
    public int? RecurringId { get; set; }
    /// <summary>The bill's original PDF (a core StoredFile); photos are in <see cref="ImageIds"/>.</summary>
    public Guid? DocumentId { get; set; }
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
    /// <summary>Who this line is split between, when it differs from the receipt's split (snacks for two, a shirt for one).</summary>
    public List<SplitPart> Split { get; set; } = [];
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
        new("housing", "Rent & housing", "🏠"),
        new("utilities", "Utilities", "💡"),
        new("phone-internet", "Phone & internet", "📶"),
        new("insurance", "Insurance", "🛡️"),
        new("subscriptions", "Subscriptions", "🔁"),
        new("dining", "Eating out", "🍽️"),
        new("fuel", "Fuel", "⛽"),
        new("fees", "Deposits & fees", "🧾"),
        new("other", "Other", "📦"),
    ];

    private static readonly HashSet<string> Ids = All.Select(c => c.Id).ToHashSet();
    public static bool Exists(string? id) => id is not null && Ids.Contains(id);
}
