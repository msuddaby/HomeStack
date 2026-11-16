using System.Net;
using HomeStack.Core.Auth;
using Microsoft.AspNetCore.Hosting;

namespace HomeStack.Tests;

public class AccountTests
{
    [Fact]
    public async Task Change_password_needs_the_current_one()
    {
        using var app = new TestApp();
        var client = await app.SetupClientAsync();

        var wrong = await client.Post("/api/auth/password", new { currentPassword = "not-it-at-all", newPassword = "brand-new-pass" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Contains("currentPassword", await wrong.Content.ReadAsStringAsync());

        var tooShort = await client.Post("/api/auth/password", new { currentPassword = "password123", newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);

        var ok = await client.Post("/api/auth/password", new { currentPassword = "password123", newPassword = "brand-new-pass" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var fresh = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await fresh.Post("/api/auth/login", new { username = "tester", password = "password123" })).StatusCode);
        (await fresh.Post("/api/auth/login", new { username = "tester", password = "brand-new-pass" })).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Removing_a_member_signs_them_out_but_you_cannot_remove_yourself()
    {
        using var app = new TestApp();
        var owner = await app.SetupClientAsync();
        var me = await (await owner.GetAsync("/api/auth/me")).Read<MeDto>();
        var sam = await (await owner.Post("/api/household/users", new { name = "Sam", username = "sam", password = "hunter2hunter2" })).Read<UserDto>();

        var samClient = app.CreateClient();
        (await samClient.Post("/api/auth/login", new { username = "sam", password = "hunter2hunter2" })).EnsureSuccessStatusCode();
        (await samClient.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.DeleteAsync($"/api/household/users/{me.User.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.DeleteAsync("/api/household/users/9999")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/household/users/{sam.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await samClient.GetAsync("/api/inventory/items")).StatusCode);

        var users = await (await owner.GetAsync("/api/household/users")).Read<List<UserDto>>();
        Assert.Equal([me.User.Id], users.Select(u => u.Id));
    }

    [Fact]
    public async Task Too_many_login_attempts_are_refused()
    {
        using var baseApp = new TestApp();
        using var app = baseApp.WithWebHostBuilder(b => b.UseSetting("Auth:LoginAttemptsPerMinute", "3"));
        var client = app.CreateClient();

        // Setup counts as an attempt too: 1 + 2 wrong passwords uses the budget of 3.
        (await client.Post("/api/auth/setup", new { householdName = "Home", name = "T", username = "tester", password = "password123" }))
            .EnsureSuccessStatusCode();
        for (var i = 0; i < 2; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.Post("/api/auth/login", new { username = "tester", password = "guess-guess" })).StatusCode);

        var blocked = await client.Post("/api/auth/login", new { username = "tester", password = "password123" });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Too many attempts", await blocked.Content.ReadAsStringAsync());

        // Other endpoints aren't limited.
        (await client.GetAsync("/api/auth/status")).EnsureSuccessStatusCode();
    }
}
