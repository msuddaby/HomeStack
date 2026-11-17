using HomeStack.Core;
using HomeStack.Core.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HomeStack.Modules.Expenses;

/// <summary>
/// Notifications on members' devices about money: "you owe" when someone splits an expense with you, overdue bills,
/// and an admin's reminder to someone who hasn't paid back. Sending never fails the change that caused it.
/// </summary>
public class ExpenseNotifier(HomeStackDbContext db, ICurrentUser me, PushService push, TimeProvider time, ILogger<ExpenseNotifier> logger)
{
    /// <summary>How long after a reminder before the same debt can be reminded again.</summary>
    public static readonly TimeSpan RemindEvery = TimeSpan.FromHours(1);
    private const int OverdueLookbackDays = 7;

    private int Hid => me.HouseholdId;

    /// <summary>
    /// Tells each member who now owes more (a new share, or a bigger one) — except whoever made the change.
    /// Several expenses at once (recurring payments catching up) make one notification per person.
    /// </summary>
    public async Task OwedAsync(IEnumerable<(Receipt Receipt, IReadOnlyList<Share> Shares)> changes, CancellationToken ct = default)
    {
        try
        {
            var items = changes.SelectMany(c => c.Shares.Where(s => s.ReceivedAt == null).Select(s => (c.Receipt, Share: s))).ToList();
            if (items.Count == 0) return;
            var people = await db.Set<Person>().Where(p => p.HouseholdId == Hid).ToDictionaryAsync(p => p.Id, ct);
            var actor = me.UserId == 0 ? null : await db.Users.Where(u => u.Id == me.UserId).Select(u => u.Name).SingleOrDefaultAsync(ct);
            var stores = await StoreNamesAsync(items.Select(i => i.Receipt), ct);

            foreach (var group in items.GroupBy(i => (i.Share.PersonId, Payer: i.Receipt.PaidByPersonId!.Value)))
            {
                if (people.GetValueOrDefault(group.Key.PersonId)?.UserId is not int userId || userId == me.UserId) continue;
                var payer = people.GetValueOrDefault(group.Key.Payer)?.Name ?? "someone";
                var list = group.ToList();
                var total = list.Sum(i => i.Share.Amount);
                string body;
                if (list.Count == 1)
                {
                    var (r, s) = list[0];
                    var what = What(r, stores);
                    var lines = r.Lines.Count > 1 ? SplitMath.Breakdown(r).GetValueOrDefault(s.PersonId) : null;
                    body = $"{what} · {r.PurchasedAt:MMM d}";
                    if (lines is { Count: > 0 }) body += $"\nFor {Names(lines.Select(l => l.Name), 3)}";
                }
                else body = Names(list.Select(i => $"{What(i.Receipt, stores)} {Money(i.Share.Amount)}"), 4);
                if (actor is not null) body += $"\nAdded by {actor}";
                var url = list.Count == 1 ? $"/expenses/receipts/{list[0].Receipt.Id}" : "/expenses/owed";
                await push.SendToUsersAsync([userId], new PushMessage($"You owe {payer} {Money(total)}", body, url,
                    list.Count == 1 ? $"share-{list[0].Share.Id}" : null), ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Couldn't send 'you owe' notifications");
        }
    }

    /// <summary>
    /// Once per bill, the first time it's seen unpaid after its due date: tells whoever pays it (everyone, if that's
    /// not a member). Bills overdue for more than a week by the time this first runs are left to the daily reminder.
    /// </summary>
    public async Task<int> OverdueBillsAsync(CancellationToken ct = default)
    {
        var today = time.Today();
        var bills = await db.Set<Receipt>().Include(r => r.Store).Include(r => r.Lines)
            .Where(r => r.HouseholdId == Hid && r.Source == ReceiptSource.Bill && r.BillPaidOn == null && r.OverdueNotifiedOn == null
                && r.DueDate < today && r.DueDate >= today.AddDays(-OverdueLookbackDays))
            .ToListAsync(ct);
        if (bills.Count == 0) return 0;
        var members = await db.Set<Person>().Where(p => p.HouseholdId == Hid && p.UserId != null).ToDictionaryAsync(p => p.Id, p => p.UserId!.Value, ct);
        foreach (var b in bills)
        {
            b.OverdueNotifiedOn = today;
            var message = new PushMessage($"Overdue: {b.Store?.Name ?? What(b, null)} {Money(b.Total)}",
                $"It was due {b.DueDate:MMM d}. Paid it already? Open it to mark it paid.", $"/expenses/receipts/{b.Id}", $"bill-{b.Id}", Urgent: true);
            try
            {
                if (b.PaidByPersonId is int payer && members.TryGetValue(payer, out var userId)) await push.SendToUsersAsync([userId], message, ct);
                else await push.SendToHouseholdAsync(message, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Couldn't send the overdue notice for bill {Bill}", b.Id);
            }
        }
        await db.SaveChangesAsync(ct);
        return bills.Count;
    }

    /// <summary>
    /// An admin nudges someone who hasn't paid back: everything they still owe one person, or just one expense.
    /// Throws if they can't get notifications or were reminded within the last hour.
    /// </summary>
    public async Task<RemindResultDto> RemindAsync(RemindRequest req, CancellationToken ct = default)
    {
        await me.RequireAdminAsync(db);
        var people = await db.Set<Person>().Where(p => p.HouseholdId == Hid).ToDictionaryAsync(p => p.Id, ct);
        var person = people.GetValueOrDefault(req.PersonId) ?? throw new NotFoundException("Person not found");
        var payer = people.GetValueOrDefault(req.OwedToPersonId)?.Name ?? throw new NotFoundException("Person not found");
        if (person.FormerMember || (person.UserId is int linked && !await db.Users.AnyAsync(u => u.Id == linked && u.HouseholdId == Hid, ct)))
            throw new BadRequestException($"{person.Name} is no longer a household member, so they can't get notifications. " +
                "If they've been added again, merge them in Expenses settings.");
        if (person.UserId is not int userId) throw new BadRequestException($"{person.Name} isn't on HomeStack, so they can't get notifications.");

        var owed = await (from r in db.Set<Receipt>().Include(r => r.Store)
                          where r.HouseholdId == Hid && r.PaidByPersonId == req.OwedToPersonId
                          from s in r.Shares
                          where s.PersonId == req.PersonId && s.ReceivedAt == null && (req.ShareId == null || s.Id == req.ShareId)
                          select new { Receipt = r, Share = s }).ToListAsync(ct);
        if (owed.Count == 0) throw new BadRequestException($"{person.Name} doesn't owe {payer} anything.");
        var now = time.GetUtcNow().UtcDateTime;
        if (owed.Max(o => o.Share.RemindedAt) is DateTime last && now - last < RemindEvery)
            throw new ConflictException($"{person.Name} was reminded {Ago(now - last)}. Give them a little while.");
        if (!await db.Set<PushSubscription>().AnyAsync(s => s.HouseholdId == Hid && s.UserId == userId, ct))
            throw new BadRequestException($"{person.Name} hasn't turned on notifications on any device.");

        // Lines need loading for the "Costco" vs "Rent" description of one-line expenses.
        var receiptIds = owed.Select(o => o.Receipt.Id).ToList();
        await db.Set<ReceiptLine>().Where(l => receiptIds.Contains(l.ReceiptId)).LoadAsync(ct);
        var total = owed.Sum(o => o.Share.Amount);
        var sorted = owed.OrderByDescending(o => o.Receipt.PurchasedAt).ToList();
        var body = sorted.Count == 1
            ? $"{What(sorted[0].Receipt, null)} · {sorted[0].Receipt.PurchasedAt:MMM d}"
            : Names(sorted.Select(o => $"{What(o.Receipt, null)} {Money(o.Share.Amount)}"), 4);
        var from = await db.Users.Where(u => u.Id == me.UserId).Select(u => u.Name).SingleOrDefaultAsync(ct);
        if (from is not null) body += $"\nReminder from {from}";
        var result = await push.SendToUsersAsync([userId],
            new PushMessage($"Reminder: you owe {payer} {Money(total)}", body,
                sorted.Count == 1 ? $"/expenses/receipts/{sorted[0].Receipt.Id}" : "/expenses/owed", $"remind-{req.PersonId}-{req.OwedToPersonId}"), ct);
        if (result.Sent == 0) throw new BadRequestException($"Couldn't reach {person.Name}'s devices: {result.Error ?? "no devices"}");

        foreach (var o in owed) o.Share.RemindedAt = now;
        await db.SaveChangesAsync(ct);
        return new RemindResultDto(result.Sent, now);
    }

    private async Task<Dictionary<int, string>> StoreNamesAsync(IEnumerable<Receipt> receipts, CancellationToken ct)
    {
        var ids = receipts.Select(r => r.StoreId).OfType<int>().Distinct().ToList();
        return await db.Set<Store>().Where(s => ids.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
    }

    /// <summary>"Costco", or "Rent" for a one-line expense (as on the Owed tab).</summary>
    private static string What(Receipt r, Dictionary<int, string>? stores)
    {
        var store = r.Store?.Name ?? (r.StoreId is int id ? stores?.GetValueOrDefault(id) : null);
        return PeopleService.What(store, r.Lines.OrderBy(l => l.Sort).FirstOrDefault()?.Name, r.Lines.Count);
    }

    private static string Names(IEnumerable<string> names, int max)
    {
        var list = names.ToList();
        return list.Count <= max ? string.Join(", ", list) : $"{string.Join(", ", list.Take(max))} + {list.Count - max} more";
    }

    private static string Money(decimal d) => d.ToString("$#,0.00", System.Globalization.CultureInfo.InvariantCulture);

    private static string Ago(TimeSpan t) => t.TotalMinutes < 2 ? "just now" : $"{(int)t.TotalMinutes} minutes ago";
}
