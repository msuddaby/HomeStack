using System.Net;
using System.Net.Http.Json;
using HomeStack.Core.Auth;

namespace HomeStack.Tests;

public class AuthTests
{
    [Fact]
    public async Task Setup_only_once_then_login_and_add_household_members()
    {
        using var app = new TestApp();
        var anon = app.CreateClient();

        var status = await (await anon.GetAsync("/api/auth/status")).Read<AuthStatusDto>();
        Assert.True(status.NeedsSetup);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/inventory/items")).StatusCode);

        var owner = await app.SetupClientAsync();
        var me = await (await owner.GetAsync("/api/auth/me")).Read<MeDto>();
        Assert.Equal("Test Home", me.Household.Name);

        // Second setup is rejected.
        var again = await anon.Post("/api/auth/setup", new { householdName = "X", name = "X", username = "xx", password = "password123" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);

        // Add a household member, who then shares the same household.
        (await owner.Post("/api/household/users", new { name = "Sam", username = "Sam", password = "hunter2hunter2" })).EnsureSuccessStatusCode();
        var sam = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await sam.Post("/api/auth/login", new { username = "sam", password = "wrong-password" })).StatusCode);
        var samMe = await (await sam.Post("/api/auth/login", new { username = "sam", password = "hunter2hunter2" })).Read<MeDto>();
        Assert.Equal(me.Household.Id, samMe.Household.Id);
    }

    [Fact]
    public async Task Setup_validates_input()
    {
        using var app = new TestApp();
        var res = await app.CreateClient().PostAsJsonAsync("/api/auth/setup",
            new { householdName = "", name = "A", username = "a b", password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("password", body);
        Assert.Contains("username", body);
    }
}
