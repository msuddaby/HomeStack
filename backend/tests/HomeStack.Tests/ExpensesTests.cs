using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeStack.Modules.Expenses;
using HomeStack.Modules.Inventory;
using SkiaSharp;

namespace HomeStack.Tests;

public class ExpensesTests
{
    private static string OpenAiReply(object content) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = JsonSerializer.Serialize(content) },
            ["finish_reason"] = "stop",
        }),
    }.ToJsonString();

    private static async Task<HttpClient> StartWithAi(TestApp app)
    {
        var c = await app.SetupClientAsync();
        (await c.PutAsJsonAsync("/api/ai/settings",
            new { provider = "openAiCompatible", baseUrl = "https://api.deepseek.com", apiKey = "k", model = "deepseek-flash" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        return c;
    }

    private static MultipartFormDataContent Photo()
    {
        using var bitmap = new SKBitmap(40, 60);
        bitmap.Erase(SKColors.White);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Jpeg, 90);
        var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { file, "images", "receipt.jpg" } };
    }

    private static object Line(string name, decimal amount, string category = "household", string? code = null, int? itemId = null,
        double quantity = 1, object? inventory = null) =>
        new { itemId, code, name, category, quantity, amount, unitPrice = amount, inventory };

    private static object Receipt(string? store, DateOnly date, decimal total, params object[] lines) =>
        new { store, purchasedAt = date, total, lines };

    [Fact]
    public async Task Reading_a_receipt_keeps_every_line_with_codes_discounts_and_food()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        app.Ai.Bodies.Enqueue(OpenAiReply(new
        {
            store = "Costco", date = app.Today.AddDays(-1).ToString("yyyy-MM-dd"), subtotal = 33.97, tax = 1.20, total = 35.17,
            lines = new object[]
            {
                new { code = "123 4567", raw = "1234567 KS DETRGNT", name = "Kirkland laundry detergent 4.5 L", quantity = 1, unitPrice = 18.99,
                      amount = 15.99, discount = 3.00, category = "household", food = false },
                new { code = "998877", raw = "998877 EGGS 24CT", name = "Eggs", quantity = 1, amount = 7.99, category = "groceries", food = true,
                      foodCategory = "eggs", location = "fridge" },
                new { code = (string?)null, raw = "BOTTLE DEP", name = "Bottle deposit", amount = 0.10, category = "not-real", food = false },
                new { code = "1", raw = "??", name = " ", amount = 1.00 },
                new { code = "555", raw = "555 SOCKS", name = "Socks", quantity = 2, unitPrice = 4.95, category = "clothing", sameAs = 999 },
            },
        }));

        var draft = await (await c.PostAsync("/api/expenses/read", Photo())).Read<ReceiptDraftDto>();

        Assert.Equal("Costco", draft.Store);
        Assert.Equal(app.Today.AddDays(-1), draft.PurchasedAt);
        Assert.Equal(35.17m, draft.Total);
        Assert.Equal(1.20m, draft.Tax);
        Assert.Equal(4, draft.Lines.Count); // the nameless line is dropped

        var detergent = draft.Lines[0];
        Assert.Equal("1234567", detergent.Code); // spaces stripped
        Assert.Equal(15.99m, detergent.Amount);
        Assert.Equal(3.00m, detergent.Discount);
        Assert.Null(detergent.Food);
        Assert.Null(detergent.Item); // never bought before

        var eggs = draft.Lines[1];
        Assert.Equal("eggs", eggs.Food!.Category);
        Assert.Equal(LocationKind.Fridge, eggs.Food.Location);

        Assert.Equal("other", draft.Lines[2].Category); // unknown category
        var socks = draft.Lines[3];
        Assert.Equal(9.90m, socks.Amount); // worked out from 2 × 4.95
        Assert.Null(socks.Suggestion); // made-up item id is dropped

        // Everything is offered to the model as a receipt reader, not a grocery list.
        var prompt = app.Ai.Requests[0].Body;
        Assert.Contains("household", prompt);
        Assert.Contains("image_url", prompt);
    }

    [Fact]
    public async Task Saving_records_items_puts_food_away_and_links_the_same_item_next_time()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        var fridge = (await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).First(l => l.Kind == LocationKind.Fridge).Id;
        var day1 = app.Today.AddDays(-30);

        var first = await (await c.Post("/api/expenses/receipts", Receipt("Costco", day1, 23.98m,
            Line("Kirkland detergent", 15.99m, code: "1234567"),
            Line("Eggs", 7.99m, "groceries", code: "998877",
                inventory: new { product = new { name = "Eggs", category = "eggs" }, locationId = fridge, quantity = 24 })))).Read<ReceiptDto>();

        Assert.Equal("Costco", first.Store);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(1, first.AddedToInventory);
        var eggsInFridge = (await (await c.GetAsync("/api/inventory/items")).Read<List<ItemDto>>()).Single();
        Assert.Equal(day1, eggsInFridge.PurchasedAt);
        Assert.Equal(7.99m, eggsInFridge.Price); // the line amount becomes the inventory price
        Assert.Contains("Costco", eggsInFridge.Notes);

        // Next scan at the same store: the item number links it to the same item.
        app.Ai.Bodies.Enqueue(OpenAiReply(new
        {
            store = "costco", date = app.Today.ToString("yyyy-MM-dd"), total = 14.99,
            lines = new object[] { new { code = "1234567", name = "KS laundry soap", amount = 14.99, category = "household" } },
        }));
        var draft = await (await c.PostAsync("/api/expenses/read", Photo())).Read<ReceiptDraftDto>();
        Assert.Equal("Costco", draft.Store); // matched to the saved store's spelling
        var detergentId = first.Lines[0].ItemId!.Value;
        Assert.Equal(detergentId, draft.Lines[0].Item?.Id);
        Assert.Equal(15.99m, draft.Lines[0].Item?.LastUnitPrice);
        Assert.Equal("Costco", draft.Lines[0].Item?.LastStore);
        Assert.Contains($"{detergentId}: Kirkland detergent (Costco)", app.Ai.Requests[^1].Body); // offered for cross-store matching

        // Saved without an item id, the code still links it.
        var second = await (await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today, 14.99m,
            Line("KS laundry soap", 14.99m, code: "1234567")))).Read<ReceiptDto>();
        Assert.Equal(detergentId, second.Lines[0].ItemId);

        // At Walmart the user confirms "same as the Costco one".
        await c.Post("/api/expenses/receipts", Receipt("Walmart", app.Today, 19.47m,
            Line("Kirkland detergent", 19.47m, code: "068113100123", itemId: detergentId)));

        var item = await (await c.GetAsync($"/api/expenses/items/{detergentId}")).Read<TrackedItemDetailDto>();
        Assert.Equal(3, item.Purchases.Count);
        Assert.Equal(["Costco", "Walmart"], item.Stores.Select(s => s.Store)); // cheapest first
        Assert.Equal(14.99m, item.Stores[0].LastUnitPrice);
        Assert.Equal(14.99m, item.Stores[0].LowestUnitPrice);
        Assert.Equal(50.45m, item.TotalSpent);
        Assert.Equal(["1234567", "068113100123"], item.Item.Codes.Order().Reverse());
        Assert.Equal(eggsInFridge.Product.Id, (await (await c.GetAsync("/api/expenses/items")).Read<List<TrackedItemDto>>())
            .Single(i => i.Name == "Eggs").ProductId);
    }

    [Fact]
    public async Task Leaving_food_out_of_inventory_is_remembered_for_next_time()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        var pantry = (await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).First(l => l.Kind == LocationKind.Pantry).Id;
        object Coke(bool track) => new
        {
            code = "4040", name = "Diet Coke 35 pk", category = "snacks", quantity = 1, amount = 17.99m, trackInventory = track,
            inventory = track ? new { product = new { name = "Diet Coke", category = "beverages" }, locationId = pantry } : null,
        };
        async Task<DraftLineDto> ScanCoke()
        {
            app.Ai.Bodies.Enqueue(OpenAiReply(new
            {
                store = "Costco", date = app.Today.ToString("yyyy-MM-dd"), total = 17.99,
                lines = new object[] { new { code = "4040", name = "Diet Coke 35 pk", amount = 17.99, category = "snacks", food = true, foodCategory = "beverages", location = "pantry" } },
            }));
            return (await (await c.PostAsync("/api/expenses/read", Photo())).Read<ReceiptDraftDto>()).Lines.Single();
        }

        // First time: offered for inventory, and the user unticks it. Spending is still recorded.
        Assert.True((await ScanCoke()).Food!.Add);
        Assert.Contains("- snacks: Snacks", app.Ai.Requests[^1].Body); // the AI is offered the new category
        var saved = await (await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today.AddDays(-20), 17.99m, Coke(track: false)))).Read<ReceiptDto>();
        Assert.Equal(0, saved.AddedToInventory);
        Assert.Empty(await (await c.GetAsync("/api/inventory/items")).Read<List<ItemDto>>());

        // Next time it starts unticked.
        var again = await ScanCoke();
        Assert.False(again.Food!.Add);
        Assert.True(again.Item!.SkipInventory);

        // Ticking it again turns it back on.
        await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today, 17.99m, Coke(track: true)));
        Assert.True((await ScanCoke()).Food!.Add);

        // And it can be set on the item directly.
        var id = saved.Lines[0].ItemId!.Value;
        var updated = await (await c.PutAsJsonAsync($"/api/expenses/items/{id}",
            new { name = "Diet Coke 35 pk", category = "snacks", skipInventory = true }, TestApp.Json)).Read<TrackedItemDetailDto>();
        Assert.True(updated.Item.SkipInventory);
        Assert.False((await ScanCoke()).Food!.Add);

        Assert.Contains(await (await c.GetAsync("/api/expenses/categories")).Read<List<ExpenseCategory>>(), x => x.Id == "snacks");
    }

    [Fact]
    public async Task Duplicate_scan_is_flagged()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        await c.Post("/api/expenses/receipts", Receipt("Walmart", app.Today, 12.34m, Line("Paper towel", 12.34m)));
        app.Ai.Bodies.Enqueue(OpenAiReply(new
        {
            store = "Walmart", date = app.Today.ToString("yyyy-MM-dd"), total = 12.34,
            lines = new object[] { new { name = "Paper towel", amount = 12.34, category = "household" } },
        }));
        var draft = await (await c.PostAsync("/api/expenses/read", Photo())).Read<ReceiptDraftDto>();
        Assert.NotNull(draft.DuplicateOf);
        Assert.NotNull(draft.Lines[0].Item); // no code: linked by the same name
    }

    [Fact]
    public void Rhythm_uses_the_typical_gap_and_needs_three_purchases_to_be_due()
    {
        var today = new DateOnly(2026, 6, 1);
        DateOnly Ago(int d) => today.AddDays(-d);

        var two = ExpensesService.Rhythm([Ago(50), Ago(25)], today);
        Assert.Equal(25, two.EveryDays);
        Assert.False(two.Due); // one gap isn't a pattern yet

        var three = ExpensesService.Rhythm([Ago(70), Ago(46), Ago(22), Ago(22)], today);
        Assert.Equal(3, three.Purchases); // same day counts once
        Assert.Equal(24, three.EveryDays);
        Assert.Equal(22, three.DaysSinceLast);
        Assert.True(three.Due); // 22 ≥ 85% of 24

        Assert.False(ExpensesService.Rhythm([Ago(70), Ago(60), Ago(50)], today).Due); // 50 days > 3 × 10: probably stopped buying it
        Assert.False(ExpensesService.Rhythm([Ago(40), Ago(20), Ago(2)], today).Due); // bought 2 days ago, every ~19: not yet
    }

    [Fact]
    public async Task Things_bought_regularly_show_up_when_due()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        foreach (var ago in new[] { 66, 44, 22 })
            await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today.AddDays(-ago), 18.99m, Line("Detergent", 18.99m, code: "1")));
        await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today, 5m, Line("Batteries", 5m, code: "2")));

        var due = await (await c.GetAsync("/api/expenses/items/due")).Read<List<TrackedItemDto>>();
        var detergent = Assert.Single(due);
        Assert.Equal("Detergent", detergent.Name);
        Assert.Equal(22, detergent.Rhythm!.EveryDays);
    }

    [Fact]
    public async Task Summary_breaks_the_month_down_by_category_store_and_tax()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        var thisMonth = new DateOnly(app.Today.Year, app.Today.Month, 1);
        await c.Post("/api/expenses/receipts", new
        {
            store = "Costco", purchasedAt = thisMonth, subtotal = 30m, tax = 1.5m, total = 40m,
            lines = new[] { Line("Detergent", 20m), Line("Eggs", 10m, "groceries") },
        });
        await c.Post("/api/expenses/receipts/manual", new { purchasedAt = thisMonth, amount = 25m, category = "dining", description = "Pizza" });
        await c.Post("/api/expenses/receipts", Receipt("Walmart", thisMonth.AddMonths(-1), 50m, Line("Shoes", 50m, "clothing")));

        var s = await (await c.GetAsync("/api/expenses/summary?months=3")).Read<SummaryDto>();
        Assert.Equal(65m, s.Total);
        Assert.Equal(50m, s.PreviousTotal);
        var cats = s.ByCategory.ToDictionary(b => b.Key, b => b.Total);
        Assert.Equal(25m, cats["dining"]);
        Assert.Equal(20m, cats["household"]);
        Assert.Equal(10m, cats["groceries"]);
        Assert.Equal(1.5m, cats["tax"]);
        Assert.Equal(8.5m, cats["unitemised"]); // 40 total − 30 lines − 1.50 tax
        Assert.Equal(["Costco", "No store"], s.ByStore.Select(b => b.Label));
        Assert.Equal([50m, 65m], s.Months.TakeLast(2).Select(m => m.Total));

        // Manual expenses aren't tracked items.
        Assert.DoesNotContain(await (await c.GetAsync("/api/expenses/items")).Read<List<TrackedItemDto>>(), i => i.Name == "Pizza");
    }

    [Fact]
    public async Task Photos_attach_to_a_receipt_and_deleting_it_cleans_up()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        var r = await (await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today, 9m, Line("Lightbulbs", 9m, code: "42")))).Read<ReceiptDto>();

        var withPhoto = await (await c.PostAsync($"/api/expenses/receipts/{r.Id}/photos", Photo())).Read<ReceiptDto>();
        var src = Assert.Single(withPhoto.Images);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync(src)).StatusCode);
        Assert.True((await (await c.GetAsync("/api/expenses/receipts")).Read<List<ReceiptSummaryDto>>()).Single().HasPhoto);

        (await c.DeleteAsync($"/api/expenses/receipts/{r.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync(src)).StatusCode);
        Assert.Empty(await (await c.GetAsync("/api/expenses/items")).Read<List<TrackedItemDto>>()); // its only purchase is gone

        // The store number is forgotten too, so a new purchase makes a fresh item.
        var again = await (await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today, 9m, Line("Bulbs", 9m, code: "42")))).Read<ReceiptDto>();
        Assert.Equal("Bulbs", (await (await c.GetAsync($"/api/expenses/items/{again.Lines[0].ItemId}")).Read<TrackedItemDetailDto>()).Item.Name);
    }

    [Fact]
    public async Task Merging_moves_purchases_and_store_numbers()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        var costco = await (await c.Post("/api/expenses/receipts", Receipt("Costco", app.Today.AddDays(-10), 18m, Line("Tide Pods", 18m, code: "111")))).Read<ReceiptDto>();
        var walmart = await (await c.Post("/api/expenses/receipts", Receipt("Walmart", app.Today, 22m, Line("Tide pods 81", 22m, code: "999")))).Read<ReceiptDto>();
        var keep = costco.Lines[0].ItemId!.Value;

        var merged = await (await c.Post($"/api/expenses/items/{walmart.Lines[0].ItemId}/merge", new { intoItemId = keep })).Read<TrackedItemDetailDto>();
        Assert.Equal(keep, merged.Item.Id);
        Assert.Equal(2, merged.Purchases.Count);
        Assert.Equal(2, merged.Item.Codes.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/expenses/items/{walmart.Lines[0].ItemId}")).StatusCode);

        // The Walmart number now leads to the kept item.
        var next = await (await c.Post("/api/expenses/receipts", Receipt("Walmart", app.Today, 21m, Line("TIDE", 21m, code: "999")))).Read<ReceiptDto>();
        Assert.Equal(keep, next.Lines[0].ItemId);
    }

    [Fact]
    public async Task Bad_requests_are_refused()
    {
        using var app = new TestApp();
        var c = await StartWithAi(app);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/expenses/receipts",
            Receipt("Costco", app.Today, 1m, Line("Thing", 1m, "made-up")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/expenses/receipts",
            Receipt("Costco", app.Today, 1m, Line("Thing", 1m, itemId: 12345)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Post("/api/expenses/receipts/manual",
            new { purchasedAt = app.Today, amount = 0, category = "dining", description = "Nothing" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/expenses/read", new MultipartFormDataContent())).StatusCode);
    }
}
