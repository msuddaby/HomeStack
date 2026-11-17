using System.Text;
using HomeStack.Core;
using HomeStack.Core.Ai;
using HomeStack.Core.Images;
using HomeStack.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeStack.Modules.Expenses;

/// <summary>
/// Bills (uploaded as a PDF or photo, read by AI), fixed recurring payments (recorded automatically on the day) and
/// expected bills (recurring, amount varies: missing until uploaded).
/// </summary>
public class BillsService(HomeStackDbContext db, ICurrentUser me, TimeProvider time, AiService ai, ExpensesService expenses, PeopleService people,
    ExpenseNotifier notifier)
{
    public const int MaxImages = 4;
    public const long MaxFileBytes = 15 * 1024 * 1024;
    /// <summary>An expected bill counts as missing this many days after its expected date.</summary>
    public const int GraceDays = 5;

    private int Hid => me.HouseholdId;
    private IQueryable<Recurring> Recurrings => db.Set<Recurring>().Where(r => r.HouseholdId == Hid);
    private IQueryable<Receipt> Receipts => db.Set<Receipt>().Where(r => r.HouseholdId == Hid);

    // ---------- Reading a bill ----------

    private record Reply(string? Payee, string? AccountNumber, string? StatementDate, string? DueDate, string? PeriodStart, string? PeriodEnd,
        decimal? AmountDue, string? Category, string? Note);

    /// <summary>Reads one PDF (its first pages) or up to four photos of a bill into a draft to review. Nothing is saved.</summary>
    public async Task<BillDraftDto> ReadAsync(IReadOnlyList<(byte[] Data, string ContentType)> files, CancellationToken ct)
    {
        var images = new List<AiImage>();
        foreach (var (data, type) in files)
        {
            if (Pdf.Looks(data)) images.AddRange(Pdf.RenderPages(data).Select(p => new AiImage(p, "image/jpeg")));
            else images.Add(new AiImage(data, type));
        }
        if (images.Count == 0) throw new BadRequestException("Choose a PDF or photo of the bill.");
        images = images.Take(MaxImages).ToList();

        var user = (images.Count > 1 ? $"These {images.Count} images are one bill, in page order." : "This image is a bill.") + "\n" + Shape;
        var reply = await ai.JsonAsync<Reply>(new AiPrompt(await SystemPromptAsync(), user, images, MaxTokens: 4096), ct);
        return await ToDraftAsync(reply);
    }

    private const string Shape = """
        Reply with JSON only, in this shape:
        {"payee": "Enmax", "accountNumber": "1234", "statementDate": "YYYY-MM-DD", "dueDate": "YYYY-MM-DD",
         "periodStart": "YYYY-MM-DD", "periodEnd": "YYYY-MM-DD", "amountDue": 142.37, "category": "utilities", "note": null}
        """;

    private async Task<string> SystemPromptAsync()
    {
        var sb = new StringBuilder("""
            You read household bills (electricity, gas, water, phone, internet, insurance, rent statements, subscriptions)
            for a household's expense tracker.

            Rules:
            - "payee": the company's short name ("Enmax", "Telus", "City of Calgary"). If it's one of the payees listed
              below, use that exact name.
            - "accountNumber": the last 4 characters of the account number only, or null.
            - "statementDate": the bill or statement date. "dueDate": when payment is due. "periodStart"/"periodEnd": the
              service period it covers. Use null for anything not printed. Dates as YYYY-MM-DD.
            - "amountDue": the total amount to pay by the due date, as a number. If the account is in credit or nothing is
              due, use 0 and say so in "note".
            - "category": exactly one id from the list below.
            - "note": anything worth knowing in a few words (e.g. "includes $40 overdue balance"), else null.
            - Never invent values. If the image isn't a bill, set everything to null and explain in "note".

            Category ids:
            """);
        sb.AppendLine();
        foreach (var c in ExpenseCategories.All) sb.AppendLine($"- {c.Id}: {c.Label}");
        var payees = await Recurrings.Select(r => r.Payee ?? r.Name).Distinct().ToListAsync();
        payees.AddRange(await expenses.GetStoreNamesAsync());
        if (payees.Count > 0)
            sb.AppendLine().AppendLine("Payees this household already uses: " + string.Join("; ", payees.Distinct(StringComparer.OrdinalIgnoreCase).Take(200)));
        return sb.ToString();
    }

    private async Task<BillDraftDto> ToDraftAsync(Reply reply)
    {
        var today = time.Today();
        DateOnly? Date(string? s) => DateOnly.TryParse(s, out var d) && d >= today.AddYears(-2) && d <= today.AddYears(1) ? d : null;
        var payee = Clean(reply.Payee, 80);
        var statement = Date(reply.StatementDate);
        var amount = reply.AmountDue is { } a && a >= 0 && a < 1_000_000 ? Math.Round(a, 2) : (decimal?)null;
        var category = ExpenseCategories.Exists(reply.Category) ? reply.Category! : "utilities";

        // The expected bill (or recurring payment) from the same payee, if any: its split is the starting point.
        RecurringRefDto? recurring = null;
        if (payee is not null)
        {
            var p = payee.ToLower();
            var match = await Recurrings.Include(r => r.Split).Where(r => r.Active && ((r.Payee ?? r.Name).ToLower() == p || r.Name.ToLower() == p))
                .OrderBy(r => r.Amount != null).FirstOrDefaultAsync();
            if (match is not null) recurring = new RecurringRefDto(match.Id, match.Name, match.PaidByPersonId, SplitMath.Dtos(match.Split));
        }

        int? duplicate = null;
        if (payee is not null && statement is not null && amount is not null)
        {
            var store = await expenses.FindStoreAsync(payee);
            duplicate = store is null ? null : (await Receipts.Where(r => r.Source == ReceiptSource.Bill && r.StoreId == store.Id && r.PurchasedAt == statement)
                .Select(r => new { r.Id, r.Total }).ToListAsync()).FirstOrDefault(r => r.Total == amount)?.Id;
        }

        return new BillDraftDto(payee, statement, Date(reply.DueDate), amount, category, Clean(reply.AccountNumber, 20),
            Date(reply.PeriodStart), Date(reply.PeriodEnd), Clean(reply.Note, 300), recurring, duplicate);
    }

    // ---------- Saving a bill ----------

    public async Task<ReceiptDto> SaveAsync(SaveBillRequest req)
    {
        var payer = await expenses.PayerAsync(req.PaidByPersonId, req.Split is { Count: > 0 }, req.Split);
        Recurring? recurring = null;
        if (req.RecurringId is int rid)
            recurring = await Recurrings.SingleOrDefaultAsync(r => r.Id == rid) ?? throw new BadRequestException("Unknown recurring payment");

        await using var tx = await db.Database.BeginTransactionAsync();
        var store = await expenses.GetOrCreateStoreAsync(req.Payee);
        var receipt = new Receipt
        {
            HouseholdId = Hid, StoreId = store?.Id, PurchasedAt = req.StatementDate, DueDate = req.DueDate, BillPaidOn = req.PaidOn,
            Total = req.Amount, Note = Clean(req.Note, 500), Source = ReceiptSource.Bill, UserId = me.UserId, RecurringId = recurring?.Id,
            PaidByPersonId = payer, Split = SplitMath.Parts(req.Split),
            Lines = [new ReceiptLine { Name = Clean(req.Description, 120) ?? $"{req.Payee.Trim()} bill", Category = req.Category, Amount = req.Amount }],
        };
        var owed = SplitMath.ApplyShares(receipt);
        db.Add(receipt);
        // This period's bill is in: expect the next one a period later.
        if (recurring is not null && recurring.Amount is null)
        {
            var next = NextAfter(req.StatementDate, recurring.Frequency, req.StatementDate.Day);
            if (next > recurring.NextDate) (recurring.NextDate, recurring.DayOfMonth) = (next, req.StatementDate.Day);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.OwedAsync([(receipt, owed)]);
        return await expenses.GetReceiptAsync(receipt.Id);
    }

    // ---------- Recurring payments and expected bills ----------

    /// <summary>The next date after <paramref name="d"/>; monthly and yearly ones land on <paramref name="dayOfMonth"/> (or the month's last day).</summary>
    public static DateOnly NextAfter(DateOnly d, Frequency f, int dayOfMonth) => f switch
    {
        Frequency.Weekly => d.AddDays(7),
        Frequency.Biweekly => d.AddDays(14),
        Frequency.Yearly => OnDay(d.AddYears(1), dayOfMonth),
        _ => OnDay(d.AddMonths(1), dayOfMonth),
    };

    private static DateOnly OnDay(DateOnly month, int day) =>
        new(month.Year, month.Month, Math.Clamp(day, 1, DateTime.DaysInMonth(month.Year, month.Month)));

    public async Task<List<RecurringDto>> ListRecurringAsync()
    {
        var list = await Recurrings.Include(r => r.Split).OrderBy(r => !r.Active).ThenBy(r => r.NextDate).ToListAsync();
        var ids = list.Select(r => r.Id).ToList();
        var last = (await Receipts.Where(r => r.RecurringId != null && ids.Contains(r.RecurringId.Value))
                .Select(r => new { RecurringId = r.RecurringId!.Value, r.Id, r.PurchasedAt, r.Total }).ToListAsync())
            .GroupBy(r => r.RecurringId).ToDictionary(g => g.Key, g => g.MaxBy(r => (r.PurchasedAt, r.Id))!);
        var names = await people.NamesAsync();
        return list.Select(r =>
        {
            var l = last.GetValueOrDefault(r.Id);
            return new RecurringDto(r.Id, r.Name, r.Payee, r.Category, r.Amount, r.Frequency, r.NextDate, r.PaidByPersonId,
                r.PaidByPersonId is int p ? names.GetValueOrDefault(p) : null, SplitMath.Dtos(r.Split), r.Active, r.Note, r.Amount is null,
                l?.Id, l?.PurchasedAt, l?.Total);
        }).ToList();
    }

    public async Task<RecurringDto> CreateRecurringAsync(RecurringRequest req)
    {
        var r = new Recurring { HouseholdId = Hid, Name = req.Name.Trim() };
        await ApplyAsync(r, req);
        db.Add(r);
        await db.SaveChangesAsync();
        await PostDueAsync(); // rent "from today" is recorded straight away
        return (await ListRecurringAsync()).Single(x => x.Id == r.Id);
    }

    public async Task<RecurringDto> UpdateRecurringAsync(int id, RecurringRequest req)
    {
        var r = await Recurrings.Include(x => x.Split).SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Recurring payment not found");
        await ApplyAsync(r, req);
        await db.SaveChangesAsync();
        await PostDueAsync();
        return (await ListRecurringAsync()).Single(x => x.Id == id);
    }

    private async Task ApplyAsync(Recurring r, RecurringRequest req)
    {
        r.PaidByPersonId = await expenses.PayerAsync(req.PaidByPersonId, req.Split is { Count: > 0 }, req.Split);
        r.Name = req.Name.Trim();
        r.Payee = Clean(req.Payee, 80);
        r.Category = req.Category;
        r.Amount = req.Amount;
        r.Frequency = req.Frequency;
        if (r.NextDate != req.NextDate) (r.NextDate, r.DayOfMonth) = (req.NextDate, req.NextDate.Day);
        if (r.DayOfMonth == 0) r.DayOfMonth = req.NextDate.Day;
        r.Active = req.Active;
        r.Note = Clean(req.Note, 500);
        r.Split = SplitMath.Parts(req.Split);
    }

    /// <summary>Stops it; expenses already recorded for it stay.</summary>
    public async Task DeleteRecurringAsync(int id)
    {
        var r = await Recurrings.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Recurring payment not found");
        db.Remove(r);
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Records every fixed payment that's come due (each on its own date, catching up after downtime, at most a year's worth),
    /// with its split. Returns how many were recorded.
    /// </summary>
    public async Task<int> PostDueAsync(CancellationToken ct = default)
    {
        var today = time.Today();
        var due = await Recurrings.Include(r => r.Split).Where(r => r.Active && r.Amount != null && r.NextDate <= today).ToListAsync(ct);
        var posted = new List<(Receipt, IReadOnlyList<Share>)>();
        foreach (var r in due)
        {
            var store = await expenses.GetOrCreateStoreAsync(r.Payee);
            for (var n = 0; r.NextDate <= today && n < 53; n++)
            {
                var receipt = new Receipt
                {
                    HouseholdId = Hid, StoreId = store?.Id, PurchasedAt = r.NextDate, Total = r.Amount!.Value, Source = ReceiptSource.Recurring,
                    UserId = me.UserId, RecurringId = r.Id, Note = r.Note, PaidByPersonId = r.PaidByPersonId,
                    Split = r.Split.Select(p => new SplitPart { PersonId = p.PersonId, Weight = p.Weight }).ToList(),
                    Lines = [new ReceiptLine { Name = r.Name, Category = r.Category, Amount = r.Amount.Value }],
                };
                posted.Add((receipt, SplitMath.ApplyShares(receipt)));
                db.Add(receipt);
                r.NextDate = NextAfter(r.NextDate, r.Frequency, r.DayOfMonth);
            }
        }
        await db.SaveChangesAsync(ct);
        await notifier.OwedAsync(posted, ct);
        return posted.Count;
    }

    /// <summary>Unpaid bills due, fixed payments coming up and expected bills, within <paramref name="days"/> days (plus anything overdue).</summary>
    public async Task<List<UpcomingDto>> UpcomingAsync(int days = 30)
    {
        var today = time.Today();
        var until = today.AddDays(days);
        var items = new List<UpcomingDto>();

        var bills = await Receipts.Where(r => r.Source == ReceiptSource.Bill && r.BillPaidOn == null && r.DueDate != null && r.DueDate <= until)
            .Select(r => new { r.Id, Name = r.Store!.Name, First = r.Lines.Select(l => l.Name).FirstOrDefault(), r.DueDate, r.Total, r.RecurringId }).ToListAsync();
        items.AddRange(bills.Select(b => new UpcomingDto(UpcomingKind.Bill, b.DueDate!.Value, b.Name ?? b.First ?? "Bill", b.Total, b.Id, b.RecurringId,
            b.DueDate < today, false)));

        foreach (var r in await Recurrings.Where(r => r.Active && r.NextDate <= until).ToListAsync())
        {
            var expected = r.Amount is null;
            var missing = expected && r.NextDate.AddDays(GraceDays) < today;
            items.Add(new UpcomingDto(expected ? UpcomingKind.Expected : UpcomingKind.Recurring, r.NextDate, r.Name, r.Amount, null, r.Id,
                r.NextDate < today, missing));
        }
        return items.OrderBy(i => i.Date).ThenBy(i => i.Name).ToList();
    }

    private static string? Clean(string? s, int max)
    {
        if (string.IsNullOrWhiteSpace(s) || s.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)) return null;
        var t = s.Trim();
        return t.Length <= max ? t : t[..max];
    }
}

/// <summary>
/// For every household: records fixed recurring payments on their day, and (from the household's reminder hour,
/// so nobody's woken at midnight) tells members about bills that have just gone overdue.
/// </summary>
internal sealed class RecurringWorker(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<RecurringWorker> logger)
    : PeriodicWorker(scopes, config, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task TickAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<HomeStackDbContext>();
        var households = await db.Households.Select(h => h.Id).ToListAsync(ct);
        var hours = await db.Set<NotificationSettings>().ToDictionaryAsync(s => s.HouseholdId, s => s.DigestHour, ct);
        var hour = time.GetLocalNow().Hour;
        foreach (var id in households)
        {
            await using var scope = Scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<HouseholdScope>().Enter(id);
            var posted = await scope.ServiceProvider.GetRequiredService<BillsService>().PostDueAsync(ct);
            if (posted > 0) logger.LogInformation("Recorded {Count} recurring payments for household {Household}", posted, id);
            if (hour >= hours.GetValueOrDefault(id, 8))
                await scope.ServiceProvider.GetRequiredService<ExpenseNotifier>().OverdueBillsAsync(ct);
        }
    }
}

/// <summary>Daily digest: bills due soon or overdue, and expected bills that haven't been uploaded.</summary>
public class BillsDigest(BillsService bills, TimeProvider time) : IDigestSource
{
    public int Order => 35;

    public async Task<IReadOnlyList<DigestSection>> GetSectionsAsync(CancellationToken ct)
    {
        var today = time.Today();
        var soon = (await bills.UpcomingAsync(3)).Where(i => i.Kind == UpcomingKind.Bill || i.Missing).ToList();
        var lines = soon.Select(i => i switch
        {
            { Missing: true } => $"{i.Name}: expected around {i.Date:MMM d}, not uploaded yet",
            { Overdue: true } => $"{i.Name} ${i.Amount:0.00}: overdue since {i.Date:MMM d}",
            _ => $"{i.Name} ${i.Amount:0.00}: due {(i.Date == today ? "today" : i.Date == today.AddDays(1) ? "tomorrow" : i.Date.ToString("ddd MMM d"))}",
        }).ToList();
        return [new DigestSection("Bills", lines, Urgent: soon.Any(i => i.Overdue && i.Kind == UpcomingKind.Bill))];
    }
}
