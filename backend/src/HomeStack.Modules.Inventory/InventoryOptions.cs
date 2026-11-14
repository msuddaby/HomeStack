namespace HomeStack.Modules.Inventory;

/// <summary>Bound from the "Inventory" config section.</summary>
public class InventoryOptions
{
    /// <summary>Items expiring within this many days show as "expiring soon".</summary>
    public int ExpiringSoonDays { get; set; } = 3;
    /// <summary>Freezable fridge items expiring within this many days show as "freeze it now".</summary>
    public int FreezeWindowDays { get; set; } = 2;
    /// <summary>Freezer items past this fraction of their freezer life show as "long in freezer".</summary>
    public double FreezerWarnRatio { get; set; } = 0.8;
}
