using System.Net;
using HomeStack.Core.Auth;
using HomeStack.Modules.Expenses;
using Device = HomeStack.Tests.PushTests.Device;

namespace HomeStack.Tests;

/// <summary>A removed member added again, and someone added by name who turns out to be a member: merging them.</summary>
public class PeopleMergeTests
{
    private static object Part(int personId, double weight = 1) => new { personId, weight };

    private static async Task<List<PersonDto>> People(HttpClient c) => await (await c.GetAsync("/api/expenses/people")).Read<List<PersonDto>>();

    private static async Task<HttpClient> Login(TestApp app, string user)
    {
        var c = app.CreateClient();
        (await c.Post("/api/auth/login", new { username = user, password = "password123" })).EnsureSuccessStatusCode();
        return c;
    }

    private static Task<HttpResponseMessage> Expense(HttpClient c, TestApp app, string what, decimal amount, int paidBy, params object[] split) =>
        c.Post("/api/expenses/receipts/manual", new
        {
            purchasedAt = app.Today, amount, category = "groceries", description = what, paidByPersonId = paidBy, split,
        });

    [Fact]
    public async Task A_member_removed_and_added_again_can_be_merged_and_reminded()
    {
        using var app = new TestApp();
        using var phone = new Device();
        var alex = await app.SetupClientAsync();
        (await alex.Post("/api/household/users", new { name = "Cole", username = "cole", password = "password123" })).EnsureSuccessStatusCode();
        var people = await People(alex);
        int a = people.Single(p => p.Name == "Tester").Id, oldCole = people.Single(p => p.Name == "Cole").Id;
        (await Expense(alex, app, "Groceries", 30m, a, Part(a), Part(oldCole))).EnsureSuccessStatusCode();

        // Cole can't sign in, so he's removed and added again; his phone gets notifications on the new account.
        var users = await (await alex.GetAsync("/api/household/users")).Read<List<UserDto>>();
        (await alex.DeleteAsync($"/api/household/users/{users.Single(u => u.Name == "Cole").Id}")).EnsureSuccessStatusCode();
        (await alex.Post("/api/household/users", new { name = "Cole", username = "cole2", password = "password123" })).EnsureSuccessStatusCode();
        var cole = await Login(app, "cole2");
        (await cole.Post("/api/notifications/push/subscriptions", phone.Subscription())).EnsureSuccessStatusCode();

        // What he owed is still on the old Cole, who's no longer a member, and is suggested as the new one.
        var balance = (await (await alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances.Single(b => b.PersonId == oldCole);
        Assert.Equal(15m, balance.Outstanding);
        Assert.False(balance.IsMember);
        var remindOld = await alex.Post("/api/expenses/remind", new { personId = oldCole, owedToPersonId = a });
        Assert.Equal(HttpStatusCode.BadRequest, remindOld.StatusCode);
        Assert.Contains("no longer a household member", await remindOld.Content.ReadAsStringAsync());

        people = await People(alex);
        var old = people.Single(p => p.Id == oldCole);
        var newCole = people.Single(p => p.Name == "Cole" && p.UserId != null).Id;
        Assert.True(old.Archived);
        Assert.True(old.FormerMember);
        Assert.Null(old.UserId);
        Assert.Equal(newCole, old.SameAsId);
        Assert.Null(people.Single(p => p.Id == newCole).SameAsId);

        Assert.Equal(HttpStatusCode.Forbidden, (await cole.Post($"/api/expenses/people/{oldCole}/merge", new { intoPersonId = newCole })).StatusCode);
        var merged = await (await alex.Post($"/api/expenses/people/{oldCole}/merge", new { intoPersonId = newCole })).Read<MergeResultDto>();
        Assert.Equal(1, merged.Expenses);
        Assert.Equal("Cole", merged.Into);

        Assert.DoesNotContain(await People(alex), p => p.Id == oldCole);
        balance = (await (await alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances.Single();
        Assert.Equal(newCole, balance.PersonId);
        Assert.Equal(15m, balance.Outstanding);
        Assert.True(balance.IsMember);

        app.Push.Requests.Clear();
        (await alex.Post("/api/expenses/remind", new { personId = newCole, owedToPersonId = a })).EnsureSuccessStatusCode();
        Assert.Single(app.Push.Requests, r => r.Request.RequestUri!.ToString() == phone.Endpoint);
    }

    [Fact]
    public async Task Merging_combines_splits_and_shares_and_nobody_owes_themselves()
    {
        using var app = new TestApp();
        var alex = await app.SetupClientAsync();
        (await alex.Post("/api/household/users", new { name = "Sam", username = "sam", password = "password123" })).EnsureSuccessStatusCode();
        var people = await People(alex);
        int a = people.Single(p => p.Name == "Tester").Id, s = people.Single(p => p.Name == "Sam").Id;
        var pat = await (await alex.Post("/api/expenses/people", new { name = "sam " })).Read<PersonDto>(); // added by name before joining

        // Both in one split: Alex paid 30, three ways.
        var both = await (await Expense(alex, app, "Pizza", 30m, a, Part(a), Part(s), Part(pat.Id))).Read<ReceiptDto>();
        // "sam " paid, and Sam owes "sam ": once merged, Sam doesn't owe himself.
        var paidByPat = await (await Expense(alex, app, "Movie", 20m, pat.Id, Part(s), Part(pat.Id))).Read<ReceiptDto>();
        var rent = await (await alex.Post("/api/expenses/recurring", new
        {
            name = "Rent", category = "housing", amount = 1000m, frequency = "Monthly", nextDate = app.Today.AddDays(5), paidByPersonId = pat.Id,
            split = new[] { Part(a), Part(pat.Id) },
        })).Read<RecurringDto>();

        Assert.Equal(s, (await People(alex)).Single(p => p.Id == pat.Id).SameAsId);
        var merged = await (await alex.Post($"/api/expenses/people/{pat.Id}/merge", new { intoPersonId = s })).Read<MergeResultDto>();
        Assert.Equal(3, merged.Expenses);

        var pizza = await (await alex.GetAsync($"/api/expenses/receipts/{both.Id}")).Read<ReceiptDto>();
        Assert.Equal(2, pizza.Split.Count);
        Assert.Equal(2, pizza.Split.Single(p => p.PersonId == s).Weight);
        Assert.Equal(20m, Assert.Single(pizza.Shares).Amount);

        var movie = await (await alex.GetAsync($"/api/expenses/receipts/{paidByPat.Id}")).Read<ReceiptDto>();
        Assert.Equal(s, movie.PaidByPersonId);
        Assert.Empty(movie.Shares);

        var recurring = (await (await alex.GetAsync("/api/expenses/recurring")).Read<List<RecurringDto>>()).Single(r => r.Id == rent.Id);
        Assert.Equal(s, recurring.PaidByPersonId);
        Assert.Equal([a, s], recurring.Split.Select(p => p.PersonId).Order());
    }

    [Fact]
    public async Task Members_cant_be_merged_away_and_a_wrong_suggestion_can_be_dismissed()
    {
        using var app = new TestApp();
        var alex = await app.SetupClientAsync();
        (await alex.Post("/api/household/users", new { name = "Sam", username = "sam", password = "password123" })).EnsureSuccessStatusCode();
        var people = await People(alex);
        int a = people.Single(p => p.Name == "Tester").Id, s = people.Single(p => p.Name == "Sam").Id;
        var otherSam = await (await alex.Post("/api/expenses/people", new { name = "Sam" })).Read<PersonDto>();

        Assert.Equal(HttpStatusCode.BadRequest, (await alex.Post($"/api/expenses/people/{s}/merge", new { intoPersonId = a })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.Post($"/api/expenses/people/{otherSam.Id}/merge", new { intoPersonId = otherSam.Id })).StatusCode);

        Assert.Equal(s, (await People(alex)).Single(p => p.Id == otherSam.Id).SameAsId);
        (await alex.Post($"/api/expenses/people/{otherSam.Id}/not-same", new { })).EnsureSuccessStatusCode();
        Assert.Null((await People(alex)).Single(p => p.Id == otherSam.Id).SameAsId);
    }
}
