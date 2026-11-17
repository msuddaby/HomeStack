using System.ComponentModel.DataAnnotations;
using HomeStack.Core;
using HomeStack.Modules.Inventory;

namespace HomeStack.Modules.Expenses;

// ---------- Reading a receipt (suggestion for review; nothing is saved) ----------

/// <summary>A tracked item, as shown next to a receipt line: "Tide Pods, last $27.99 at Costco".</summary>
public record ItemRefDto(int Id, string Name, string? LastStore, decimal? LastUnitPrice, bool SkipInventory,
    double? PackQuantity = null, string? PackUnit = null);

/// <summary>
/// What a food line would add to inventory. <see cref="Add"/> is the starting tick: off when the household
/// left this item out of inventory last time. <see cref="Quantity"/>/<see cref="Unit"/> is how much to put away
/// ("2 L" for a line that just says PACE SALSA); <see cref="AmountFrom"/> says where that came from.
/// </summary>
public record DraftFoodDto(string Name, string Category, LocationKind Location, ProductDto? Product, bool Add,
    double Quantity, string? Unit, PackSource AmountFrom);

/// <summary>Where a put-away amount came from: remembered on the item, a size printed on the receipt, or just the receipt quantity.</summary>
public enum PackSource { Receipt, Printed, Remembered }

public record DraftLineDto(
    int Key, string? Code, string? Raw, string Name, string Category, double Quantity, string? Unit,
    decimal? UnitPrice, decimal Amount, decimal? Discount,
    // Bought before: same store and item number (or same name). Linked automatically.
    ItemRefDto? Item,
    // The AI thinks it's this item from another store. Only linked if the user confirms.
    ItemRefDto? Suggestion,
    // Set for food and drink, which can go into inventory.
    DraftFoodDto? Food);

public record ReceiptDraftDto(
    string? Store, DateOnly? PurchasedAt, decimal? Subtotal, decimal? Tax, decimal? Total,
    List<DraftLineDto> Lines, string? Note,
    // A saved receipt with the same store, date and total: probably scanned already.
    int? DuplicateOf);

// ---------- Saving ----------

public record SaveLineRequest(
    // An existing tracked item (auto-linked or a confirmed suggestion). Null: matched by code or created.
    int? ItemId,
    [StringLength(40)] string? Code,
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(200)] string? Raw,
    [Required] string Category,
    [Range(0.001, 100000)] double Quantity,
    [StringLength(20)] string? Unit,
    [Range(-100000, 100000)] decimal? UnitPrice,
    [Range(-100000, 100000)] decimal Amount,
    [Range(0, 100000)] decimal? Discount,
    // Food to put into inventory from this line.
    AddItemRequest? Inventory,
    // For food lines: whether "Put away" was ticked. Remembered on the item for next time. Null for non-food.
    bool? TrackInventory = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
        if (Inventory is not null)
            foreach (var r in DtoValidator.Validate(Inventory))
                yield return new ValidationResult(r.ErrorMessage, r.MemberNames.Select(m => $"inventory.{m}"));
    }
}

public record SaveReceiptRequest(
    [StringLength(80)] string? Store,
    DateOnly PurchasedAt,
    [Range(0, 1000000)] decimal? Subtotal,
    [Range(0, 1000000)] decimal? Tax,
    [Range(-1000000, 1000000)] decimal Total,
    [StringLength(500)] string? Note,
    [Required, MaxLength(300)] List<SaveLineRequest> Lines) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        for (var i = 0; i < (Lines?.Count ?? 0); i++)
            foreach (var r in DtoValidator.Validate(Lines![i]))
                yield return new ValidationResult(r.ErrorMessage, r.MemberNames.Select(m => $"lines[{i}].{m}").ToList());
    }
}

/// <summary>An expense without a receipt to scan: "$40 at the farmers' market".</summary>
public record ManualExpenseRequest(
    [StringLength(80)] string? Store,
    DateOnly PurchasedAt,
    [Range(0.01, 1000000)] decimal Amount,
    [Required] string Category,
    [Required, StringLength(120, MinimumLength = 1)] string Description,
    [StringLength(500)] string? Note) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
    }
}

public record UpdateReceiptRequest(
    [StringLength(80)] string? Store,
    DateOnly PurchasedAt,
    [StringLength(500)] string? Note);

// ---------- Reading saved data ----------

public record ReceiptLineDto(
    int Id, int? ItemId, string? Code, string Name, string? Raw, string Category, double Quantity, string? Unit,
    decimal? UnitPrice, decimal Amount, decimal? Discount);

public record ReceiptSummaryDto(int Id, string? Store, DateOnly PurchasedAt, decimal Total, int LineCount, ReceiptSource Source, bool HasPhoto);

public record ReceiptDto(
    int Id, string? Store, DateOnly PurchasedAt, decimal? Subtotal, decimal? Tax, decimal Total, string? Note,
    ReceiptSource Source, List<string> Images, List<ReceiptLineDto> Lines, DateTime CreatedAt,
    // Inventory items added from this receipt, when saving.
    int AddedToInventory = 0);

/// <summary>How often something is bought, from the dates it was bought on.</summary>
public record RhythmDto(int Purchases, DateOnly LastBought, int DaysSinceLast, int? EveryDays, bool Due);

public record StorePriceDto(string Store, decimal? LastUnitPrice, decimal? LowestUnitPrice, DateOnly LastBought, int Purchases);

public record TrackedItemDto(
    int Id, string Name, string Category, int? ProductId, RhythmDto? Rhythm, decimal? LastUnitPrice, string? LastStore,
    List<string> Codes, bool SkipInventory, double? PackQuantity, string? PackUnit);

public record PurchaseDto(int ReceiptId, DateOnly Date, string? Store, double Quantity, string? Unit, decimal? UnitPrice, decimal Amount, decimal? Discount);

public record TrackedItemDetailDto(TrackedItemDto Item, List<StorePriceDto> Stores, List<PurchaseDto> Purchases, decimal TotalSpent);

public record UpdateItemRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required] string Category,
    // Null leaves it as it is.
    bool? SkipInventory = null,
    // With UpdatePack: how much one of these is ("2", "L"); null quantity forgets it.
    [Range(0.001, 100000)] double? PackQuantity = null,
    [StringLength(20)] string? PackUnit = null,
    bool UpdatePack = false) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
    }
}

/// <summary>"This is the same thing as that": moves all purchases and store codes onto <see cref="IntoItemId"/>.</summary>
public record MergeItemsRequest(int IntoItemId);

public record MonthTotalDto(string Month, decimal Total, int Receipts);
public record BreakdownDto(string Key, string Label, decimal Total);

public record SummaryDto(
    string Month, decimal Total, decimal? PreviousTotal, List<BreakdownDto> ByCategory, List<BreakdownDto> ByStore,
    List<MonthTotalDto> Months);
