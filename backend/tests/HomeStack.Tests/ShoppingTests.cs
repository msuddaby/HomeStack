using System.Net;
using HomeStack.Modules.Inventory;
using HomeStack.Modules.Shopping;

namespace HomeStack.Tests;

public class ShoppingTests
{
    private static async Task<(TestApp App, HttpClient Client, List<LocationDto> Locations)> Start()
    {
        var app = new TestApp();
        var client = await app.SetupClientAsync();
        var locations = await (await client.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        return (app, client, locations);
    }

    private static async Task<List<ShoppingItemDto>> List(HttpClient c) =>
        await (await c.GetAsync("/api/shopping/items")).Read<List<ShoppingItemDto>>();

    [Fact]
    public async Task Adding_merges_duplicates_and_links_known_products()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        await client.Post("/api/inventory/items", new { product = new { name = "Milk", category = "dairy" }, locationId = locations[1].Id });

        var a = await (await client.Post("/api/shopping/items", new { name = "milk", quantity = 1 })).Read<ShoppingItemDto>();
        Assert.NotNull(a.ProductId);
        Assert.Equal("Milk", a.Name);
        Assert.Equal("dairy", a.Category);

        var b = await (await client.Post("/api/shopping/items", new { name = "Milk", quantity = 2 })).Read<ShoppingItemDto>();
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(3, b.Quantity);

        await client.Post("/api/shopping/items", new { name = "Paper towels" });
        Assert.Equal(2, (await List(client)).Count);
    }

    [Fact]
    public async Task Low_stock_products_are_auto_added_dismissable_and_removed_when_restocked()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var pantry = locations.First(l => l.Kind == LocationKind.Pantry).Id;
        var rice = await (await client.Post("/api/inventory/products", new { name = "Rice", category = "dry-goods", minQuantity = 2, defaultUnit = "bag" })).Read<ProductDto>();

        var list = await List(client);
        var auto = Assert.Single(list);
        Assert.Equal(ShoppingSource.LowStock, auto.Source);
        Assert.Equal(2, auto.Quantity);
        Assert.Equal("bag", auto.Unit);

        // Dismissed: stays hidden instead of being re-added.
        (await client.DeleteAsync($"/api/shopping/items/{auto.Id}")).EnsureSuccessStatusCode();
        Assert.Empty(await List(client));
        Assert.Empty(await List(client));

        // Restocked: the hidden row goes away; running low again later re-adds it.
        var bag = await (await client.Post("/api/inventory/items", new { productId = rice.Id, locationId = pantry, quantity = 2 })).Read<ItemDto>();
        Assert.Empty(await List(client));
        await client.Post($"/api/inventory/items/{bag.Id}/consume", new { quantity = 1.5 });
        var again = Assert.Single(await List(client));
        Assert.Equal(1.5, again.Quantity);
    }

    [Fact]
    public async Task Put_away_adds_checked_items_to_inventory_and_clears_list()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var fridge = locations.First(l => l.Kind == LocationKind.Fridge).Id;
        var freezer = locations.First(l => l.Kind == LocationKind.Freezer).Id;

        var yogurt = await (await client.Post("/api/shopping/items", new { name = "Greek yogurt", category = "dairy", quantity = 2, unit = "tub" })).Read<ShoppingItemDto>();
        var chicken = await (await client.Post("/api/shopping/items", new { name = "Chicken breast", category = "poultry" })).Read<ShoppingItemDto>();
        var towels = await (await client.Post("/api/shopping/items", new { name = "Paper towels" })).Read<ShoppingItemDto>();
        await client.Post("/api/shopping/items", new { name = "Not bought yet", category = "fruit" });
        foreach (var id in new[] { yogurt.Id, chicken.Id, towels.Id })
            (await client.Post($"/api/shopping/items/{id}/check", new { @checked = true })).EnsureSuccessStatusCode();

        var res = await (await client.Post("/api/shopping/put-away", new
        {
            lines = new object[]
            {
                new { shoppingItemId = yogurt.Id, addToInventory = true, locationId = fridge },
                new { shoppingItemId = chicken.Id, addToInventory = true, locationId = freezer },
                new { shoppingItemId = towels.Id, addToInventory = false },
            },
        })).Read<PutAwayResult>();

        Assert.Equal(2, res.AddedToInventory);
        Assert.Equal(1, res.Cleared);
        var y = res.Items.Single(i => i.Product.Name == "Greek yogurt");
        Assert.Equal(2, y.Quantity);
        Assert.Equal("tub", y.Unit);
        Assert.Equal(app.Today.AddDays(7), y.ExpiresAt);
        var c = res.Items.Single(i => i.Product.Name == "Chicken breast");
        Assert.Equal(app.Today, c.FrozenAt);

        Assert.Equal(["Not bought yet"], (await List(client)).Select(i => i.Name));
        // Products were created, so next time "chicken breast" links to it.
        var linked = await (await client.Post("/api/shopping/items", new { name = "chicken breast" })).Read<ShoppingItemDto>();
        Assert.Equal(c.Product.Id, linked.ProductId);
    }

    [Fact]
    public async Task Put_away_requires_location_and_rolls_back_on_error()
    {
        var (app, client, _) = await Start();
        using var _app = app;
        var a = await (await client.Post("/api/shopping/items", new { name = "Eggs", category = "eggs" })).Read<ShoppingItemDto>();

        var bad = await client.Post("/api/shopping/put-away", new { lines = new[] { new { shoppingItemId = a.Id, addToInventory = true } } });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var unknownLoc = await client.Post("/api/shopping/put-away", new { lines = new[] { new { shoppingItemId = a.Id, addToInventory = true, locationId = 999 } } });
        Assert.Equal(HttpStatusCode.BadRequest, unknownLoc.StatusCode);
        Assert.Single(await List(client));
    }

    [Fact]
    public async Task Check_records_who_and_clear_checked_removes_only_checked()
    {
        var (app, client, _) = await Start();
        using var _app = app;
        var a = await (await client.Post("/api/shopping/items", new { name = "Bananas", category = "fruit" })).Read<ShoppingItemDto>();
        await client.Post("/api/shopping/items", new { name = "Apples", category = "fruit" });

        var checkedItem = await (await client.Post($"/api/shopping/items/{a.Id}/check", new { @checked = true })).Read<ShoppingItemDto>();
        Assert.NotNull(checkedItem.CheckedAt);
        Assert.Equal("Tester", checkedItem.CheckedBy);

        await client.Post("/api/shopping/clear-checked", new { });
        Assert.Equal(["Apples"], (await List(client)).Select(i => i.Name));
    }
}
