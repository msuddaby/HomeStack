using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HomeStack.Core;
using HomeStack.Core.Auth;
using HomeStack.Modules.Expenses;
using Microsoft.Extensions.DependencyInjection;
using Device = HomeStack.Tests.PushTests.Device;

namespace HomeStack.Tests;

public class ExpenseNotificationTests
{
    /// <summary>Alex (admin, set-up user "Tester"), Sam and Jo, each signed in with a device that has notifications on.</summary>
    private sealed class Home : IDisposable
    {
        public TestApp App { get; } = new();
        public HttpClient Alex = null!, Sam = null!, Jo = null!;
        public Device AlexPhone { get; } = new();
        public Device SamPhone { get; } = new();
        public Device JoPhone { get; } = new();
        public List<PersonDto> People = null!;
        public int HouseholdId;

        public async Task<Home> StartAsync(bool joSubscribes = true)
        {
            Alex = await App.SetupClientAsync();
            foreach (var (name, user) in new[] { ("Sam", "sam"), ("Jo", "jo") })
                (await Alex.Post("/api/household/users", new { name, username = user, password = "password123" })).EnsureSuccessStatusCode();
            Sam = await Login("sam");
            Jo = await Login("jo");
            var phones = new List<(HttpClient, Device)> { (Alex, AlexPhone), (Sam, SamPhone) };
            if (joSubscribes) phones.Add((Jo, JoPhone));
            foreach (var (c, d) in phones)
                (await c.Post("/api/notifications/push/subscriptions", d.Subscription())).EnsureSuccessStatusCode();
            People = await (await Alex.GetAsync("/api/expenses/people")).Read<List<PersonDto>>();
            HouseholdId = (await (await Alex.GetAsync("/api/auth/me")).Read<MeDto>()).Household.Id;
            App.Push.Requests.Clear();
            App.Push.RawBodies.Clear();
            return this;
        }

        private async Task<HttpClient> Login(string user)
        {
            var c = App.CreateClient();
            (await c.Post("/api/auth/login", new { username = user, password = "password123" })).EnsureSuccessStatusCode();
            return c;
        }

        public int Id(string name) => People.Single(p => p.Name == name).Id;

        /// <summary>The notifications each device got, decrypted, since the last call.</summary>
        public List<JsonElement> Sent(Device d)
        {
            var got = App.Push.Requests.Select((r, i) => (r.Request.RequestUri!.ToString(), i)).Where(x => x.Item1 == d.Endpoint)
                .Select(x => d.Decrypt(App.Push.RawBodies[x.i])).ToList();
            return got;
        }

        public void Clear() { App.Push.Requests.Clear(); App.Push.RawBodies.Clear(); }

        public async Task<T> AsHousehold<T>(Func<IServiceProvider, Task<T>> run)
        {
            await using var scope = App.Services.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<HouseholdScope>().Enter(HouseholdId);
            return await run(scope.ServiceProvider);
        }

        public void Dispose() { App.Dispose(); AlexPhone.Dispose(); SamPhone.Dispose(); JoPhone.Dispose(); }
    }

    private static object Part(int personId) => new { personId, weight = 1 };

    [Fact]
    public async Task People_who_now_owe_more_are_told_but_not_whoever_made_the_change()
    {
        using var h = await new Home().StartAsync();
        int a = h.Id("Tester"), s = h.Id("Sam"), j = h.Id("Jo");

        var r = await (await h.Alex.Post("/api/expenses/receipts", new
        {
            store = "Costco", purchasedAt = h.App.Today, total = 33m, tax = 3m, split = new[] { Part(a), Part(s), Part(j) },
            lines = new object[]
            {
                new { name = "Detergent", category = "household", quantity = 1, amount = 18m },
                new { name = "Chips", category = "snacks", quantity = 1, amount = 6m, split = new[] { Part(a), Part(s) } },
                new { name = "Shirt", category = "clothing", quantity = 1, amount = 6m, split = new[] { Part(j) } },
            },
        })).Read<ReceiptDto>();

        Assert.Empty(h.Sent(h.AlexPhone)); // Alex added it
        var sam = Assert.Single(h.Sent(h.SamPhone));
        Assert.Equal("You owe Tester $9.90", sam.GetProperty("title").GetString());
        Assert.Equal($"Costco · {h.App.Today:MMM d}\nFor Detergent, Chips\nAdded by Tester", sam.GetProperty("body").GetString());
        Assert.Equal($"/expenses/receipts/{r.Id}", sam.GetProperty("url").GetString());
        Assert.Equal("You owe Tester $13.20", Assert.Single(h.Sent(h.JoPhone)).GetProperty("title").GetString());

        // The shirt was Sam's: Sam owes more and hears about it; Jo owes less and doesn't.
        h.Clear();
        var shirt = r.Lines.Single(l => l.Name == "Shirt").Id;
        (await h.Alex.PutAsJsonAsync($"/api/expenses/receipts/{r.Id}/split", new
        {
            paidByPersonId = a, split = new[] { Part(a), Part(s), Part(j) }, lines = new[] { new { lineId = shirt, split = new[] { Part(s) } } },
        }, TestApp.Json)).EnsureSuccessStatusCode();
        Assert.Equal("You owe Tester $16.50", Assert.Single(h.Sent(h.SamPhone)).GetProperty("title").GetString());
        Assert.Empty(h.Sent(h.JoPhone));

        // Sam records that Jo paid for dinner and Sam owes half: Sam did it, so only Jo... who's owed, not owing. Nobody hears.
        h.Clear();
        (await h.Sam.Post("/api/expenses/receipts/manual", new
        {
            purchasedAt = h.App.Today, amount = 40m, category = "dining", description = "Dinner", paidByPersonId = j, split = new[] { Part(s), Part(j) },
        })).EnsureSuccessStatusCode();
        Assert.Empty(h.App.Push.Requests);
    }

    [Fact]
    public async Task Overdue_bills_are_announced_once_to_whoever_pays_them()
    {
        using var h = await new Home().StartAsync();
        int s = h.Id("Sam");
        var today = h.App.Today;
        async Task<ReceiptDto> Bill(string payee, int dueDaysAgo, int? paidBy) => await (await h.Alex.Post("/api/expenses/bills", new
        {
            payee, statementDate = today.AddDays(-30), dueDate = today.AddDays(-dueDaysAgo), amount = 80m, category = "utilities", paidByPersonId = paidBy,
        })).Read<ReceiptDto>();
        var enmax = await Bill("Enmax", 1, s);         // Sam pays it: just Sam
        await Bill("Telus", 3, null);                  // nobody named: everyone
        await Bill("Old Gas Co", 10, s);               // overdue too long ago: left to the daily reminder
        await Bill("Shaw", 0, s);                      // due today: not overdue yet
        h.Clear();

        Assert.Equal(2, await h.AsHousehold(sp => sp.GetRequiredService<ExpenseNotifier>().OverdueBillsAsync()));
        var sam = h.Sent(h.SamPhone).Select(m => m.GetProperty("title").GetString()).Order().ToList();
        Assert.Equal(["Overdue: Enmax $80.00", "Overdue: Telus $80.00"], sam);
        Assert.Equal(["Overdue: Telus $80.00"], h.Sent(h.AlexPhone).Select(m => m.GetProperty("title").GetString()));
        Assert.Equal($"/expenses/receipts/{enmax.Id}", h.Sent(h.SamPhone).First(m => m.GetProperty("title").GetString()!.Contains("Enmax")).GetProperty("url").GetString());
        Assert.True(h.Sent(h.SamPhone).All(m => m.GetProperty("urgent").GetBoolean()));

        h.Clear();
        Assert.Equal(0, await h.AsHousehold(sp => sp.GetRequiredService<ExpenseNotifier>().OverdueBillsAsync()));
        Assert.Empty(h.App.Push.Requests);
    }

    [Fact]
    public async Task Admins_remind_someone_who_hasnt_paid_but_not_too_often()
    {
        using var h = await new Home().StartAsync(joSubscribes: false);
        int a = h.Id("Tester"), s = h.Id("Sam"), j = h.Id("Jo");
        var pat = await (await h.Alex.Post("/api/expenses/people", new { name = "Pat" })).Read<PersonDto>();
        foreach (var (what, amount) in new[] { ("Groceries", 30m), ("Internet", 50m) })
            (await h.Alex.Post("/api/expenses/receipts/manual", new
            {
                purchasedAt = h.App.Today, amount, category = "groceries", description = what, split = new[] { Part(a), Part(s), Part(j), Part(pat.Id) },
            })).EnsureSuccessStatusCode();
        h.Clear();

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Sam.Post("/api/expenses/remind", new { personId = j, owedToPersonId = a })).StatusCode);
        var notOnHomeStack = await h.Alex.Post("/api/expenses/remind", new { personId = pat.Id, owedToPersonId = a });
        Assert.Equal(HttpStatusCode.BadRequest, notOnHomeStack.StatusCode);
        Assert.Contains("isn't on HomeStack", await notOnHomeStack.Content.ReadAsStringAsync());
        var noDevice = await h.Alex.Post("/api/expenses/remind", new { personId = j, owedToPersonId = a });
        Assert.Equal(HttpStatusCode.BadRequest, noDevice.StatusCode);
        Assert.Contains("hasn't turned on notifications", await noDevice.Content.ReadAsStringAsync());

        (await h.Alex.Post("/api/expenses/remind", new { personId = s, owedToPersonId = a })).EnsureSuccessStatusCode();
        var m = Assert.Single(h.Sent(h.SamPhone));
        Assert.Equal("Reminder: you owe Tester $20.00", m.GetProperty("title").GetString());
        Assert.Equal("Groceries $7.50, Internet $12.50\nReminder from Tester", m.GetProperty("body").GetString());
        Assert.Equal("/expenses/owed", m.GetProperty("url").GetString());

        Assert.Equal(HttpStatusCode.Conflict, (await h.Alex.Post("/api/expenses/remind", new { personId = s, owedToPersonId = a })).StatusCode);
        var balance = (await (await h.Alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances.Single(b => b.PersonId == s);
        Assert.True(balance.IsMember);
        Assert.All(balance.Items, i => Assert.NotNull(i.RemindedAt));
        Assert.False((await (await h.Alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances.Single(b => b.PersonId == pat.Id).IsMember);

        // An hour later, one expense on its own.
        h.App.Time.Now = h.App.Time.Now.AddMinutes(61);
        h.Clear();
        var internet = balance.Items.Single(i => i.What == "Internet");
        (await h.Alex.Post("/api/expenses/remind", new { personId = s, owedToPersonId = a, shareId = internet.ShareId })).EnsureSuccessStatusCode();
        m = Assert.Single(h.Sent(h.SamPhone));
        Assert.Equal("Reminder: you owe Tester $12.50", m.GetProperty("title").GetString());
        Assert.Equal($"/expenses/receipts/{internet.ReceiptId}", m.GetProperty("url").GetString());
    }
}
