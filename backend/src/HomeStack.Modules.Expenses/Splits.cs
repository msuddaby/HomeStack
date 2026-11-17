using HomeStack.Core;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Expenses;

/// <summary>Works out who owes what from a receipt's split.</summary>
public static class SplitMath
{
    /// <summary>
    /// What each person other than the payer owes. Each line is shared by its own split (or the receipt's), by weight;
    /// a line with no split is the payer's. Tax and anything not itemised are shared in proportion, by scaling every
    /// portion so they add up to the receipt total. Empty when there's no payer.
    /// </summary>
    public static Dictionary<int, decimal> Owed(Receipt r)
    {
        if (r.PaidByPersonId is not int payer) return [];
        // Decimal throughout: money shouldn't pick up floating-point error before it's rounded.
        var portions = new Dictionary<int, decimal>();
        foreach (var (line, person, amount) in Portions(r))
            portions[person] = portions.GetValueOrDefault(person) + amount;
        var linesSum = r.Lines.Sum(l => l.Amount);
        if (linesSum == 0) return [];
        return portions.Where(kv => kv.Key != payer)
            // Scaled to the receipt total (tax, anything not itemised); half a cent rounds up (71.185 → 71.19).
            .Select(kv => (kv.Key, Amount: Math.Round(kv.Value * r.Total / linesSum, 2, MidpointRounding.AwayFromZero)))
            .Where(x => x.Amount > 0)
            .ToDictionary(x => x.Key, x => x.Amount);
    }

    /// <summary>
    /// Each person's part of each line, before tax: shared by the line's split (or the receipt's), by weight;
    /// a line with no split is the payer's.
    /// </summary>
    private static IEnumerable<(ReceiptLine Line, int Person, decimal Amount)> Portions(Receipt r)
    {
        if (r.PaidByPersonId is not int payer) yield break;
        foreach (var line in r.Lines)
        {
            var parts = line.Split.Count > 0 ? line.Split : r.Split;
            var weights = parts.Sum(p => (decimal)p.Weight);
            if (parts.Count == 0 || weights <= 0) { yield return (line, payer, line.Amount); continue; }
            foreach (var p in parts) yield return (line, p.PersonId, line.Amount * (decimal)p.Weight / weights);
        }
    }

    /// <summary>
    /// What each person's share is made of: their part of each line (before tax, rounded to the cent) in receipt order.
    /// Whatever's left of the share is tax and anything not itemised.
    /// </summary>
    public static Dictionary<int, List<ShareLineDto>> Breakdown(Receipt r) =>
        Portions(r).GroupBy(x => x.Person).ToDictionary(g => g.Key, g => g
            .OrderBy(x => x.Line.Sort)
            .Select(x => new ShareLineDto(x.Line.Id, x.Line.Name, Math.Round(x.Amount, 2, MidpointRounding.AwayFromZero), x.Line.Amount))
            .Where(x => x.Amount != 0)
            .ToList());

    /// <summary>
    /// Brings the receipt's shares in line with its split, keeping "received" on people who still owe.
    /// Returns the shares that are new or bigger than before (to tell those people).
    /// </summary>
    public static IReadOnlyList<Share> ApplyShares(Receipt r)
    {
        var owed = Owed(r);
        var more = new List<Share>();
        r.Shares.RemoveAll(s => !owed.ContainsKey(s.PersonId));
        foreach (var (person, amount) in owed)
        {
            var share = r.Shares.FirstOrDefault(s => s.PersonId == person);
            if (share is null) r.Shares.Add(share = new Share { PersonId = person, Amount = amount });
            else if (amount > share.Amount) share.Amount = amount;
            else { share.Amount = amount; continue; }
            more.Add(share);
        }
        return more;
    }

    public static List<SplitPart> Parts(IEnumerable<SplitPartDto>? parts) =>
        (parts ?? []).Select(p => new SplitPart { PersonId = p.PersonId, Weight = p.Weight }).ToList();

    public static List<SplitPartDto> Dtos(IEnumerable<SplitPart> parts) => parts.Select(p => new SplitPartDto(p.PersonId, p.Weight)).ToList();
}

/// <summary>People to split with, and who owes whom.</summary>
public class PeopleService(HomeStackDbContext db, ICurrentUser me)
{
    private int Hid => me.HouseholdId;
    private IQueryable<Person> People => db.Set<Person>().Where(p => p.HouseholdId == Hid);

    /// <summary>
    /// Everyone you can split with. Household members are added (and renamed) automatically; a member who's been
    /// removed is archived so their history stays.
    /// </summary>
    public async Task<List<PersonDto>> ListAsync()
    {
        await SyncMembersAsync();
        var mine = me.UserId;
        return (await People.OrderBy(p => p.Archived).ThenBy(p => p.UserId == null).ThenBy(p => p.Name).ToListAsync())
            .Select(p => new PersonDto(p.Id, p.Name, p.UserId, p.Archived, p.UserId == mine && mine != 0)).ToList();
    }

    private async Task SyncMembersAsync()
    {
        var users = await db.Users.Where(u => u.HouseholdId == Hid).Select(u => new { u.Id, u.Name }).ToListAsync();
        var people = await People.Where(p => p.UserId != null).ToListAsync();
        foreach (var u in users)
        {
            var p = people.FirstOrDefault(x => x.UserId == u.Id);
            if (p is null) db.Add(new Person { HouseholdId = Hid, Name = u.Name, UserId = u.Id });
            else { p.Name = u.Name; p.Archived = false; }
        }
        foreach (var p in people.Where(p => users.All(u => u.Id != p.UserId))) p.Archived = true;
        await db.SaveChangesAsync();
    }

    /// <summary>The signed-in member's own person (created if needed). Null for background jobs.</summary>
    public async Task<int?> MyPersonIdAsync()
    {
        if (me.UserId == 0) return null;
        var uid = me.UserId;
        var id = await People.Where(p => p.UserId == uid).Select(p => (int?)p.Id).FirstOrDefaultAsync();
        if (id is not null) return id;
        await SyncMembersAsync();
        return await People.Where(p => p.UserId == uid).Select(p => (int?)p.Id).FirstOrDefaultAsync();
    }

    /// <summary>Throws unless every id is someone in this household.</summary>
    public async Task CheckAsync(IEnumerable<int?> ids)
    {
        var set = ids.OfType<int>().Distinct().ToList();
        if (set.Count == 0) return;
        if (await People.CountAsync(p => set.Contains(p.Id)) != set.Count) throw new BadRequestException("Unknown person in the split.");
    }

    public async Task<PersonDto> AddAsync(PersonRequest req)
    {
        var p = new Person { HouseholdId = Hid, Name = req.Name.Trim() };
        db.Add(p);
        await db.SaveChangesAsync();
        return new PersonDto(p.Id, p.Name, null, false, false);
    }

    /// <summary>Renames someone who isn't a member (members' names come from their account).</summary>
    public async Task<PersonDto> RenameAsync(int id, PersonRequest req)
    {
        var p = await People.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Person not found");
        if (p.UserId is not null) throw new BadRequestException("Household members' names come from their account.");
        p.Name = req.Name.Trim();
        await db.SaveChangesAsync();
        return new PersonDto(p.Id, p.Name, null, p.Archived, false);
    }

    /// <summary>Hides someone who isn't a member from the pickers. What they owe stays visible.</summary>
    public async Task ArchiveAsync(int id)
    {
        var p = await People.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Person not found");
        if (p.UserId is not null) throw new BadRequestException("Remove household members in Settings → Household members.");
        p.Archived = true;
        await db.SaveChangesAsync();
    }

    public async Task<Dictionary<int, string>> NamesAsync() => await People.ToDictionaryAsync(p => p.Id, p => p.Name);

    // ---------- Who owes whom ----------

    /// <summary>Outstanding shares by person and who they owe (plus those received in the last <paramref name="receivedDays"/> days).</summary>
    public async Task<BalancesDto> BalancesAsync(int receivedDays = 30)
    {
        var since = DateTime.UtcNow.AddDays(-receivedDays);
        var rows = await (from r in db.Set<Receipt>()
                          where r.HouseholdId == Hid && r.PaidByPersonId != null
                          from s in r.Shares
                          where s.ReceivedAt == null || s.ReceivedAt >= since
                          select new
                          {
                              s.Id, s.PersonId, OwedTo = r.PaidByPersonId!.Value, ReceiptId = r.Id, r.PurchasedAt, Store = r.Store!.Name,
                              First = r.Lines.OrderBy(l => l.Sort).Select(l => l.Name).FirstOrDefault(), LineCount = r.Lines.Count, s.Amount, s.ReceivedAt,
                              s.RemindedAt,
                          }).ToListAsync();
        var names = await NamesAsync();
        var members = await People.Where(p => p.UserId != null).Select(p => p.Id).ToListAsync();
        var balances = rows.GroupBy(x => (x.PersonId, x.OwedTo)).Select(g => new BalanceDto(
                g.Key.PersonId, names.GetValueOrDefault(g.Key.PersonId, "?"), g.Key.OwedTo, names.GetValueOrDefault(g.Key.OwedTo, "?"),
                g.Where(x => x.ReceivedAt == null).Sum(x => x.Amount),
                g.OrderBy(x => x.ReceivedAt != null).ThenByDescending(x => x.PurchasedAt)
                    .Select(x => new OwedItemDto(x.Id, x.ReceiptId, What(x.Store, x.First, x.LineCount), x.PurchasedAt, x.Amount, x.ReceivedAt, x.RemindedAt)).ToList(),
                members.Contains(g.Key.PersonId)))
            .OrderByDescending(b => b.Outstanding).ThenBy(b => b.Person).ToList();
        return new BalancesDto(await me.IsAdminAsync(db), await MyPersonIdAsync(), balances);
    }

    /// <summary>"Costco", or the item for a one-line expense ("Rent"), or both.</summary>
    internal static string What(string? store, string? first, int lines) =>
        lines == 1 && first is not null ? (store is null || first.Contains(store, StringComparison.OrdinalIgnoreCase) ? first : $"{first} ({store})")
        : store ?? first ?? "Expense";

    /// <summary>Admins mark a share received (or not, to undo).</summary>
    public async Task MarkReceivedAsync(int shareId, bool received)
    {
        await me.RequireAdminAsync(db);
        var share = await (from r in db.Set<Receipt>() where r.HouseholdId == Hid from s in r.Shares where s.Id == shareId select s)
            .SingleOrDefaultAsync() ?? throw new NotFoundException("Share not found");
        share.ReceivedAt = received ? share.ReceivedAt ?? DateTime.UtcNow : null;
        share.ReceivedByUserId = received ? me.UserId : null;
        await db.SaveChangesAsync();
    }
}
