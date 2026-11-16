using System.Net;
using HomeStack.Modules.Inventory;

namespace HomeStack.Tests;

public class InventoryTests
{
    private static async Task<(TestApp App, HttpClient Client, List<LocationDto> Locations)> Start()
    {
        var app = new TestApp();
        var client = await app.SetupClientAsync();
        var locations = await (await client.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        return (app, client, locations);
    }

    private static int Loc(List<LocationDto> ls, LocationKind k) => ls.First(l => l.Kind == k).Id;

    private static object NewProduct(string name, string category, double? min = null) =>
        new { name, category, minQuantity = min };

    [Fact]
    public async Task Household_gets_default_locations()
    {
        var (app, _, locations) = await Start();
        using var _app = app;
        Assert.Equal([LocationKind.Pantry, LocationKind.Fridge, LocationKind.Freezer], locations.Select(l => l.Kind));
    }

    [Fact]
    public async Task Adding_item_suggests_expiry_from_category_and_reuses_product()
    {
        var (app, client, locations) = await Start();
        using var _app = app;

        var milk = await (await client.Post("/api/inventory/items",
            new { product = NewProduct("2% Milk", "dairy"), locationId = Loc(locations, LocationKind.Fridge), quantity = 1 })).Read<ItemDto>();
        Assert.Equal(app.Today.AddDays(7), milk.ExpiresAt);
        Assert.Equal(app.Today, milk.PurchasedAt);
        Assert.Null(milk.FrozenAt);

        // Straight into the freezer: frozen from purchase, freezer shelf life.
        var steak = await (await client.Post("/api/inventory/items",
            new { product = NewProduct("Ribeye", "meat"), locationId = Loc(locations, LocationKind.Freezer) })).Read<ItemDto>();
        Assert.Equal(app.Today, steak.FrozenAt);
        Assert.Equal(app.Today.AddDays(180), steak.ExpiresAt);

        // Explicit expiry wins; existing product is reused.
        var milk2 = await (await client.Post("/api/inventory/items",
            new { productId = milk.Product.Id, locationId = Loc(locations, LocationKind.Fridge), expiresAt = app.Today.AddDays(20) })).Read<ItemDto>();
        Assert.Equal(app.Today.AddDays(20), milk2.ExpiresAt);

        var products = await (await client.GetAsync("/api/inventory/products?q=milk")).Read<List<ProductDto>>();
        Assert.Single(products);
    }

    [Fact]
    public async Task Freezing_and_thawing_adjust_dates()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var chicken = await (await client.Post("/api/inventory/items",
            new { product = NewProduct("Chicken thighs", "poultry"), locationId = Loc(locations, LocationKind.Fridge) })).Read<ItemDto>();

        app.Time.Now = app.Time.Now.AddDays(1);
        var frozen = (await (await client.Post($"/api/inventory/items/{chicken.Id}/move", new { locationId = Loc(locations, LocationKind.Freezer) })).Read<ItemActionDto>()).Item!;
        Assert.Equal(app.Today, frozen.FrozenAt);
        Assert.Equal(app.Today.AddDays(270), frozen.ExpiresAt);

        app.Time.Now = app.Time.Now.AddDays(30);
        var thawed = (await (await client.Post($"/api/inventory/items/{chicken.Id}/move", new { locationId = Loc(locations, LocationKind.Fridge) })).Read<ItemActionDto>()).Item!;
        Assert.Null(thawed.FrozenAt);
        Assert.Equal(app.Today.AddDays(2), thawed.ExpiresAt);
    }

    [Fact]
    public async Task Consume_partially_then_fully()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var eggs = await (await client.Post("/api/inventory/items",
            new { product = NewProduct("Eggs", "eggs"), locationId = Loc(locations, LocationKind.Fridge), quantity = 12, unit = "pc" })).Read<ItemDto>();

        var left = await (await client.Post($"/api/inventory/items/{eggs.Id}/consume", new { quantity = 4 })).Read<ItemActionDto>();
        Assert.Equal(8, left.Item!.Quantity);

        var gone = await (await client.Post($"/api/inventory/items/{eggs.Id}/consume", new { wasted = true })).Read<ItemActionDto>();
        Assert.Null(gone.Item);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/inventory/items/{eggs.Id}")).StatusCode);
    }

    [Fact]
    public async Task Dashboard_flags_expired_expiring_freeze_soon_long_frozen_and_low_stock()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var fridge = Loc(locations, LocationKind.Fridge);
        var freezer = Loc(locations, LocationKind.Freezer);
        var pantry = Loc(locations, LocationKind.Pantry);
        var t = app.Today;

        await client.Post("/api/inventory/items", new { product = NewProduct("Old yogurt", "dairy"), locationId = fridge, expiresAt = t.AddDays(-1) });
        await client.Post("/api/inventory/items", new { product = NewProduct("Ketchup", "condiments"), locationId = fridge, expiresAt = t.AddDays(2) });
        await client.Post("/api/inventory/items", new { product = NewProduct("Ground beef", "ground-meat"), locationId = fridge, expiresAt = t.AddDays(1) });
        await client.Post("/api/inventory/items", new { product = NewProduct("Frozen peas", "vegetables"), locationId = freezer, frozenAt = t.AddDays(-220) });
        await client.Post("/api/inventory/items", new { product = NewProduct("Fresh ice cream", "frozen"), locationId = freezer });
        await client.Post("/api/inventory/items", new { product = NewProduct("Rice", "dry-goods", min: 2), locationId = pantry, quantity = 1 });

        var d = await (await client.GetAsync("/api/inventory/dashboard")).Read<InventoryDashboardDto>();
        Assert.Equal(["Old yogurt"], d.Expired.Select(i => i.Product.Name));
        Assert.Equal(["Ground beef"], d.FreezeSoon.Select(i => i.Product.Name));
        Assert.Equal(["Ketchup"], d.ExpiringSoon.Select(i => i.Product.Name)); // ketchup isn't freezable
        Assert.Equal(["Frozen peas"], d.LongInFreezer.Select(i => i.Item.Product.Name));
        Assert.Equal(220, d.LongInFreezer[0].DaysFrozen);
        Assert.Equal(["Rice"], d.LowStock.Select(x => x.Product.Name));
        Assert.Equal(3, d.Counts.Single(c => c.Kind == LocationKind.Fridge).Items);
    }

    [Fact]
    public async Task Barcode_lookup_finds_local_product_and_rejects_duplicates()
    {
        var (app, client, _) = await Start();
        using var _app = app;
        (await client.Post("/api/inventory/products", new { name = "Oat milk", category = "beverages", barcode = "0001112223334" })).EnsureSuccessStatusCode();

        var hit = await (await client.GetAsync("/api/inventory/barcode/0001112223334")).Read<BarcodeLookupDto>();
        Assert.Equal("local", hit.Source);
        Assert.Equal("Oat milk", hit.Product!.Name);

        var dup = await client.Post("/api/inventory/products", new { name = "Other", category = "beverages", barcode = "0001112223334" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
    }

    [Fact]
    public async Task Households_cannot_see_each_others_items()
    {
        var (app, client, locations) = await Start();
        using var _app = app;
        var item = await (await client.Post("/api/inventory/items",
            new { product = NewProduct("Secret cheese", "cheese"), locationId = Loc(locations, LocationKind.Fridge) })).Read<ItemDto>();

        // No public way to create a second household (single-household install), so add one directly.
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HomeStack.Core.HomeStackDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.IPasswordHasher<HomeStack.Core.Data.User>>();
            var h = new HomeStack.Core.Data.Household { Name = "Neighbours" };
            db.Households.Add(h);
            await db.SaveChangesAsync();
            var u = new HomeStack.Core.Data.User { HouseholdId = h.Id, Name = "N", Username = "neighbour" };
            u.PasswordHash = hasher.HashPassword(u, "password123");
            db.Users.Add(u);
            await db.SaveChangesAsync();
        }
        var other = app.CreateClient();
        (await other.Post("/api/auth/login", new { username = "neighbour", password = "password123" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/inventory/items/{item.Id}")).StatusCode);
        Assert.Empty(await (await other.GetAsync("/api/inventory/items")).Read<List<ItemDto>>());
    }
}

public class InventoryValidationTests
{
    [Fact]
    public async Task Rejects_unknown_category_and_missing_product()
    {
        using var app = new TestApp();
        var client = await app.SetupClientAsync();
        var res = await client.Post("/api/inventory/items", new { product = new { name = "X", category = "nope" }, locationId = 1 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("product.category", await res.Content.ReadAsStringAsync());

        res = await client.Post("/api/inventory/items", new { locationId = 1 });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, res.StatusCode);
    }
}
