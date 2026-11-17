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
    bool? TrackInventory = null,
    // Who this line is split between, when not the receipt's split. Null or empty: the receipt's split.
    List<SplitPartDto>? Split = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
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
    [Required, MaxLength(300)] List<SaveLineRequest> Lines,
    // Splitting: who paid (default: you, when there's a split) and the default split for every line.
    int? PaidByPersonId = null,
    List<SplitPartDto>? Split = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
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
    [StringLength(500)] string? Note,
    int? PaidByPersonId = null,
    List<SplitPartDto>? Split = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
    }
}

// ---------- People and splitting ----------

/// <summary>
/// One person's part of a split. Weights are shared out in proportion: 1 each for an equal split, or the amounts
/// themselves for custom amounts ($900 / $600 of rent).
/// </summary>
public record SplitPartDto(int PersonId, double Weight = 1)
{
    internal static IEnumerable<ValidationResult> Check(List<SplitPartDto>? parts, string field)
    {
        if (parts is null) yield break;
        if (parts.Count > 50) yield return new("Too many people in a split.", [field]);
        if (parts.Any(p => p.Weight is <= 0 or > 10_000_000 || double.IsNaN(p.Weight))) yield return new("Split shares must be more than zero.", [field]);
        if (parts.Select(p => p.PersonId).Distinct().Count() != parts.Count) yield return new("Someone is in the split twice.", [field]);
    }
}

public record PersonDto(int Id, string Name, int? UserId, bool Archived, bool IsMe);
public record PersonRequest([Required, StringLength(80, MinimumLength = 1)] string Name);

public record LineSplitRequest(int LineId, List<SplitPartDto>? Split);

/// <summary>Change who paid and how a saved expense is split (lines not listed keep their split).</summary>
public record SetSplitRequest(int? PaidByPersonId, List<SplitPartDto>? Split, List<LineSplitRequest>? Lines) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
        foreach (var l in Lines ?? []) foreach (var r in SplitPartDto.Check(l.Split, nameof(Lines))) yield return r;
    }
}

/// <summary>What someone owes for an expense; <see cref="Lines"/> is their part of each line, before tax.</summary>
public record ShareDto(int Id, int PersonId, string Person, decimal Amount, DateTime? ReceivedAt, List<ShareLineDto> Lines,
    DateTime? RemindedAt = null, bool IsMember = false);
/// <summary>One person's part of a receipt line (<see cref="Amount"/>) out of the whole line (<see cref="LineAmount"/>).</summary>
public record ShareLineDto(int LineId, string Name, decimal Amount, decimal LineAmount);

public record OwedItemDto(int ShareId, int ReceiptId, string What, DateOnly Date, decimal Amount, DateTime? ReceivedAt, DateTime? RemindedAt);
/// <summary>What one person owes another, outstanding first.</summary>
/// <param name="IsMember">On HomeStack, so they can be sent a reminder.</param>
public record BalanceDto(int PersonId, string Person, int OwedToPersonId, string OwedTo, decimal Outstanding, List<OwedItemDto> Items, bool IsMember);
public record BalancesDto(bool CanMarkReceived, int? MyPersonId, List<BalanceDto> Balances);
public record MarkReceivedRequest(bool Received);
/// <summary>Remind <see cref="PersonId"/> of what they owe <see cref="OwedToPersonId"/>: everything, or one share.</summary>
public record RemindRequest(int PersonId, int OwedToPersonId, int? ShareId = null);
public record RemindResultDto(int Devices, DateTime RemindedAt);

// ---------- Bills ----------

public record RecurringRefDto(int Id, string Name, int? PaidByPersonId, List<SplitPartDto> Split);

public record BillDraftDto(
    string? Payee, DateOnly? StatementDate, DateOnly? DueDate, decimal? Amount, string Category, string? Account,
    DateOnly? PeriodStart, DateOnly? PeriodEnd, string? Note,
    // The expected bill or recurring payment it looks like (same payee): its split is the starting point.
    RecurringRefDto? Recurring,
    int? DuplicateOf);

public record SaveBillRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Payee,
    DateOnly StatementDate,
    DateOnly? DueDate,
    [Range(0.01, 1000000)] decimal Amount,
    [Required] string Category,
    [StringLength(120)] string? Description,
    [StringLength(500)] string? Note,
    int? PaidByPersonId,
    List<SplitPartDto>? Split,
    int? RecurringId,
    // When the bill was paid to the company; null if not yet.
    DateOnly? PaidOn) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
    }
}

/// <summary>Mark a bill paid to the company (with the date), or not paid (null).</summary>
public record BillPaidRequest(DateOnly? PaidOn);

// ---------- Recurring payments and expected bills ----------

public record RecurringDto(
    int Id, string Name, string? Payee, string Category, decimal? Amount, Frequency Frequency, DateOnly NextDate,
    int? PaidByPersonId, string? PaidBy, List<SplitPartDto> Split, bool Active, string? Note,
    // No fixed amount: an expected bill, waiting to be uploaded each period.
    bool Expected,
    // The latest expense recorded for it.
    int? LastReceiptId, DateOnly? LastDate, decimal? LastAmount);

public record RecurringRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? Payee,
    [Required] string Category,
    // Null: an expected bill whose amount changes (uploaded each time).
    [Range(0.01, 1000000)] decimal? Amount,
    Frequency Frequency,
    DateOnly NextDate,
    int? PaidByPersonId,
    List<SplitPartDto>? Split,
    bool Active = true,
    [StringLength(500)] string? Note = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!ExpenseCategories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
        foreach (var r in SplitPartDto.Check(Split, nameof(Split))) yield return r;
    }
}

public enum UpcomingKind { Bill, Recurring, Expected }

/// <summary>
/// Something coming up: an unpaid bill's due date, a fixed payment that will be recorded, or an expected bill
/// (<see cref="Missing"/> once it's late and still not uploaded).
/// </summary>
public record UpcomingDto(UpcomingKind Kind, DateOnly Date, string Name, decimal? Amount, int? ReceiptId, int? RecurringId,
    bool Overdue, bool Missing);

public record UpdateReceiptRequest(
    [StringLength(80)] string? Store,
    DateOnly PurchasedAt,
    [StringLength(500)] string? Note);

// ---------- Reading saved data ----------

public record ReceiptLineDto(
    int Id, int? ItemId, string? Code, string Name, string? Raw, string Category, double Quantity, string? Unit,
    decimal? UnitPrice, decimal Amount, decimal? Discount, List<SplitPartDto> Split);

public record ReceiptSummaryDto(int Id, string? Store, DateOnly PurchasedAt, decimal Total, int LineCount, ReceiptSource Source, bool HasPhoto,
    // Bills: when due and whether paid to the company.
    DateOnly? DueDate = null, DateOnly? BillPaidOn = null,
    // Shares still owed to whoever paid.
    decimal Outstanding = 0);

public record ReceiptDto(
    int Id, string? Store, DateOnly PurchasedAt, decimal? Subtotal, decimal? Tax, decimal Total, string? Note,
    ReceiptSource Source, List<string> Images, List<ReceiptLineDto> Lines, DateTime CreatedAt,
    int? PaidByPersonId, string? PaidBy, List<SplitPartDto> Split, List<ShareDto> Shares,
    DateOnly? DueDate, DateOnly? BillPaidOn, int? RecurringId, string? Document,
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
