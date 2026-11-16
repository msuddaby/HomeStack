using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeStack.Modules.Inventory;
using HomeStack.Modules.Meals;

namespace HomeStack.Tests;

public class MealIdeasTests
{
    private static string Reply(object content) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject { ["message"] = new JsonObject { ["content"] = JsonSerializer.Serialize(content) } }),
    }.ToJsonString();

    /// <summary>The user message sent to the (fake) AI, decoded from the request JSON.</summary>
    private static string UserPrompt(TestApp app) =>
        JsonNode.Parse(app.Ai.Requests[0].Body)!["messages"]![1]!["content"]!.GetValue<string>();

    private static async Task<(TestApp App, HttpClient C, int TacosId)> Start()
    {
        var app = new TestApp();
        var c = await app.SetupClientAsync();
        (await c.PutAsJsonAsync("/api/ai/settings", new { provider = "openAiCompatible", baseUrl = "https://api.deepseek.com", apiKey = "k", model = "deepseek-flash" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        var locs = await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        int Loc(LocationKind k) => locs.First(l => l.Kind == k).Id;
        await c.Post("/api/inventory/items", new { product = new { name = "Spinach", category = "vegetables" }, locationId = Loc(LocationKind.Fridge), expiresAt = app.Today.AddDays(1) });
        await c.Post("/api/inventory/items", new { product = new { name = "Chicken thighs", category = "poultry" }, locationId = Loc(LocationKind.Freezer), quantity = 2, unit = "lb" });
        var tacos = await (await c.Post("/api/meals/recipes", new
        {
            title = "Beef tacos", servings = 4, ingredients = new[] { "1 lb ground beef", "8 tortillas" }, steps = new[] { "Cook." }, tags = Array.Empty<string>(),
        })).Read<RecipeDto>();
        return (app, c, tacos.Id);
    }

    private static object NewIdea(string title, params string[] ingredients) => new
    {
        title, why = "Uses the spinach.", servings = 2, prepMinutes = 10, cookMinutes = 20,
        ingredients, steps = new[] { "Cook it all." }, tags = new[] { "Quick" },
    };

    [Fact]
    public async Task Ideas_mix_saved_and_new_recipes_checked_against_real_stock()
    {
        var (app, c, tacosId) = await Start();
        using var _ = app;
        app.Ai.Bodies.Enqueue(Reply(new
        {
            ideas = new object[]
            {
                NewIdea("Chicken spinach rice", "1 lb chicken thighs", "2 cups spinach", "1 cup rice"),
                new { recipeId = tacosId, title = "Tacos!", why = "Quick." },
                new { recipeId = 9999, title = "Made-up saved recipe" },        // unknown id and no ingredients: dropped
                NewIdea("chicken spinach RICE", "1 lb chicken", "spinach"),      // duplicate title: dropped
                NewIdea("Toast", "bread"),                                       // too thin: dropped
            },
        }));

        var res = await (await c.Post("/api/meals/ideas", new { count = 4, wish = "something quick" })).Read<IdeasDto>();
        Assert.Equal(2, res.Ideas.Count);

        var bowl = res.Ideas[0];
        Assert.Null(bowl.RecipeId);
        Assert.NotNull(bowl.Draft);
        Assert.Equal(["rice"], bowl.Missing);
        Assert.Equal("Spinach", Assert.Single(bowl.UsesExpiring).Name);
        Assert.Equal(30, bowl.TotalMinutes);
        Assert.Equal(["quick"], bowl.Draft!.Tags);

        var tacos = res.Ideas[1];
        Assert.Equal(tacosId, tacos.RecipeId);
        Assert.Equal("Beef tacos", tacos.Title);  // saved title wins over what the AI wrote
        Assert.Contains("ground beef", tacos.Missing);

        var prompt = UserPrompt(app);
        Assert.Contains("Spinach: 1 in Fridge, expires tomorrow", prompt);
        Assert.Contains($"#{tacosId} Beef tacos", prompt);
        Assert.Contains("Chicken thighs (2 lb)", prompt);
        Assert.Contains("<wish>something quick</wish>", prompt);
    }

    [Fact]
    public async Task A_new_idea_named_like_a_saved_recipe_is_that_recipe()
    {
        var (app, c, tacosId) = await Start();
        using var _ = app;
        app.Ai.Bodies.Enqueue(Reply(new { ideas = new[] { NewIdea("beef Tacos ", "1 lb ground beef", "8 tortillas") } }));

        var idea = Assert.Single((await (await c.Post("/api/meals/ideas", new { count = 2 })).Read<IdeasDto>()).Ideas);
        Assert.Equal(tacosId, idea.RecipeId);
        Assert.Null(idea.Draft);
    }

    [Fact]
    public async Task Week_plan_fills_free_dinners_in_order()
    {
        var (app, c, tacosId) = await Start();
        using var _ = app;
        (await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", title = "Takeout" })).EnsureSuccessStatusCode();
        (await c.Post("/api/meals/plan", new { date = app.Today.AddDays(2), slot = "dinner", title = "Out" })).EnsureSuccessStatusCode();
        app.Ai.Bodies.Enqueue(Reply(new
        {
            ideas = new object[]
            {
                NewIdea("Spinach omelette", "4 eggs", "1 cup spinach"),
                new { recipeId = tacosId },
            },
        }));

        var res = await (await c.Post("/api/meals/ideas", new { week = true })).Read<IdeasDto>();
        Assert.Contains("Plan 5 dinners", UserPrompt(app));
        Assert.Equal([app.Today.AddDays(1), app.Today.AddDays(3)], res.Ideas.Select(i => i.Date!.Value));
    }

    [Fact]
    public async Task Accepting_saves_drafts_and_plans_them()
    {
        var (app, c, tacosId) = await Start();
        using var _ = app;
        var draft = new RecipeRequest("Spinach omelette", null, 2, 5, 10, null, null, ["4 eggs", "1 cup spinach"], ["Whisk.", "Cook."], ["quick"], null);

        var a = await (await c.Post("/api/meals/ideas/accept", new { draft, date = app.Today.AddDays(1) })).Read<AcceptIdeaResult>();
        Assert.NotNull(a.PlanEntryId);
        var recipe = await (await c.GetAsync($"/api/meals/recipes/{a.RecipeId}")).Read<RecipeDto>();
        Assert.Equal(["quick", "idea"], recipe.Tags);
        var plan = await (await c.GetAsync($"/api/meals/plan?from={app.Today:yyyy-MM-dd}&to={app.Today.AddDays(6):yyyy-MM-dd}")).Read<List<PlanEntryDto>>();
        Assert.Equal("Spinach omelette", Assert.Single(plan).Title);

        var saved = await (await c.Post("/api/meals/ideas/accept", new { recipeId = tacosId })).Read<AcceptIdeaResult>();
        Assert.Equal(tacosId, saved.RecipeId);
        Assert.Null(saved.PlanEntryId);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/meals/ideas/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/meals/ideas/accept", new { draft = draft with { Title = "" } })).StatusCode);
    }

    [Fact]
    public async Task Full_week_is_a_clear_error()
    {
        var (app, c, _) = await Start();
        using var __ = app;
        for (var d = 0; d < 7; d++)
            (await c.Post("/api/meals/plan", new { date = app.Today.AddDays(d), slot = "dinner", title = "Planned" })).EnsureSuccessStatusCode();
        var res = await c.Post("/api/meals/ideas", new { week = true });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("already planned", await res.Content.ReadAsStringAsync());
        Assert.Empty(app.Ai.Requests);
    }
}
