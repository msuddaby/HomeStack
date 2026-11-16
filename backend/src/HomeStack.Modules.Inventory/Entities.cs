namespace HomeStack.Modules.Inventory;

public enum LocationKind { Pantry, Fridge, Freezer, Other }

public enum InventoryEventType { Added, Used, Wasted, Moved, Frozen, Thawed }

public class Location
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public LocationKind Kind { get; set; }
    public int Sort { get; set; }
}

/// <summary>What something *is* (e.g. "2% Milk"). Remembers barcode, category and shelf-life overrides.</summary>
public class Product
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    public string? Brand { get; set; }
    public string Category { get; set; } = "other";
    public string? Barcode { get; set; }
    public string? DefaultUnit { get; set; }
    public string? ImageUrl { get; set; }
    public int? PantryDays { get; set; }
    public int? FridgeDays { get; set; }
    public int? FreezerDays { get; set; }
    /// <summary>Low-stock threshold; null = not tracked.</summary>
    public double? MinQuantity { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>A specific thing on a shelf: a carton of milk in the fridge, two steaks in the freezer.</summary>
public class InventoryItem
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public int LocationId { get; set; }
    public Location? Location { get; set; }
    public double Quantity { get; set; } = 1;
    public string? Unit { get; set; }
    public DateOnly? PurchasedAt { get; set; }
    public DateOnly? ExpiresAt { get; set; }
    public DateOnly? OpenedAt { get; set; }
    public DateOnly? FrozenAt { get; set; }
    public string? Notes { get; set; }
    /// <summary>What one unit cost, so using or wasting part of an item carries its share of the price.</summary>
    public decimal? UnitPrice { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Append-only history, for waste stats and consumption-based shopping suggestions later.</summary>
public class InventoryEvent
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public int ProductId { get; set; }
    public int? ItemId { get; set; }
    public int UserId { get; set; }
    public InventoryEventType Type { get; set; }
    public double Quantity { get; set; }
    public string? Unit { get; set; }
    /// <summary>Price of the amount used or wasted, when the item had a price.</summary>
    public decimal? Value { get; set; }
    /// <summary>JSON copy of the item just before this event, so the action can be undone.</summary>
    public string? Snapshot { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}
