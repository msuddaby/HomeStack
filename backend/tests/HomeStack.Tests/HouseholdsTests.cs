using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HomeStack.Core;
using HomeStack.Core.Admin;
using HomeStack.Core.Ai;
using HomeStack.Core.Auth;
using HomeStack.Modules.Expenses;
using HomeStack.Modules.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace HomeStack.Tests;

public class HouseholdsTests
{
    /// <summary>The instance admin's household (set up as "tester"), plus the Smiths, who joined with an invite.</summary>
    private static async Task<(HttpClient Admin, HttpClient Smith, MeDto SmithMe)> TwoHouseholds(TestApp app)
    {
        var admin = await app.SetupClientAsync();
        var invite = await (await admin.Post("/api/admin/invites", new { note = "The Smiths" })).Read<NewInviteDto>();
        var smith = app.CreateClient();
        var me = await (await smith.Post("/api/auth/join", new
        {
            token = invite.Token, householdName = "Smiths", name = "Sam Smith", username = "smith", password = "password123",
        })).Read<MeDto>();
        return (admin, smith, me);
    }

    private static async Task<int> Fridge(HttpClient c) =>
        (await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).First(l => l.Kind == LocationKind.Fridge).Id;

    private static MultipartFormDataContent Png()
    {
        using var bitmap = new SKBitmap(20, 20);
        bitmap.Erase(SKColors.OrangeRed);
        using var data = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        var file = new ByteArrayContent(data.ToArray());
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { file, "image", "photo.png" } };
    }

    [Fact]
    public async Task Invite_links_bring_in_a_separate_household_once()
    {
        using var app = new TestApp();
        var admin = await app.SetupClientAsync();
        Assert.True((await (await admin.GetAsync("/api/auth/me")).Read<MeDto>()).IsInstanceAdmin);

        var invite = await (await admin.Post("/api/admin/invites", new { note = "The Smiths" })).Read<NewInviteDto>();
        Assert.Equal(app.Time.Now.UtcDateTime.AddDays(7), invite.Invite.ExpiresAt);
        var stranger = app.CreateClient();
        var check = await (await stranger.GetAsync($"/api/auth/invites/{invite.Token}")).Read<InviteCheckDto>();
        Assert.True(check.Valid);
        Assert.Equal("The Smiths", check.Note);
        Assert.False((await (await stranger.GetAsync("/api/auth/invites/not-a-real-token")).Read<InviteCheckDto>()).Valid);

        // The admin's username is reserved, even on an invite.
        var reserved = await stranger.Post("/api/auth/join", new { token = invite.Token, householdName = "Smiths", name = "Sam", username = "Tester", password = "password123" });
        Assert.Equal(HttpStatusCode.BadRequest, reserved.StatusCode);
        Assert.Contains("reserved", await reserved.Content.ReadAsStringAsync());

        var me = await (await stranger.Post("/api/auth/join", new { token = invite.Token, householdName = "Smiths", name = "Sam Smith", username = "smith", password = "password123" }))
            .Read<MeDto>();
        Assert.Equal("Smiths", me.Household.Name);
        Assert.True(me.User.IsAdmin);
        Assert.False(me.IsInstanceAdmin);
        Assert.Equal(3, (await (await stranger.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>()).Count); // modules set it up

        // Used: the same link can't make another household.
        var again = await app.CreateClient().Post("/api/auth/join", new { token = invite.Token, householdName = "Twice", name = "X", username = "twice", password = "password123" });
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.False((await (await stranger.GetAsync($"/api/auth/invites/{invite.Token}")).Read<InviteCheckDto>()).Valid);

        // Revoked and expired links don't work either.
        var revoked = await (await admin.Post("/api/admin/invites", new { })).Read<NewInviteDto>();
        (await admin.DeleteAsync($"/api/admin/invites/{revoked.Invite.Id}")).EnsureSuccessStatusCode();
        Assert.False((await (await stranger.GetAsync($"/api/auth/invites/{revoked.Token}")).Read<InviteCheckDto>()).Valid);
        var expiring = await (await admin.Post("/api/admin/invites", new { days = 1 })).Read<NewInviteDto>();
        app.Time.Now = app.Time.Now.AddDays(2);
        Assert.False((await (await stranger.GetAsync($"/api/auth/invites/{expiring.Token}")).Read<InviteCheckDto>()).Valid);

        var invites = await (await admin.GetAsync("/api/admin/invites")).Read<List<InviteDto>>();
        Assert.Equal("Smiths", invites.Single(i => i.Id == invite.Invite.Id).Household);
        Assert.NotNull(invites.Single(i => i.Id == revoked.Invite.Id).RevokedAt);
    }

    [Fact]
    public async Task Households_cant_see_each_others_data()
    {
        using var app = new TestApp();
        var (admin, smith, _) = await TwoHouseholds(app);

        var item = await (await admin.Post("/api/inventory/items", new { product = new { name = "Chicken", category = "meat" }, locationId = await Fridge(admin) })).Read<ItemDto>();
        var product = await (await admin.PostAsync($"/api/inventory/products/{item.Product.Id}/image", Png())).Read<ProductDto>();
        var receipt = await (await admin.Post("/api/expenses/receipts/manual", new { purchasedAt = app.Today, amount = 20m, category = "groceries", description = "Milk" }))
            .Read<ReceiptDto>();

        Assert.Empty(await (await smith.GetAsync("/api/inventory/items")).Read<List<ItemDto>>());
        Assert.Empty(await (await smith.GetAsync("/api/expenses/receipts")).Read<List<ReceiptSummaryDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await smith.GetAsync($"/api/inventory/items/{item.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await smith.GetAsync($"/api/expenses/receipts/{receipt.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await smith.GetAsync(product.Image)).StatusCode);
        Assert.Single(await (await smith.GetAsync("/api/household/users")).Read<List<UserDto>>());
        Assert.DoesNotContain((await (await smith.GetAsync("/api/expenses/people")).Read<List<PersonDto>>()), p => p.Name == "Tester");
    }

    [Fact]
    public async Task Only_the_instance_admin_manages_households_and_backups()
    {
        using var app = new TestApp();
        var (admin, smith, smithMe) = await TwoHouseholds(app);

        // The Smiths' own admin runs their household, not the instance.
        foreach (var url in new[] { "/api/admin/households", "/api/admin/invites", "/api/admin/settings", "/api/system/backups" })
            Assert.Equal(HttpStatusCode.Forbidden, (await smith.GetAsync(url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await smith.Post("/api/admin/invites", new { })).StatusCode);
        var reserved = await smith.Post("/api/household/users", new { name = "Not Matt", username = "tester2", password = "password123" });
        reserved.EnsureSuccessStatusCode(); // ordinary names are fine…
        Assert.Equal(HttpStatusCode.BadRequest, (await smith.Post("/api/household/users", new { name = "X", username = "TESTER", password = "password123" })).StatusCode); // …the admin's isn't

        var households = await (await admin.GetAsync("/api/admin/households")).Read<List<AdminHouseholdDto>>();
        Assert.Equal(["Smiths", "Test Home"], households.Select(h => h.Name));
        var smiths = households.Single(h => h.Name == "Smiths");
        Assert.Equal((2, false), (smiths.Members, smiths.IsMine));
        Assert.Equal(["Sam Smith"], smiths.Admins);
        Assert.True(households.Single(h => h.Name == "Test Home").IsMine);

        (await admin.PutAsJsonAsync($"/api/admin/households/{smiths.Id}", new { name = "The Smiths" }, TestApp.Json)).EnsureSuccessStatusCode();
        Assert.Equal("The Smiths", (await (await smith.GetAsync("/api/auth/me")).Read<MeDto>()).Household.Name);

        // Sam forgot their password.
        var users = await (await admin.GetAsync($"/api/admin/households/{smiths.Id}/users")).Read<List<AdminUserDto>>();
        var sam = users.Single(u => u.Username == "smith");
        (await admin.Post($"/api/admin/users/{sam.Id}/password", new { newPassword = "new-password-1" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().Post("/api/auth/login", new { username = "smith", password = "password123" })).StatusCode);
        (await app.CreateClient().Post("/api/auth/login", new { username = "smith", password = "new-password-1" })).EnsureSuccessStatusCode();
        Assert.Equal(smithMe.User.Id, sam.Id);
    }

    [Fact]
    public async Task Deleting_a_household_removes_everything_in_it_and_nothing_else()
    {
        using var app = new TestApp();
        var (admin, smith, smithMe) = await TwoHouseholds(app);
        var smiths = smithMe.Household.Id;
        int mine = (await (await admin.GetAsync("/api/auth/me")).Read<MeDto>()).Household.Id;

        foreach (var c in new[] { admin, smith })
        {
            var item = await (await c.Post("/api/inventory/items", new { product = new { name = "Chicken", category = "meat" }, locationId = await Fridge(c) })).Read<ItemDto>();
            (await c.PostAsync($"/api/inventory/products/{item.Product.Id}/image", Png())).EnsureSuccessStatusCode();
            (await c.Post("/api/shopping/items", new { name = "Bread" })).EnsureSuccessStatusCode();
            (await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", title = "Tacos" })).EnsureSuccessStatusCode();
            (await c.Post("/api/household/users", new { name = "Kid", username = c == admin ? "kid1" : "kid2", password = "password123" })).EnsureSuccessStatusCode();
            var people = await (await c.GetAsync("/api/expenses/people")).Read<List<PersonDto>>();
            (await c.Post("/api/expenses/receipts/manual", new
            {
                purchasedAt = app.Today, amount = 30m, category = "groceries", description = "Groceries",
                split = people.Select(p => new { personId = p.Id, weight = 1 }).ToArray(),
            })).EnsureSuccessStatusCode();
            (await c.PutAsJsonAsync("/api/ai/settings", new { provider = "anthropic", baseUrl = "https://api.anthropic.com", apiKey = "k", model = "m" }, TestApp.Json))
                .EnsureSuccessStatusCode();
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomeStackDbContext>();
        var tables = db.Model.GetEntityTypes().Where(t => t.FindProperty("HouseholdId") is not null && t.GetTableName() is { } name && name != "households")
            .Select(t => t.GetTableName()!).Distinct().ToList();
        Assert.Contains("inventory_items", tables);
        Assert.Contains("expenses_receipts", tables);
        async Task<Dictionary<string, int>> Rows(int household)
        {
            var rows = new Dictionary<string, int>();
            foreach (var t in tables) rows[t] = await Count(db, $"SELECT COUNT(*) AS Value FROM \"{t}\" WHERE \"HouseholdId\" = {household}");
            return rows;
        }
        var before = await Rows(mine);
        var theirs = await Rows(smiths);
        foreach (var table in new[] { "users", "inventory_items", "inventory_products", "images", "shopping_items", "meals_plan", "expenses_receipts", "expenses_people", "ai_settings" })
            Assert.True(theirs.GetValueOrDefault(table) > 0, $"test set-up added nothing to {table}");

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/admin/households/{smiths}")).StatusCode); // name not typed
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"/api/admin/households/{mine}?confirmName=Test%20Home")).StatusCode);
        (await admin.DeleteAsync($"/api/admin/households/{smiths}?confirmName=smiths")).EnsureSuccessStatusCode();

        Assert.All(await Rows(smiths), kv => Assert.True(kv.Value == 0, $"{kv.Key} still has the Smiths' rows"));
        Assert.Equal(before, await Rows(mine));
        // Child rows (receipt lines, shares…) went with their parents.
        Assert.Equal(0, await Count(db, "SELECT COUNT(*) AS Value FROM \"expenses_receipt_lines\" WHERE \"ReceiptId\" NOT IN (SELECT \"Id\" FROM \"expenses_receipts\")"));
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA foreign_key_check;";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.False(await reader.ReadAsync(), "foreign key violations left behind");
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await smith.GetAsync("/api/auth/me")).StatusCode);
        Assert.Single(await (await admin.GetAsync("/api/inventory/items")).Read<List<ItemDto>>());
    }

    private static Task<int> Count(HomeStackDbContext db, string sql) => db.Database.SqlQueryRaw<int>(sql).SingleAsync();

    private static string AnthropicReply(string text) => new JsonObject
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["stop_reason"] = "end_turn",
    }.ToJsonString();

    [Fact]
    public async Task The_admin_can_share_their_AI_with_households_that_have_none()
    {
        using var app = new TestApp();
        var (admin, smith, _) = await TwoHouseholds(app);
        (await admin.PutAsJsonAsync("/api/ai/settings", new { provider = "anthropic", baseUrl = "https://api.anthropic.com", apiKey = "sk-ant-main", model = "claude-x" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        var read = new { text = "a dozen eggs" };
        string Reply() => AnthropicReply("""{"items": [{"name": "Eggs", "category": "eggs", "location": "fridge", "quantity": 12}]}""");

        Assert.False((await (await smith.GetAsync("/api/ai/settings")).Read<AiSettingsDto>()).Configured);
        Assert.Equal(HttpStatusCode.BadRequest, (await smith.Post("/api/inventory/read/text", read)).StatusCode);

        var shared = await (await admin.PutAsJsonAsync("/api/admin/settings", new { shareAi = true }, TestApp.Json)).Read<InstanceSettingsDto>();
        Assert.Equal((true, "Test Home"), (shared.ShareAi, shared.MainHousehold));
        var settingsJson = await (await smith.GetAsync("/api/ai/settings")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("sk-ant", settingsJson);
        Assert.DoesNotContain("claude-x", settingsJson);
        var settings = await (await smith.GetAsync("/api/ai/settings")).Read<AiSettingsDto>();
        Assert.Equal((true, false, "Tester", false), (settings.Configured, settings.Own, settings.SharedBy, settings.HasApiKey));

        app.Ai.Bodies.Enqueue(Reply());
        (await smith.Post("/api/inventory/read/text", read)).EnsureSuccessStatusCode();
        Assert.Equal("sk-ant-main", app.Ai.Requests.Last().Request.Headers.GetValues("x-api-key").Single());

        // Their own settings win; testing a connection never borrows the shared key.
        (await smith.PutAsJsonAsync("/api/ai/settings", new { provider = "anthropic", baseUrl = "https://api.anthropic.com", apiKey = "sk-ant-smith", model = "claude-y" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        Assert.True((await (await smith.GetAsync("/api/ai/settings")).Read<AiSettingsDto>()).Own);
        app.Ai.Bodies.Enqueue(Reply());
        (await smith.Post("/api/inventory/read/text", read)).EnsureSuccessStatusCode();
        Assert.Equal("sk-ant-smith", app.Ai.Requests.Last().Request.Headers.GetValues("x-api-key").Single());

        (await smith.DeleteAsync("/api/ai/settings")).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync("/api/admin/settings", new { shareAi = false }, TestApp.Json)).EnsureSuccessStatusCode();
        Assert.False((await (await smith.GetAsync("/api/ai/settings")).Read<AiSettingsDto>()).Configured);
    }
}
