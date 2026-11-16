using System.Net;
using System.Net.Http.Json;
using HomeStack.Modules.Inventory;

namespace HomeStack.Tests;

public class UndoAndWasteTests
{
    private static async Task<(TestApp App, HttpClient Client, int Fridge, int Freezer)> Start()
    {
        var app = new TestApp();
        var client = await app.SetupClientAsync();
        var locations = await (await client.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        return (app, client, locations.First(l => l.Kind == LocationKind.Fridge).Id, locations.First(l => l.Kind == LocationKind.Freezer).Id);
    }

    private static async Task<ItemDto> Add(HttpClient c, string name, string category, int location, double quantity = 1, decimal? price = null) =>
        await (await c.Post("/api/inventory/items", new { product = new { name, category }, locationId = location, quantity, price })).Read<ItemDto>();

    private static async Task<ItemActionDto> Consume(HttpClient c, int id, double? quantity = null, bool wasted = false) =>
        await (await c.Post($"/api/inventory/items/{id}/consume", new { quantity, wasted })).Read<ItemActionDto>();

    private static Task<HttpResponseMessage> Undo(HttpClient c, int eventId) => c.Post($"/api/inventory/events/{eventId}/undo", new { });

    private static async Task<WasteStatsDto> Stats(HttpClient c, int months = 3) =>
        await (await c.GetAsync($"/api/inventory/stats/waste?months={months}")).Read<WasteStatsDto>();

    [Fact]
    public async Task Price_is_shared_per_unit_and_follows_partial_waste()
    {
        var (app, client, fridge, _) = await Start();
        using var _app = app;
        var eggs = await Add(client, "Eggs", "eggs", fridge, 12, 6.00m);
        Assert.Equal(6.00m, eggs.Price);

        var half = await Consume(client, eggs.Id, 6, wasted: true);
        Assert.Equal(3.00m, half.Item!.Price);

        var month = (await Stats(client)).Months[^1];
        Assert.Equal(1, month.Wasted);
        Assert.Equal(3.00m, month.WastedValue);

        // Editing sets the value of what's left.
        var edited = await (await client.PutAsJsonAsync($"/api/inventory/items/{eggs.Id}", new
        {
            locationId = fridge, quantity = 6, unit = (string?)null, purchasedAt = eggs.PurchasedAt, expiresAt = eggs.ExpiresAt,
            openedAt = (DateOnly?)null, frozenAt = (DateOnly?)null, notes = (string?)null, price = 4.50m,
        }, TestApp.Json)).Read<ItemDto>();
        Assert.Equal(4.50m, edited.Price);
    }

    [Fact]
    public async Task Undo_partial_use_restores_quantity_and_removes_the_event()
    {
        var (app, client, fridge, _) = await Start();
        using var _app = app;
        var eggs = await Add(client, "Eggs", "eggs", fridge, 12);

        var used = await Consume(client, eggs.Id, 4);
        var restored = await (await Undo(client, used.EventId)).Read<ItemDto>();
        Assert.Equal(12, restored.Quantity);
        Assert.Equal(0, (await Stats(client)).Months[^1].Used);

        // The event is gone, so a second undo finds nothing.
        Assert.Equal(HttpStatusCode.NotFound, (await Undo(client, used.EventId)).StatusCode);
    }

    [Fact]
    public async Task Undo_throw_out_brings_the_item_back_with_its_id_and_price()
    {
        var (app, client, fridge, _) = await Start();
        using var _app = app;
        var milk = await Add(client, "Milk", "dairy", fridge, 1, 4.99m);

        var gone = await Consume(client, milk.Id, wasted: true);
        Assert.Null(gone.Item);
        Assert.Equal(1, (await Stats(client)).Months[^1].Wasted);

        var back = await (await Undo(client, gone.EventId)).Read<ItemDto>();
        Assert.Equal(milk.Id, back.Id);
        Assert.Equal(4.99m, back.Price);
        Assert.Equal(milk.ExpiresAt, back.ExpiresAt);
        (await client.GetAsync($"/api/inventory/items/{milk.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(0, (await Stats(client)).Months[^1].Wasted);
    }

    [Fact]
    public async Task Undo_freeze_restores_place_and_dates()
    {
        var (app, client, fridge, freezer) = await Start();
        using var _app = app;
        var chicken = await Add(client, "Chicken thighs", "poultry", fridge);

        var frozen = await (await client.Post($"/api/inventory/items/{chicken.Id}/move", new { locationId = freezer })).Read<ItemActionDto>();
        Assert.NotNull(frozen.Item!.FrozenAt);

        var back = await (await Undo(client, frozen.EventId)).Read<ItemDto>();
        Assert.Equal(fridge, back.Location.Id);
        Assert.Null(back.FrozenAt);
        Assert.Equal(chicken.ExpiresAt, back.ExpiresAt);
    }

    [Fact]
    public async Task Undo_is_refused_after_a_later_action_or_an_hour()
    {
        var (app, client, fridge, _) = await Start();
        using var _app = app;
        var eggs = await Add(client, "Eggs", "eggs", fridge, 12);

        var first = await Consume(client, eggs.Id, 1);
        await Consume(client, eggs.Id, 1);
        Assert.Equal(HttpStatusCode.Conflict, (await Undo(client, first.EventId)).StatusCode);

        var bread = await Add(client, "Bread", "bakery", fridge);
        var late = await Consume(client, bread.Id, wasted: true);
        app.Time.Now = app.Time.Now.AddMinutes(61);
        Assert.Equal(HttpStatusCode.Conflict, (await Undo(client, late.EventId)).StatusCode);
    }

    [Fact]
    public async Task Stats_group_by_month_rank_products_and_leave_money_out_when_unknown()
    {
        var (app, client, fridge, _) = await Start();
        using var _app = app;
        var earlier = app.Time.Now;

        // Earlier month: two lettuces and a yogurt thrown out, nothing priced.
        var lettuce = await Add(client, "Lettuce", "vegetables", fridge);
        async Task<ItemDto> MoreLettuce(decimal? price = null) =>
            await (await client.Post("/api/inventory/items", new { productId = lettuce.Product.Id, locationId = fridge, price })).Read<ItemDto>();
        await Consume(client, lettuce.Id, wasted: true);
        await Consume(client, (await MoreLettuce()).Id, wasted: true);
        await Consume(client, (await Add(client, "Yogurt", "dairy", fridge)).Id, wasted: true);

        // About five weeks later: one priced throw-out, one use.
        app.Time.Now = app.Time.Now.AddDays(35);
        await Consume(client, (await MoreLettuce(2.50m)).Id, wasted: true);
        await Consume(client, (await Add(client, "Cheese", "dairy", fridge)).Id);

        var stats = await Stats(client, 3);
        Assert.Equal(3, stats.Months.Count);
        Assert.Equal(app.Time.Now.ToString("yyyy-MM"), stats.Months[^1].Month);
        Assert.True(string.CompareOrdinal(stats.Months[0].Month, stats.Months[1].Month) < 0);

        var then = stats.Months.Single(m => m.Month == earlier.ToString("yyyy-MM"));
        Assert.Equal(3, then.Wasted);
        Assert.Null(then.WastedValue);

        var now = stats.Months[^1];
        Assert.Equal((1, 1, 2.50m), (now.Wasted, now.Used, now.WastedValue));
        Assert.Null(now.UsedValue);
        // Months with nothing show as zeros.
        Assert.All(stats.Months.Where(m => m != then && m != now), m => Assert.Equal(0, m.Wasted + m.Used));

        var top = stats.TopWasted[0];
        Assert.Equal(("Lettuce", 3, 2.50m), (top.Product.Name, top.Times, top.Value));
        Assert.Equal("Yogurt", stats.TopWasted[1].Product.Name);
    }
}
