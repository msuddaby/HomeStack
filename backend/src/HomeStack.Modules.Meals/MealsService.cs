using HomeStack.Core;
using HomeStack.Modules.Inventory;
using HomeStack.Modules.Shopping;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Meals;

public class MealsService(HomeStackDbContext db, ICurrentUser me, TimeProvider time, InventoryService inventory, ShoppingService shopping)
{
    /// <summary>Ingredients in stock that expire within this many days count as "uses up expiring food".</summary>
    private const int ExpiringDays = 3;

    private int Hid => me.HouseholdId;
    private IQueryable<Recipe> Recipes => db.Set<Recipe>().Where(r => r.HouseholdId == Hid);
    private IQueryable<MealPlanEntry> Plan => db.Set<MealPlanEntry>().Where(e => e.HouseholdId == Hid);

    // ---------- Availability ----------

    /// <summary>Products and stock levels, loaded once per request and used to evaluate many ingredients.</summary>
    private sealed record Pantry(List<ProductDto> Products, Dictionary<int, ProductDto> ById, Dictionary<int, StockDto> Stock);

    private async Task<Pantry> LoadPantryAsync()
    {
        var products = await inventory.GetAllProductsAsync();
        var stock = (await inventory.GetStockAsync()).ToDictionary(s => s.ProductId);
        return new Pantry(products, products.ToDictionary(p => p.Id), stock);
    }

    private static IngredientDto Evaluate(RecipeIngredient ing, Pantry pantry)
    {
        // Auto-links made at save time can go stale (product added later), so unlocked, unlinked ingredients are re-matched.
        var productId = ing.ProductId ?? (ing.LinkLocked ? null : ProductMatcher.Match(ing.Name, pantry.Products)?.Id);
        var product = productId is int pid ? pantry.ById.GetValueOrDefault(pid) : null;
        var staple = ing.Staple || (!ing.LinkLocked && product is null && ProductMatcher.IsStaple(ing.Name));
        var stock = product is not null ? pantry.Stock.GetValueOrDefault(product.Id) : null;
        var inStock = staple || stock?.Quantity > 0;
        return new IngredientDto(ing.Id, ing.Text, ing.Name, ing.Quantity, ing.Unit, ing.Note, ing.Optional,
            product?.Id, product?.Name, ing.LinkLocked, staple, inStock, stock?.Quantity, product?.DefaultUnit, stock?.SoonestExpiry);
    }

    private static bool IsMissing(IngredientDto i) => !i.InStock && !i.Optional;

    private RecipeSummaryDto Summarize(Recipe r, Pantry pantry)
    {
        var today = time.Today();
        var ings = r.Ingredients.OrderBy(i => i.Sort).Select(i => Evaluate(i, pantry)).ToList();
        var missing = ings.Where(IsMissing).Select(i => i.Name).ToList();
        var expiring = ings.Where(i => i.InStock && !i.Staple && i.SoonestExpiry is { } e && e <= today.AddDays(ExpiringDays))
            .Select(i => new ExpiringUse(i.ProductName ?? i.Name, i.SoonestExpiry!.Value)).OrderBy(e => e.ExpiresAt).ToList();
        int? total = r.PrepMinutes is null && r.CookMinutes is null ? null : (r.PrepMinutes ?? 0) + (r.CookMinutes ?? 0);
        return new RecipeSummaryDto(r.Id, r.Title, r.ImageUrl, r.Tags, total, r.Servings, ings.Count, missing.Count, missing, expiring);
    }

    // ---------- Recipes ----------

    public async Task<List<RecipeSummaryDto>> ListRecipesAsync(string? q, string? tag)
    {
        var query = Recipes.Include(r => r.Ingredients);
        var recipes = await query.ToListAsync();
        if (!string.IsNullOrWhiteSpace(q))
            recipes = recipes.Where(r => r.Title.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase)
                || r.Ingredients.Any(i => i.Name.Contains(q.Trim(), StringComparison.OrdinalIgnoreCase))).ToList();
        if (!string.IsNullOrWhiteSpace(tag))
            recipes = recipes.Where(r => r.Tags.Contains(tag.Trim().ToLowerInvariant())).ToList();
        var pantry = await LoadPantryAsync();
        return recipes.OrderBy(r => r.Title).Select(r => Summarize(r, pantry)).ToList();
    }

    /// <summary>"What can I make?": fewest missing ingredients first, then the ones that use up expiring food.</summary>
    public async Task<List<RecipeSummaryDto>> SuggestAsync()
    {
        var recipes = await Recipes.Include(r => r.Ingredients).ToListAsync();
        var pantry = await LoadPantryAsync();
        return recipes.Select(r => Summarize(r, pantry))
            .Where(s => s.IngredientCount > 0)
            .OrderBy(s => s.MissingCount)
            .ThenByDescending(s => s.UsesExpiring.Count)
            .ThenBy(s => s.UsesExpiring.Select(e => e.ExpiresAt).DefaultIfEmpty(DateOnly.MaxValue).Min())
            .ThenBy(s => s.Title)
            .ToList();
    }

    public async Task<RecipeDto> GetRecipeAsync(int id)
    {
        var r = await LoadRecipe(id);
        return ToDto(r, await LoadPantryAsync());
    }

    public async Task<RecipeDto> CreateRecipeAsync(RecipeRequest req)
    {
        var r = new Recipe { HouseholdId = Hid, Title = req.Title };
        await ApplyAsync(r, req);
        db.Add(r);
        await db.SaveChangesAsync();
        return ToDto(r, await LoadPantryAsync());
    }

    public async Task<RecipeDto> UpdateRecipeAsync(int id, RecipeRequest req)
    {
        var r = await LoadRecipe(id);
        await ApplyAsync(r, req);
        r.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return ToDto(r, await LoadPantryAsync());
    }

    public async Task DeleteRecipeAsync(int id)
    {
        var r = await LoadRecipe(id);
        // Keep planned meals, as free text.
        foreach (var e in await Plan.Where(e => e.RecipeId == id).ToListAsync())
        {
            e.RecipeId = null;
            e.Title ??= r.Title;
        }
        db.Remove(r);
        await db.SaveChangesAsync();
    }

    public async Task<RecipeDto> LinkIngredientAsync(int recipeId, int ingredientId, LinkRequest req)
    {
        var r = await LoadRecipe(recipeId);
        var ing = r.Ingredients.SingleOrDefault(i => i.Id == ingredientId) ?? throw new NotFoundException("Ingredient not found");
        if (req.ProductId is int pid) await inventory.GetProductAsync(pid); // validates it belongs to this household
        ing.ProductId = req.Staple ? null : req.ProductId;
        ing.Staple = req.Staple;
        ing.LinkLocked = true;
        await db.SaveChangesAsync();
        return ToDto(r, await LoadPantryAsync());
    }

    private async Task ApplyAsync(Recipe r, RecipeRequest req)
    {
        r.Title = req.Title.Trim();
        r.Description = Blank(req.Description);
        r.Servings = req.Servings;
        r.PrepMinutes = req.PrepMinutes;
        r.CookMinutes = req.CookMinutes;
        r.SourceUrl = Blank(req.SourceUrl);
        r.ImageUrl = Blank(req.ImageUrl);
        r.Steps = req.Steps.Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        r.Tags = req.Tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).Distinct().ToList();
        r.Notes = Blank(req.Notes);

        // Re-parse ingredient lines, keeping links the user set on lines that didn't change.
        var previous = r.Ingredients.GroupBy(i => i.Text).ToDictionary(g => g.Key, g => g.First());
        var products = await inventory.GetAllProductsAsync();
        var lines = req.Ingredients.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var next = lines.Select((line, i) =>
        {
            var p = IngredientParser.Parse(line);
            var ing = new RecipeIngredient { Sort = i, Text = p.Text, Name = p.Name, Quantity = p.Quantity, Unit = p.Unit, Note = p.Note, Optional = p.Optional };
            if (previous.TryGetValue(p.Text, out var old) && old.LinkLocked)
            {
                ing.ProductId = old.ProductId;
                ing.LinkLocked = true;
                ing.Staple = old.Staple;
            }
            else
            {
                ing.ProductId = ProductMatcher.Match(p.Name, products)?.Id;
                ing.Staple = ing.ProductId is null && ProductMatcher.IsStaple(p.Name);
            }
            return ing;
        }).ToList();

        db.RemoveRange(r.Ingredients);
        r.Ingredients = next;
    }

    // ---------- Cooking ----------

    /// <summary>What cooking this recipe would use from inventory, with suggested amounts (converted between units where possible).</summary>
    public async Task<CookPlanDto> GetCookPlanAsync(int recipeId, int? servings)
    {
        var r = await LoadRecipe(recipeId);
        var pantry = await LoadPantryAsync();
        var factor = (double)(servings ?? r.Servings) / r.Servings;
        var evaluated = r.Ingredients.OrderBy(i => i.Sort).Select(i => (Ing: i, Eval: Evaluate(i, pantry))).Where(x => !x.Eval.Staple).ToList();
        var productIds = evaluated.Where(x => x.Eval.ProductId != null).Select(x => x.Eval.ProductId!.Value).Distinct().ToList();
        var items = (await inventory.GetItemsForProductsAsync(productIds)).GroupBy(i => i.Product.Id).ToDictionary(g => g.Key, g => g.ToList());

        var lines = evaluated.Select(x =>
        {
            var needed = x.Ing.Quantity is double q ? Math.Round(q * factor, 3) : (double?)null;
            var candidates = x.Eval.ProductId is int pid && items.TryGetValue(pid, out var list)
                ? list.Select(i => new CookCandidate(i.Id, i.Quantity, i.Unit, i.Location.Name, i.ExpiresAt)).ToList()
                : [];
            var first = candidates.FirstOrDefault();
            double? suggested = null;
            if (first is not null && needed is double n && Units.Convert(n, x.Ing.Unit, first.Unit) is double converted)
                suggested = Math.Round(Math.Min(converted, first.Quantity), 3);
            return new CookLine(x.Ing.Id, x.Ing.Text, needed, x.Ing.Unit, x.Eval.ProductId, x.Eval.ProductName, candidates, first?.ItemId, suggested);
        }).ToList();

        return new CookPlanDto(r.Id, r.Title, servings ?? r.Servings, lines);
    }

    /// <summary>Takes the chosen amounts out of inventory and marks the planned meal as cooked.</summary>
    public async Task<CookResult> CookAsync(CookRequest req)
    {
        await LoadRecipe(req.RecipeId);
        await using var tx = await db.Database.BeginTransactionAsync();
        foreach (var use in req.Uses)
            await inventory.ConsumeAsync(use.ItemId, new ConsumeRequest(use.Quantity));
        if (req.PlanEntryId is int pe)
        {
            var entry = await Plan.SingleOrDefaultAsync(e => e.Id == pe) ?? throw new NotFoundException("Planned meal not found");
            entry.CookedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        await tx.CommitAsync();
        return new CookResult(req.Uses.Count);
    }

    // ---------- Shopping ----------

    public async Task<ToListResult> AddMissingToListAsync(int recipeId)
    {
        var r = await LoadRecipe(recipeId);
        var pantry = await LoadPantryAsync();
        return await AddToListAsync(r.Ingredients.Select(i => (Evaluate(i, pantry), r.Title)));
    }

    /// <summary>Adds every missing ingredient for the uncooked planned meals in a date range.</summary>
    public async Task<ToListResult> AddPlanMissingToListAsync(PlanShoppingRequest req)
    {
        var entries = await Plan.Where(e => e.Date >= req.From && e.Date <= req.To && e.CookedAt == null && e.RecipeId != null)
            .Include(e => e.Recipe!).ThenInclude(r => r.Ingredients).ToListAsync();
        var pantry = await LoadPantryAsync();
        return await AddToListAsync(entries.SelectMany(e => e.Recipe!.Ingredients.Select(i => (Evaluate(i, pantry), e.Recipe.Title))));
    }

    private async Task<ToListResult> AddToListAsync(IEnumerable<(IngredientDto Ing, string Recipe)> ingredients)
    {
        var missing = ingredients.Where(x => IsMissing(x.Ing))
            .GroupBy(x => x.Ing.ProductId?.ToString() ?? x.Ing.Name.ToLowerInvariant())
            .Select(g => (Ing: g.First().Ing, Recipes: g.Select(x => x.Recipe).Distinct().ToList()))
            .ToList();
        var names = new List<string>();
        foreach (var (ing, recipes) in missing)
        {
            var added = await shopping.AddAsync(new AddShoppingItemRequest(
                Name: ing.ProductId is null ? ing.Name : null, ProductId: ing.ProductId, Category: null,
                Quantity: null, Unit: null, Note: Truncate($"for {string.Join(", ", recipes)}", 200)));
            names.Add(added.Name);
        }
        return new ToListResult(names.Count, names);
    }

    // ---------- Meal plan ----------

    public async Task<List<PlanEntryDto>> GetPlanAsync(DateOnly from, DateOnly to)
    {
        if (to < from || to.DayNumber - from.DayNumber > 62) throw new BadRequestException("Pick a range of up to two months.");
        var entries = await Plan.Where(e => e.Date >= from && e.Date <= to)
            .Include(e => e.Recipe!).ThenInclude(r => r.Ingredients)
            .OrderBy(e => e.Date).ThenBy(e => e.Slot).ThenBy(e => e.Id).ToListAsync();
        var pantry = await LoadPantryAsync();
        return entries.Select(e => ToDto(e, pantry)).ToList();
    }

    public async Task<PlanEntryDto> AddPlanAsync(PlanRequest req)
    {
        var e = new MealPlanEntry { HouseholdId = Hid };
        await ApplyAsync(e, req);
        db.Add(e);
        await db.SaveChangesAsync();
        return ToDto(e, await LoadPantryAsync());
    }

    public async Task<PlanEntryDto> UpdatePlanAsync(int id, PlanRequest req)
    {
        var e = await Plan.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Planned meal not found");
        await ApplyAsync(e, req);
        await db.SaveChangesAsync();
        return ToDto(e, await LoadPantryAsync());
    }

    public async Task DeletePlanAsync(int id)
    {
        var e = await Plan.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Planned meal not found");
        db.Remove(e);
        await db.SaveChangesAsync();
    }

    private async Task ApplyAsync(MealPlanEntry e, PlanRequest req)
    {
        e.Recipe = req.RecipeId is int rid ? await Recipes.Include(r => r.Ingredients).SingleOrDefaultAsync(r => r.Id == rid)
            ?? throw new BadRequestException("Unknown recipe") : null;
        e.RecipeId = e.Recipe?.Id;
        e.Date = req.Date;
        e.Slot = req.Slot;
        e.Title = Blank(req.Title);
        e.Servings = req.Servings;
        e.Note = Blank(req.Note);
    }

    // ---------- helpers ----------

    private async Task<Recipe> LoadRecipe(int id) =>
        await Recipes.Include(r => r.Ingredients).SingleOrDefaultAsync(r => r.Id == id) ?? throw new NotFoundException("Recipe not found");

    private static RecipeDto ToDto(Recipe r, Pantry pantry)
    {
        var ings = r.Ingredients.OrderBy(i => i.Sort).Select(i => Evaluate(i, pantry)).ToList();
        return new RecipeDto(r.Id, r.Title, r.Description, r.Servings, r.PrepMinutes, r.CookMinutes, r.SourceUrl, r.ImageUrl,
            r.Steps, r.Tags, r.Notes, ings, ings.Count(IsMissing), r.UpdatedAt);
    }

    private static PlanEntryDto ToDto(MealPlanEntry e, Pantry pantry) => new(
        e.Id, e.Date, e.Slot, e.RecipeId, e.Recipe?.Title ?? e.Title ?? "Meal", e.Recipe?.ImageUrl, e.Servings, e.Note, e.CookedAt,
        e.Recipe?.Ingredients.Select(i => Evaluate(i, pantry)).Count(IsMissing) ?? 0);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
