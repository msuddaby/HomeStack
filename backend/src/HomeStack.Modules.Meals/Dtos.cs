using System.ComponentModel.DataAnnotations;
using HomeStack.Core;

namespace HomeStack.Modules.Meals;

public record RecipeRequest(
    [Required, StringLength(150, MinimumLength = 1)] string Title,
    [StringLength(1000)] string? Description,
    [Range(1, 100)] int Servings,
    [Range(0, 10000)] int? PrepMinutes,
    [Range(0, 10000)] int? CookMinutes,
    [StringLength(500)] string? SourceUrl,
    [StringLength(500)] string? ImageUrl,
    // One ingredient per line, as written.
    List<string> Ingredients,
    List<string> Steps,
    List<string> Tags,
    [StringLength(4000)] string? Notes) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (Ingredients.Count > 100) yield return new("Too many ingredients (max 100).", [nameof(Ingredients)]);
        if (Ingredients.Any(i => i.Length > 300)) yield return new("Ingredient lines must be under 300 characters.", [nameof(Ingredients)]);
        if (Steps.Count > 100 || Steps.Any(s => s.Length > 4000)) yield return new("Too many or too-long steps.", [nameof(Steps)]);
        if (Tags.Count > 20 || Tags.Any(t => t.Length > 30)) yield return new("Too many or too-long tags.", [nameof(Tags)]);
    }
}

public record ImportRequest([Required, StringLength(2000)] string Url);

public record IngredientDto(
    int Id, string Text, string Name, double? Quantity, string? Unit, string? Note, bool Optional,
    int? ProductId, string? ProductName, bool LinkLocked, bool Staple,
    // Availability
    bool InStock, double? OnHand, string? OnHandUnit, DateOnly? SoonestExpiry);

public record RecipeDto(
    int Id, string Title, string? Description, int Servings, int? PrepMinutes, int? CookMinutes, string? SourceUrl, string? ImageUrl,
    List<string> Steps, List<string> Tags, string? Notes, List<IngredientDto> Ingredients, int MissingCount, DateTime UpdatedAt);

public record RecipeSummaryDto(
    int Id, string Title, string? ImageUrl, List<string> Tags, int? TotalMinutes, int Servings,
    int IngredientCount, int MissingCount, List<string> Missing, List<ExpiringUse> UsesExpiring);

public record ExpiringUse(string Name, DateOnly ExpiresAt);

public record LinkRequest(int? ProductId, bool Staple = false);

public record CookLine(
    int IngredientId, string Text, double? Needed, string? NeededUnit, int? ProductId, string? ProductName,
    List<CookCandidate> Candidates, int? SuggestedItemId, double? SuggestedQuantity);

public record CookCandidate(int ItemId, double Quantity, string? Unit, string Location, DateOnly? ExpiresAt);

public record CookPlanDto(int RecipeId, string Title, int Servings, List<CookLine> Lines);

public record CookUse(int ItemId, [Range(0.001, 100000)] double? Quantity);

public record CookRequest(int RecipeId, int? PlanEntryId, List<CookUse> Uses) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        foreach (var (u, i) in Uses.Select((u, i) => (u, i)))
            foreach (var r in DtoValidator.Validate(u))
                yield return new(r.ErrorMessage, r.MemberNames.Select(m => $"uses[{i}].{m}"));
    }
}

public record CookResult(int ItemsUpdated);

public record PlanRequest(
    DateOnly Date,
    MealSlot Slot,
    int? RecipeId,
    [StringLength(120)] string? Title,
    [Range(1, 100)] int? Servings,
    [StringLength(300)] string? Note) : IValidatable
{
    public IEnumerable<ValidationResult> Validate()
    {
        if (RecipeId is null && string.IsNullOrWhiteSpace(Title))
            yield return new("Pick a recipe or enter a meal.", [nameof(Title)]);
    }
}

public record PlanEntryDto(
    int Id, DateOnly Date, MealSlot Slot, int? RecipeId, string Title, string? ImageUrl, int? Servings, string? Note,
    DateTime? CookedAt, int MissingCount);

public record PlanShoppingRequest(DateOnly From, DateOnly To);
public record ToListResult(int Added, List<string> Names);
