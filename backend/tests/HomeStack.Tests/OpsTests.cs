using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeStack.Api;
using HomeStack.Core;
using HomeStack.Core.Auth;
using HomeStack.Core.Notifications;
using HomeStack.Modules.Inventory;
using Microsoft.Data.Sqlite;

namespace HomeStack.Tests;

public class NotificationTests
{
    private static async Task<(TestApp App, HttpClient Client)> StartWithItems()
    {
        var app = new TestApp();
        var c = await app.SetupClientAsync();
        var locs = await (await c.GetAsync("/api/inventory/locations")).Read<List<LocationDto>>();
        int Loc(LocationKind k) => locs.First(l => l.Kind == k).Id;

        await c.Post("/api/inventory/items", new { product = new { name = "Chicken thighs", category = "meat" }, locationId = Loc(LocationKind.Fridge), expiresAt = app.Today.AddDays(1) });
        await c.Post("/api/inventory/items", new { product = new { name = "Bagels", category = "bakery" }, locationId = Loc(LocationKind.Pantry), expiresAt = app.Today });
        await c.Post("/api/inventory/items", new { product = new { name = "Crackers", category = "snacks" }, locationId = Loc(LocationKind.Pantry), expiresAt = app.Today.AddDays(-2) });
        (await c.Post("/api/meals/plan", new { date = app.Today, slot = "dinner", title = "Takeout" })).EnsureSuccessStatusCode();
        return (app, c);
    }

    private static Task<HttpResponseMessage> SaveSettings(HttpClient c, object body) => c.PutAsJsonAsync("/api/notifications/settings", body, TestApp.Json);

    [Fact]
    public async Task Preview_summarises_what_needs_attention()
    {
        var (app, c) = await StartWithItems();
        using var _ = app;

        var m = await (await c.GetAsync("/api/notifications/preview")).Read<DigestMessage>();
        Assert.False(m.IsEmpty);
        Assert.True(m.Urgent);
        Assert.Equal("1 to freeze · 1 expiring · 1 expired", m.Title);
        Assert.Contains("Freeze it or use it\n• Chicken thighs (expires tomorrow)", m.Body);
        Assert.Contains("• Bagels (expires today)", m.Body);
        Assert.Contains("• Crackers", m.Body);
        Assert.Contains("On the menu today\n• Dinner: Takeout", m.Body);
    }

    [Fact]
    public async Task Empty_household_has_nothing_to_say()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();
        var m = await (await c.GetAsync("/api/notifications/preview")).Read<DigestMessage>();
        Assert.True(m.IsEmpty);
    }

    [Fact]
    public async Task Settings_suggest_a_topic_validate_and_hide_the_token()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();

        var initial = await (await c.GetAsync("/api/notifications/settings")).Read<NotificationSettingsDto>();
        Assert.False(initial.Saved);
        Assert.StartsWith("homestack-", initial.Topic);

        var bad = await SaveSettings(c, new { enabled = true, serverUrl = "ftp://x", topic = "has spaces", digestHour = 30 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var problem = await bad.Content.ReadFromJsonAsync<JsonElement>();
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("topic", out _));
        Assert.True(errors.TryGetProperty("serverUrl", out _));
        Assert.True(errors.TryGetProperty("digestHour", out _));

        var saved = await (await SaveSettings(c, new { enabled = true, serverUrl = "https://ntfy.example/", topic = initial.Topic, accessToken = "tk_secret", digestHour = 7 }))
            .Read<NotificationSettingsDto>();
        Assert.True(saved.Saved);
        Assert.True(saved.HasAccessToken);
        Assert.Equal("https://ntfy.example", saved.ServerUrl);
        Assert.DoesNotContain("tk_secret", await (await c.GetAsync("/api/notifications/settings")).Content.ReadAsStringAsync());

        // null keeps the token, "" clears it.
        Assert.True((await (await SaveSettings(c, new { enabled = true, serverUrl = "https://ntfy.example", topic = initial.Topic, digestHour = 7 })).Read<NotificationSettingsDto>()).HasAccessToken);
        Assert.False((await (await SaveSettings(c, new { enabled = true, serverUrl = "https://ntfy.example", topic = initial.Topic, accessToken = "", digestHour = 7 })).Read<NotificationSettingsDto>()).HasAccessToken);
    }

    [Fact]
    public async Task Test_send_publishes_to_ntfy_and_reports_failures()
    {
        var (app, c) = await StartWithItems();
        using var _ = app;

        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/notifications/test", null)).StatusCode);

        (await SaveSettings(c, new { enabled = true, serverUrl = "https://ntfy.example", topic = "homestack-abc", accessToken = "tk_1", digestHour = 8 })).EnsureSuccessStatusCode();
        (await c.PostAsync("/api/notifications/test", null)).EnsureSuccessStatusCode();

        var (req, body) = Assert.Single(app.Ntfy.Requests);
        Assert.Equal("https://ntfy.example/", req.RequestUri!.ToString());
        Assert.Equal("Bearer tk_1", req.Headers.Authorization!.ToString());
        var json = JsonDocument.Parse(body).RootElement;
        Assert.Equal("homestack-abc", json.GetProperty("topic").GetString());
        Assert.Equal(4, json.GetProperty("priority").GetInt32());
        Assert.Equal("http://localhost", json.GetProperty("click").GetString());
        Assert.Contains("Chicken thighs", json.GetProperty("message").GetString());

        app.Ntfy.Status = HttpStatusCode.Forbidden;
        var failed = await c.PostAsync("/api/notifications/test", null);
        Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        Assert.Contains("403", await failed.Content.ReadAsStringAsync());
        var settings = await (await c.GetAsync("/api/notifications/settings")).Read<NotificationSettingsDto>();
        Assert.Contains("403", settings.LastError);
    }

    [Fact]
    public async Task Daily_send_runs_outside_a_request_for_the_right_household()
    {
        var (app, c) = await StartWithItems();
        using var _ = app;
        var me = await (await c.GetAsync("/api/auth/me")).Read<MeDto>();
        (await SaveSettings(c, new { enabled = true, serverUrl = "https://ntfy.example", topic = "homestack-abc", digestHour = 8 })).EnsureSuccessStatusCode();

        // Same path the background worker takes: a fresh scope with the household entered, no HTTP context.
        await using (var scope = app.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<HouseholdScope>().Enter(me.Household.Id);
            await scope.ServiceProvider.GetRequiredService<DigestService>().SendDailyAsync();
        }

        var (_, body) = Assert.Single(app.Ntfy.Requests);
        Assert.Contains("Bagels", body);
        var settings = await (await c.GetAsync("/api/notifications/settings")).Read<NotificationSettingsDto>();
        Assert.NotNull(settings.LastSentAt);
        Assert.Null(settings.LastError);
    }
}

public class BackupTests
{
    [Fact]
    public async Task Backup_is_a_readable_copy_and_old_ones_are_pruned()
    {
        using var app = new TestApp();
        var c = await app.SetupClientAsync();

        Directory.CreateDirectory(app.BackupDir);
        foreach (var day in new[] { "2020-01-01", "2020-01-02", "2020-01-03" })
            await File.WriteAllTextAsync(Path.Combine(app.BackupDir, $"homestack-{day}.db"), "old");

        var file = await (await c.PostAsync("/api/system/backups", null)).Read<BackupFileDto>();
        Assert.Equal($"homestack-{app.Today:yyyy-MM-dd}.db", file.Name);

        var list = await (await c.GetAsync("/api/system/backups")).Read<BackupsDto>();
        Assert.Equal([file.Name, "homestack-2020-01-03.db", "homestack-2020-01-02.db"], list.Files.Select(f => f.Name));

        await using var db = new SqliteConnection($"Data Source={Path.Combine(app.BackupDir, file.Name)};Mode=ReadOnly;Pooling=False");
        await db.OpenAsync();
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "select Name from households";
        Assert.Equal("Test Home", await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Backups_require_sign_in()
    {
        using var app = new TestApp();
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().PostAsync("/api/system/backups", null)).StatusCode);
    }
}
