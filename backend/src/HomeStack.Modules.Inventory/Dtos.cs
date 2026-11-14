using System.ComponentModel.DataAnnotations;
using HomeStack.Core;

namespace HomeStack.Modules.Inventory;

public record LocationDto(int Id, string Name, LocationKind Kind, int Sort);

public record LocationRequest(
    [Required, StringLength(60, MinimumLength = 1)] string Name,
    LocationKind Kind);

public record ProductDto(
    int Id, string Name, string? Brand, string Category, string? Barcode, string? DefaultUnit, string? ImageUrl,
    int? PantryDays, int? FridgeDays, int? FreezerDays, double? MinQuantity);

public record ProductRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(80)] string? Brand,
    [Required] string Category,
    [StringLength(32)] string? Barcode,
    [StringLength(20)] string? DefaultUnit,
    [StringLength(500), Url] string? ImageUrl,
    [Range(0, 3650)] int? PantryDays,
    [Range(0, 3650)] int? FridgeDays,
    [Range(0, 3650)] int? FreezerDays,
    [Range(0, 100000)] double? MinQuantity) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!Categories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
    }
}

public record ItemDto(
    int Id, double Quantity, string? Unit, DateOnly? PurchasedAt, DateOnly? ExpiresAt, DateOnly? OpenedAt,
    DateOnly? FrozenAt, string? Notes, DateTime CreatedAt, ProductDto Product, LocationDto Location);

public record AddItemRequest(
    int? ProductId,
    // Creates a new product on the fly when ProductId is not given.
    ProductRequest? Product,
    int LocationId,
    [Range(0.001, 100000)] double Quantity = 1,
    [StringLength(20)] string? Unit = null,
    DateOnly? PurchasedAt = null,
    // When null, suggested from the product's shelf life (unless NoExpiry).
    DateOnly? ExpiresAt = null,
    bool NoExpiry = false,
    DateOnly? OpenedAt = null,
    DateOnly? FrozenAt = null,
    [StringLength(500)] string? Notes = null) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (ProductId is null && Product is null)
            yield return new ValidationResult("productId or product is required.", [nameof(ProductId)]);
        if (Product is not null)
        {
            foreach (var r in DtoValidator.Validate(Product))
                yield return new ValidationResult(r.ErrorMessage, r.MemberNames.Select(m => $"product.{m}"));
        }
    }
}

public record UpdateItemRequest(
    int LocationId,
    [Range(0.001, 100000)] double Quantity,
    [StringLength(20)] string? Unit,
    DateOnly? PurchasedAt,
    DateOnly? ExpiresAt,
    DateOnly? OpenedAt,
    DateOnly? FrozenAt,
    [StringLength(500)] string? Notes);

public record ConsumeRequest(
    // Amount to remove; null removes the whole item.
    [Range(0.001, 100000)] double? Quantity,
    bool Wasted = false);

public record MoveRequest(int LocationId, bool AdjustExpiry = true);

public record LongFrozenDto(ItemDto Item, int DaysFrozen, int FreezerDays);
public record StockDto(int ProductId, double Quantity, DateOnly? SoonestExpiry);
public record LowStockDto(ProductDto Product, double Quantity);
public record LocationCountDto(int LocationId, string Name, LocationKind Kind, int Items);

public record InventoryDashboardDto(
    IReadOnlyList<ItemDto> Expired,
    IReadOnlyList<ItemDto> ExpiringSoon,
    IReadOnlyList<ItemDto> FreezeSoon,
    IReadOnlyList<LongFrozenDto> LongInFreezer,
    IReadOnlyList<LowStockDto> LowStock,
    IReadOnlyList<LocationCountDto> Counts);

public record BarcodeSuggestion(string Name, string? Brand, string Category, string? ImageUrl, string? Quantity);
public record BarcodeLookupDto(string Source, ProductDto? Product, BarcodeSuggestion? Suggestion);

public static class Mapping
{
    public static LocationDto ToDto(this Location l) => new(l.Id, l.Name, l.Kind, l.Sort);
    public static ProductDto ToDto(this Product p) => new(p.Id, p.Name, p.Brand, p.Category, p.Barcode, p.DefaultUnit,
        p.ImageUrl, p.PantryDays, p.FridgeDays, p.FreezerDays, p.MinQuantity);
    public static ItemDto ToDto(this InventoryItem i) => new(i.Id, i.Quantity, i.Unit, i.PurchasedAt, i.ExpiresAt,
        i.OpenedAt, i.FrozenAt, i.Notes, i.CreatedAt, i.Product!.ToDto(), i.Location!.ToDto());

    public static void Apply(this Product p, ProductRequest r)
    {
        p.Name = r.Name.Trim();
        p.Brand = Blank(r.Brand);
        p.Category = r.Category;
        p.Barcode = Blank(r.Barcode);
        p.DefaultUnit = Blank(r.DefaultUnit);
        p.ImageUrl = Blank(r.ImageUrl);
        p.PantryDays = r.PantryDays;
        p.FridgeDays = r.FridgeDays;
        p.FreezerDays = r.FreezerDays;
        p.MinQuantity = r.MinQuantity;
    }

    public static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
