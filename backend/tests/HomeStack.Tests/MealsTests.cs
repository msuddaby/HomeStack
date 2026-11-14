using System.Net.Http.Json;
using System.Net;
using HomeStack.Modules.Inventory;
using HomeStack.Modules.Meals;
using HomeStack.Modules.Shopping;

namespace HomeStack.Tests;

public class MealsTests
{
    private static async Task<(TestApp App, HttpClient Client, int Fridge, int Pantry)> Start()
    {
        var app = new TestApp();
        var client = await app.SetupClientAsync();
        var locations = await (await client.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        return (app, client, locations.First(l => l.Kind == LocationKind.Fridge).Id, locations.First(l => l.Kind == LocationKind.Pantry).Id);
    }

    private static object Recipe(string title, params string[] ingredients) => new
    {
        title, servings = 4, ingredients, steps = new[] { "Cook it." }, tags = new[] { "Dinner" },
    };

    private static async Task<ItemDto> Stock(HttpClient c, string name, string category, int location, double qty = 1, string? unit = null, DateOnly? expires = null) =>
        await (await c.Post("/api/inventory/items", new { product = new { name, category }, locationId = location, quantity = qty, unit, expiresAt = expires })).Read<ItemDto>();

    [Fact]
    public async Task Recipe_links_ingredients_and_reports_availability()
    {
        var (app, c, fridge, pantry) = await Start();
        using var _app = app;
        await Stock(c, "Eggs", "eggs", fridge, 12);
        await Stock(c, "Spaghetti", "dry-goods", pantry);

        var r = await (await c.Post("/api/meals/recipes", Recipe("Carbonara", "200 g spaghetti", "3 large eggs", "100g pancetta", "Salt and pepper to taste", "Parsley (optional)"))).Read<RecipeDto>();
        Assert.Equal(["dinner"], r.Tags);
        var byName = r.Ingredients.ToDictionary(i => i.Name);
        Assert.True(byName["spaghetti"].InStock);
        Assert.Equal("Eggs", byName["large eggs"].ProductName);
        Assert.False(byName["pancetta"].InStock);
        Assert.True(byName["Salt and pepper"].Staple);
        Assert.True(byName["Parsley"].Optional);
        Assert.Equal(1, r.MissingCount);

        // Buying pancetta later makes it available without re-saving the recipe.
        await Stock(c, "Pancetta", "deli", fridge);
        Assert.Equal(0, (await (await c.GetAsync($"/api/meals/recipes/{r.Id}")).Read<RecipeDto>()).MissingCount);
    }

    [Fact]
    public async Task Suggestions_rank_by_missing_then_expiring()
    {
        var (app, c, fridge, pantry) = await Start();
        using var _app = app;
        await Stock(c, "Rice", "dry-goods", pantry);
        await Stock(c, "Spinach", "vegetables", fridge, expires: app.Today.AddDays(1));
        await Stock(c, "Onion", "vegetables", pantry);

        await c.Post("/api/meals/recipes", Recipe("Plain rice", "1 cup rice"));
        await c.Post("/api/meals/recipes", Recipe("Spinach rice", "1 cup rice", "2 cups spinach"));
        await c.Post("/api/meals/recipes", Recipe("Onion soup", "4 onions", "1 L beef stock", "Gruyere"));

        var s = await (await c.GetAsync("/api/meals/suggestions")).Read<List<RecipeSummaryDto>>();
        Assert.Equal(["Spinach rice", "Plain rice", "Onion soup"], s.Select(x => x.Title));
        Assert.Equal("Spinach", s[0].UsesExpiring.Single().Name);
        Assert.Equal(2, s[2].MissingCount);
    }

    [Fact]
    public async Task Manual_link_overrides_and_survives_edits()
    {
        var (app, c, fridge, _) = await Start();
        using var _app = app;
        var cream = await Stock(c, "Heavy cream", "dairy", fridge);
        var r = await (await c.Post("/api/meals/recipes", Recipe("Sauce", "1 cup whipping cream"))).Read<RecipeDto>();
        Assert.Null(r.Ingredients[0].ProductId);

        r = await (await c.SendAsync(new HttpRequestMessage(HttpMethod.Put, $"/api/meals/recipes/{r.Id}/ingredients/{r.Ingredients[0].Id}/link")
            { Content = JsonContent.Create(new { productId = cream.Product.Id }) })).Read<RecipeDto>();
        Assert.True(r.Ingredients[0].InStock);

        var put = new HttpRequestMessage(HttpMethod.Put, $"/api/meals/recipes/{r.Id}") { Content = JsonContent.Create(Recipe("Sauce v2", "1 cup whipping cream", "1 tsp salt")) };
        r = await (await c.SendAsync(put)).Read<RecipeDto>();
        Assert.Equal(cream.Product.Id, r.Ingredients[0].ProductId);
        Assert.True(r.Ingredients[0].LinkLocked);
    }

    [Fact]
    public async Task Cooking_deducts_with_unit_conversion_and_marks_plan_cooked()
    {
        var (app, c, fridge, _) = await Start();
        using var _app = app;
        var beef = await Stock(c, "Ground beef", "ground-meat", fridge, 1, "kg");
        var eggs = await Stock(c, "Eggs", "eggs", fridge, 12);
        var r = await (await c.Post("/api/meals/recipes", Recipe("Meatballs", "400 g ground beef", "2 eggs", "Breadcrumbs"))).Read<RecipeDto>();
        var entry = await (await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", recipeId = r.Id })).Read<PlanEntryDto>();

        // Doubling the recipe doubles the suggested amounts.
        var plan = await (await c.GetAsync($"/api/meals/recipes/{r.Id}/cook?servings=8")).Read<CookPlanDto>();
        var beefLine = plan.Lines.Single(l => l.ProductId == beef.Product.Id);
        Assert.Equal(0.8, beefLine.SuggestedQuantity);
        Assert.Equal(4, plan.Lines.Single(l => l.ProductId == eggs.Product.Id).SuggestedQuantity);
        Assert.Empty(plan.Lines.Single(l => l.Text == "Breadcrumbs").Candidates);

        var uses = plan.Lines.Where(l => l.SuggestedItemId != null).Select(l => new { itemId = l.SuggestedItemId, quantity = l.SuggestedQuantity }).ToArray();
        (await c.Post("/api/meals/cook", new { recipeId = r.Id, planEntryId = entry.Id, uses })).EnsureSuccessStatusCode();

        Assert.Equal(0.2, (await (await c.GetAsync($"/api/inventory/items/{beef.Id}")).Read<ItemDto>()).Quantity, 3);
        Assert.Equal(8, (await (await c.GetAsync($"/api/inventory/items/{eggs.Id}")).Read<ItemDto>()).Quantity);
        var week = await (await c.GetAsync($"/api/meals/plan?from={app.Today:yyyy-MM-dd}&to={app.Today:yyyy-MM-dd}")).Read<List<PlanEntryDto>>();
        Assert.NotNull(week.Single().CookedAt);
    }

    [Fact]
    public async Task Plan_missing_goes_to_shopping_list_deduplicated()
    {
        var (app, c, fridge, _) = await Start();
        using var _app = app;
        await Stock(c, "Eggs", "eggs", fridge, 12);
        var a = await (await c.Post("/api/meals/recipes", Recipe("Omelette", "3 eggs", "50 g cheddar", "Chives"))).Read<RecipeDto>();
        var b = await (await c.Post("/api/meals/recipes", Recipe("Cheese toast", "2 slices bread", "50g cheddar"))).Read<RecipeDto>();
        await c.Post("/api/meals/plan", new { date = app.Today, slot = "breakfast", recipeId = a.Id });
        await c.Post("/api/meals/plan", new { date = app.Today.AddDays(1), slot = "lunch", recipeId = b.Id });
        await c.Post("/api/meals/plan", new { date = app.Today.AddDays(2), slot = "dinner", title = "Takeout" });

        var res = await (await c.Post("/api/meals/plan/shopping", new { from = app.Today, to = app.Today.AddDays(6) })).Read<ToListResult>();
        Assert.Equal(3, res.Added);
        var list = await (await c.GetAsync("/api/shopping/items")).Read<List<ShoppingItemDto>>();
        Assert.Equal(["bread", "cheddar", "Chives"], list.Select(i => i.Name).OrderBy(n => n));
        var cheddar = list.Single(i => i.Name == "cheddar");
        Assert.Equal("cheese", cheddar.Category);
        Assert.Contains("Omelette", cheddar.Note);
        Assert.Contains("Cheese toast", cheddar.Note);
    }

    [Fact]
    public async Task Deleting_recipe_keeps_planned_meal_as_text()
    {
        var (app, c, _, _) = await Start();
        using var _app = app;
        var r = await (await c.Post("/api/meals/recipes", Recipe("Tacos", "8 tortillas"))).Read<RecipeDto>();
        await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", recipeId = r.Id });
        (await c.DeleteAsync($"/api/meals/recipes/{r.Id}")).EnsureSuccessStatusCode();
        var entry = Assert.Single(await (await c.GetAsync($"/api/meals/plan?from={app.Today:yyyy-MM-dd}&to={app.Today:yyyy-MM-dd}")).Read<List<PlanEntryDto>>());
        Assert.Equal("Tacos", entry.Title);
        Assert.Null(entry.RecipeId);
    }

    [Fact]
    public async Task Import_rejects_private_addresses_and_bad_urls()
    {
        var (app, c, _, _) = await Start();
        using var _app = app;
        foreach (var url in new[] { "http://localhost:5080/", "http://127.0.0.1/", "http://192.168.1.1/recipe", "ftp://example.com/x", "not a url" })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/meals/import", new { url })).StatusCode);
    }
}
