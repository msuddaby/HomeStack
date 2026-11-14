using System.ComponentModel.DataAnnotations;
using HomeStack.Core;
using HomeStack.Modules.Inventory;

namespace HomeStack.Modules.Shopping;

public record ShoppingItemDto(
    int Id, string Name, int? ProductId, string Category, double? Quantity, string? Unit, string? Note,
    ShoppingSource Source, DateTime CreatedAt, DateTime? CheckedAt, string? CheckedBy, ProductDto? Product);

public record AddShoppingItemRequest(
    [StringLength(120)] string? Name,
    int? ProductId,
    string? Category,
    [Range(0.001, 100000)] double? Quantity,
    [StringLength(20)] string? Unit,
    [StringLength(200)] string? Note) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (ProductId is null && string.IsNullOrWhiteSpace(Name))
            yield return new ValidationResult("Name or productId is required.", [nameof(Name)]);
        if (Category is not null && !Categories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
    }
}

public record UpdateShoppingItemRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required] string Category,
    [Range(0.001, 100000)] double? Quantity,
    [StringLength(20)] string? Unit,
    [StringLength(200)] string? Note) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (!Categories.Exists(Category))
            yield return new ValidationResult($"Unknown category '{Category}'.", [nameof(Category)]);
    }
}

public record CheckRequest(bool Checked);

public record PutAwayLine(
    int ShoppingItemId,
    // False = just clear it from the list (e.g. paper towels), don't add to inventory.
    bool AddToInventory,
    int? LocationId,
    [Range(0.001, 100000)] double? Quantity,
    [StringLength(20)] string? Unit,
    DateOnly? ExpiresAt,
    bool NoExpiry = false,
    DateOnly? FrozenAt = null);

public record PutAwayRequest([Required, MinLength(1)] List<PutAwayLine> Lines) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        foreach (var (line, i) in Lines.Select((l, i) => (l, i)))
        {
            foreach (var r in DtoValidator.Validate(line))
                yield return new ValidationResult(r.ErrorMessage, r.MemberNames.Select(m => $"lines[{i}].{m}"));
            if (line.AddToInventory && line.LocationId is null)
                yield return new ValidationResult("Choose where it goes.", [$"lines[{i}].locationId"]);
        }
    }
}

public record PutAwayResult(int AddedToInventory, int Cleared, List<ItemDto> Items);
