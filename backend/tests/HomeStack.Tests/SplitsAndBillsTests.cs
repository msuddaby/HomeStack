using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HomeStack.Core.Auth;
using HomeStack.Modules.Expenses;

namespace HomeStack.Tests;

public class SplitsAndBillsTests
{
    private static string OpenAiReply(object content) => new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = JsonSerializer.Serialize(content) },
            ["finish_reason"] = "stop",
        }),
    }.ToJsonString();

    /// <summary>The set-up admin (Alex) plus Sam and Jo as members, each with their own signed-in client.</summary>
    private static async Task<(HttpClient Alex, HttpClient Sam, List<PersonDto> People)> Household(TestApp app)
    {
        var alex = await app.SetupClientAsync();
        foreach (var (name, user) in new[] { ("Sam", "sam"), ("Jo", "jo") })
            (await alex.Post("/api/household/users", new { name, username = user, password = "password123" })).EnsureSuccessStatusCode();
        var sam = app.CreateClient();
        (await sam.Post("/api/auth/login", new { username = "sam", password = "password123" })).EnsureSuccessStatusCode();
        return (alex, sam, await (await alex.GetAsync("/api/expenses/people")).Read<List<PersonDto>>());
    }

    private static int Id(List<PersonDto> people, string name) => people.Single(p => p.Name == name).Id;
    private static object Part(int personId, double weight = 1) => new { personId, weight };

    /// <summary>A tiny one-page PDF with some text.</summary>
    internal static byte[] Pdf()
    {
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 200] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length 44 >>\nstream\nBT /F1 18 Tf 20 100 Td (Enmax $142.37) Tj ET\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
            sb.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(sb.ToString());
        sb.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    internal static MultipartFormDataContent Upload(byte[] data, string field, string name, string type)
    {
        var file = new ByteArrayContent(data);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { file, field, name } };
    }

    [Fact]
    public async Task The_set_up_member_is_admin_and_admins_manage_admins()
    {
        using var app = new TestApp();
        var (alex, sam, _) = await Household(app);
        var users = await (await alex.GetAsync("/api/household/users")).Read<List<UserDto>>();
        var alexUser = users.Single(u => u.Name == "Tester");
        var samUser = users.Single(u => u.Name == "Sam");
        Assert.True(alexUser.IsAdmin);
        Assert.False(samUser.IsAdmin);

        Assert.Equal(HttpStatusCode.Forbidden, (await sam.PutAsJsonAsync($"/api/household/users/{samUser.Id}/admin", new { isAdmin = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PutAsJsonAsync($"/api/household/users/{alexUser.Id}/admin", new { isAdmin = false })).StatusCode);

        Assert.True((await (await alex.PutAsJsonAsync($"/api/household/users/{samUser.Id}/admin", new { isAdmin = true })).Read<UserDto>()).IsAdmin);
        Assert.True((await (await sam.GetAsync("/api/auth/me")).Read<MeDto>()).User.IsAdmin);
        // Now there are two, Alex can step down; Sam is left and can't be removed.
        (await alex.PutAsJsonAsync($"/api/household/users/{alexUser.Id}/admin", new { isAdmin = false })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.DeleteAsync($"/api/household/users/{samUser.Id}")).StatusCode);
    }

    [Fact]
    public async Task People_include_members_and_outside_people()
    {
        using var app = new TestApp();
        var (alex, sam, people) = await Household(app);
        Assert.Equal(["Tester", "Jo", "Sam"], people.Where(p => p.UserId != null).Select(p => p.Name).OrderBy(n => n != "Tester").ThenBy(n => n));
        Assert.True(people.Single(p => p.Name == "Tester").IsMe);
        Assert.True((await (await sam.GetAsync("/api/expenses/people")).Read<List<PersonDto>>()).Single(p => p.Name == "Sam").IsMe);

        var pat = await (await alex.Post("/api/expenses/people", new { name = "Pat (not on HomeStack)" })).Read<PersonDto>();
        Assert.Null(pat.UserId);
        Assert.Equal("Pat", (await (await alex.PutAsJsonAsync($"/api/expenses/people/{pat.Id}", new { name = "Pat" })).Read<PersonDto>()).Name);
        Assert.Equal(HttpStatusCode.BadRequest, (await alex.PutAsJsonAsync($"/api/expenses/people/{Id(people, "Sam")}", new { name = "X" })).StatusCode);
        (await alex.DeleteAsync($"/api/expenses/people/{pat.Id}")).EnsureSuccessStatusCode();
        Assert.True((await (await alex.GetAsync("/api/expenses/people")).Read<List<PersonDto>>()).Single(p => p.Id == pat.Id).Archived);
    }

    [Fact]
    public async Task Each_receipt_line_can_be_split_differently_and_tax_is_shared_in_proportion()
    {
        using var app = new TestApp();
        var (alex, sam, people) = await Household(app);
        int a = Id(people, "Tester"), s = Id(people, "Sam"), j = Id(people, "Jo");

        // Detergent for all three (the receipt's split), snacks for Alex and Sam, a shirt just for Jo. $3 tax on $30.
        var r = await (await alex.Post("/api/expenses/receipts", new
        {
            store = "Costco", purchasedAt = app.Today, total = 33m, tax = 3m,
            split = new[] { Part(a), Part(s), Part(j) },
            lines = new object[]
            {
                new { name = "Detergent", category = "household", quantity = 1, amount = 18m },
                new { name = "Chips", category = "snacks", quantity = 1, amount = 6m, split = new[] { Part(a), Part(s) } },
                new { name = "Shirt", category = "clothing", quantity = 1, amount = 6m, split = new[] { Part(j) } },
            },
        })).Read<ReceiptDto>();

        Assert.Equal(a, r.PaidByPersonId); // no payer given: whoever saved it
        var shares = r.Shares.ToDictionary(x => x.PersonId, x => x.Amount);
        Assert.Equal(9.90m, shares[s]); // (6 + 3) × 33/30
        Assert.Equal(13.20m, shares[j]); // (6 + 6) × 33/30
        Assert.False(shares.ContainsKey(a)); // the payer doesn't owe themselves
        // What each share is made of: their part of each line, before tax.
        Assert.Equal([("Detergent", 6m, 18m), ("Chips", 3m, 6m)], r.Shares.Single(x => x.PersonId == s).Lines.Select(l => (l.Name, l.Amount, l.LineAmount)));
        Assert.Equal([("Detergent", 6m), ("Shirt", 6m)], r.Shares.Single(x => x.PersonId == j).Lines.Select(l => (l.Name, l.Amount)));

        var balances = await (await alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>();
        Assert.True(balances.CanMarkReceived);
        var jo = balances.Balances.Single(b => b.PersonId == j);
        Assert.Equal((13.20m, "Tester"), (jo.Outstanding, jo.OwedTo));
        Assert.Equal("Costco", jo.Items.Single().What);

        // Only admins mark it received.
        var joShare = r.Shares.Single(x => x.PersonId == j).Id;
        Assert.False((await (await sam.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).CanMarkReceived);
        Assert.Equal(HttpStatusCode.Forbidden, (await sam.PutAsJsonAsync($"/api/expenses/shares/{joShare}/received", new { received = true })).StatusCode);
        (await alex.PutAsJsonAsync($"/api/expenses/shares/{joShare}/received", new { received = true })).EnsureSuccessStatusCode();
        jo = (await (await alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances.Single(b => b.PersonId == j);
        Assert.Equal(0m, jo.Outstanding);
        Assert.NotNull(jo.Items.Single().ReceivedAt); // still listed for a while, as received

        // Re-splitting later (the chips were Jo's too) keeps Jo's "received".
        var chips = r.Lines.Single(l => l.Name == "Chips").Id;
        var again = await (await alex.PutAsJsonAsync($"/api/expenses/receipts/{r.Id}/split", new
        {
            paidByPersonId = a, split = new[] { Part(a), Part(s), Part(j) }, lines = new[] { new { lineId = chips, split = new[] { Part(a), Part(s), Part(j) } } },
        }, TestApp.Json)).Read<ReceiptDto>();
        Assert.Equal(15.40m, again.Shares.Single(x => x.PersonId == j).Amount); // (6 + 2 + 6) × 1.1
        Assert.NotNull(again.Shares.Single(x => x.PersonId == j).ReceivedAt);
        Assert.Equal(8.80m, (await (await alex.GetAsync("/api/expenses/receipts")).Read<List<ReceiptSummaryDto>>()).Single().Outstanding); // Sam's (6+2)×1.1
    }

    [Fact]
    public async Task Custom_amounts_and_someone_else_paying()
    {
        using var app = new TestApp();
        var (alex, _, people) = await Household(app);
        int a = Id(people, "Tester"), s = Id(people, "Sam");
        var pat = await (await alex.Post("/api/expenses/people", new { name = "Pat" })).Read<PersonDto>();

        // Sam paid a $90 dinner: Alex had $50 worth, Pat (not on HomeStack) $40.
        var dinner = await (await alex.Post("/api/expenses/receipts/manual", new
        {
            purchasedAt = app.Today, amount = 90m, category = "dining", description = "Dinner",
            paidByPersonId = s, split = new[] { Part(a, 50), Part(pat.Id, 40) },
        })).Read<ReceiptDto>();
        Assert.Equal("Sam", dinner.PaidBy);
        Assert.Equal([(a, 50m), (pat.Id, 40m)], dinner.Shares.Select(x => (x.PersonId, x.Amount)).OrderBy(x => x.Amount != 50));
        var balances = (await (await alex.GetAsync("/api/expenses/balances")).Read<BalancesDto>()).Balances;
        Assert.All(balances, b => Assert.Equal("Sam", b.OwedTo));
        Assert.Equal("Dinner", balances.First().Items.Single().What);

        Assert.Equal(HttpStatusCode.BadRequest, (await alex.Post("/api/expenses/receipts/manual", new
        {
            purchasedAt = app.Today, amount = 10m, category = "dining", description = "X", split = new[] { Part(99999) },
        })).StatusCode);
    }

    [Fact]
    public async Task Fixed_recurring_payments_are_recorded_on_the_day_with_their_split()
    {
        using var app = new TestApp();
        var (alex, _, people) = await Household(app);
        int a = Id(people, "Tester"), s = Id(people, "Sam"), j = Id(people, "Jo");
        var start = app.Today.AddMonths(-1).AddDays(-3);

        // Rent started a month and three days ago: last month's and this month's get recorded straight away.
        var rent = await (await alex.Post("/api/expenses/recurring", new
        {
            name = "Rent", payee = "Landlord", category = "housing", amount = 1500m, frequency = "monthly", nextDate = start,
            paidByPersonId = a, split = new[] { Part(a, 600), Part(s, 450), Part(j, 450) },
        })).Read<RecurringDto>();
        Assert.Equal(BillsService.NextAfter(start.AddMonths(1), Frequency.Monthly, start.Day), rent.NextDate);
        Assert.Equal(rent.NextDate.AddMonths(-1), rent.LastDate);

        var receipts = await (await alex.GetAsync("/api/expenses/receipts")).Read<List<ReceiptSummaryDto>>();
        Assert.Equal(2, receipts.Count);
        Assert.All(receipts, r => Assert.Equal(ReceiptSource.Recurring, r.Source));
        Assert.All(receipts, r => Assert.Equal("Landlord", r.Store));
        var one = await (await alex.GetAsync($"/api/expenses/receipts/{receipts[0].Id}")).Read<ReceiptDto>();
        Assert.Equal([450m, 450m], one.Shares.Select(x => x.Amount));

        // Running the job again records nothing new until next month.
        using var scope = app.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<HomeStack.Core.HouseholdScope>().Enter(1);
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<BillsService>().PostDueAsync());
        app.Time.Now = app.Time.Now.AddMonths(1);
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<BillsService>().PostDueAsync());
    }

    [Theory]
    [InlineData(2026, 1, 31, 2026, 2, 28)] // the 31st lands on the last day of shorter months
    [InlineData(2026, 2, 28, 2026, 3, 31)] // and back on the 31st after
    public void Monthly_dates_keep_their_day(int y, int m, int d, int ny, int nm, int nd) =>
        Assert.Equal(new DateOnly(ny, nm, nd), BillsService.NextAfter(new DateOnly(y, m, d), Frequency.Monthly, 31));

    [Fact]
    public async Task Expected_bills_show_as_missing_until_uploaded()
    {
        using var app = new TestApp();
        var (alex, _, people) = await Household(app);
        int a = Id(people, "Tester"), s = Id(people, "Sam");
        (await alex.PutAsJsonAsync("/api/ai/settings", new { provider = "openAiCompatible", baseUrl = "https://api.deepseek.com", apiKey = "k", model = "m" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        var expectedOn = app.Today.AddDays(-10);
        var enmax = await (await alex.Post("/api/expenses/recurring", new
        {
            name = "Electricity", payee = "Enmax", category = "utilities", amount = (decimal?)null, frequency = "monthly", nextDate = expectedOn,
            split = new[] { Part(a), Part(s) },
        })).Read<RecurringDto>();
        Assert.True(enmax.Expected);
        Assert.Empty(await (await alex.GetAsync("/api/expenses/receipts")).Read<List<ReceiptSummaryDto>>()); // nothing recorded by itself

        var missing = (await (await alex.GetAsync("/api/expenses/upcoming")).Read<List<UpcomingDto>>()).Single();
        Assert.Equal((UpcomingKind.Expected, true), (missing.Kind, missing.Missing));

        // Upload the PDF: it's turned into an image for the AI, and matched to the expected bill.
        app.Ai.Bodies.Enqueue(OpenAiReply(new
        {
            payee = "ENMAX", accountNumber = "1234", statementDate = expectedOn.ToString("yyyy-MM-dd"), dueDate = app.Today.AddDays(2).ToString("yyyy-MM-dd"),
            amountDue = 142.37, category = "utilities",
        }));
        var draft = await (await alex.PostAsync("/api/expenses/bills/read", Upload(Pdf(), "files", "enmax.pdf", "application/pdf"))).Read<BillDraftDto>();
        Assert.Contains("image_url", app.Ai.Requests[^1].Body);
        Assert.Equal((142.37m, "utilities", "1234"), (draft.Amount, draft.Category, draft.Account));
        Assert.Equal(enmax.Id, draft.Recurring?.Id);
        Assert.Equal(2, draft.Recurring!.Split.Count);

        var bill = await (await alex.Post("/api/expenses/bills", new
        {
            payee = "Enmax", statementDate = draft.StatementDate, dueDate = draft.DueDate, amount = 142.37m, category = "utilities",
            split = draft.Recurring.Split, recurringId = enmax.Id,
        })).Read<ReceiptDto>();
        Assert.Equal(71.19m, bill.Shares.Single().Amount); // 142.37 / 2, rounded

        var withPdf = await (await alex.PostAsync($"/api/expenses/receipts/{bill.Id}/document", Upload(Pdf(), "file", "enmax.pdf", "application/pdf"))).Read<ReceiptDto>();
        var doc = await alex.GetAsync(withPdf.Document);
        Assert.Equal("application/pdf", doc.Content.Headers.ContentType?.MediaType);
        Assert.Equal("inline", doc.Content.Headers.ContentDisposition?.DispositionType);

        // No longer missing; instead the bill is due in two days.
        var upcoming = await (await alex.GetAsync("/api/expenses/upcoming")).Read<List<UpcomingDto>>();
        var due = upcoming.Single(u => u.Kind == UpcomingKind.Bill);
        Assert.Equal((bill.Id, false), (due.ReceiptId, due.Overdue));
        Assert.DoesNotContain(upcoming, u => u.Missing);
        Assert.Equal(expectedOn.AddMonths(1), (await (await alex.GetAsync("/api/expenses/recurring")).Read<List<RecurringDto>>()).Single().NextDate);

        // Paid to the company: off the list. Deleting the bill deletes its PDF.
        await alex.PutAsJsonAsync($"/api/expenses/receipts/{bill.Id}/bill-paid", new { paidOn = app.Today }, TestApp.Json);
        Assert.DoesNotContain(await (await alex.GetAsync("/api/expenses/upcoming")).Read<List<UpcomingDto>>(), u => u.Kind == UpcomingKind.Bill);
        (await alex.DeleteAsync($"/api/expenses/receipts/{bill.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await alex.GetAsync(withPdf.Document)).StatusCode);
    }

    [Fact]
    public async Task Unreadable_bill_files_are_refused()
    {
        using var app = new TestApp();
        var (alex, _, _) = await Household(app);
        (await alex.PutAsJsonAsync("/api/ai/settings", new { provider = "openAiCompatible", baseUrl = "https://api.deepseek.com", apiKey = "k", model = "m" }, TestApp.Json))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alex.PostAsync("/api/expenses/bills/read", Upload("%PDF-1.4 broken"u8.ToArray(), "files", "x.pdf", "application/pdf"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await alex.PostAsync("/api/expenses/bills/read", Upload("hello"u8.ToArray(), "files", "x.txt", "text/plain"))).StatusCode);
        Assert.Empty(app.Ai.Requests);
    }
}
