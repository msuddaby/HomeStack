namespace HomeStack.Modules.Shopping;

public enum ShoppingSource { Manual, LowStock }

public class ShoppingItem
{
    public int Id { get; set; }
    public int HouseholdId { get; set; }
    public required string Name { get; set; }
    /// <summary>Linked inventory product, when known. Put-away uses it; free-text items get a product created then.</summary>
    public int? ProductId { get; set; }
    /// <summary>Inventory category id; drives store-section grouping and put-away defaults.</summary>
    public string Category { get; set; } = "other";
    public double? Quantity { get; set; }
    public string? Unit { get; set; }
    public string? Note { get; set; }
    public ShoppingSource Source { get; set; }
    public int AddedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CheckedAt { get; set; }
    public int? CheckedByUserId { get; set; }
    /// <summary>
    /// Auto-added (low stock) items the user removed. Kept hidden so they don't reappear until the
    /// product is restocked, at which point the row is deleted.
    /// </summary>
    public DateTime? DismissedAt { get; set; }
}
