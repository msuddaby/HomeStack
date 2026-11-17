using System.Globalization;
using HomeStack.Core;
using HomeStack.Core.Images;
using HomeStack.Modules.Inventory;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Expenses;

public class ExpensesService(HomeStackDbContext db, ICurrentUser me, TimeProvider time, ImageService images, InventoryService inventory,
    PeopleService people, FileService files, ExpenseNotifier notifier)
{
    public const int MaxPhotos = 4;
    /// <summary>Receipt photos stay readable for returns and warranties.</summary>
    public const int PhotoEdge = 1600;

    private int Hid => me.HouseholdId;
    private IQueryable<Store> Stores => db.Set<Store>().Where(s => s.HouseholdId == Hid);
    private IQueryable<TrackedItem> Items => db.Set<TrackedItem>().Where(i => i.HouseholdId == Hid);
    private IQueryable<ItemCode> Codes => db.Set<ItemCode>().Where(c => c.HouseholdId == Hid);
    private IQueryable<Receipt> Receipts => db.Set<Receipt>().Where(r => r.HouseholdId == Hid);

    // ---------- Lookups used while reading a receipt ----------

    public async Task<List<string>> GetStoreNamesAsync() => await Stores.OrderBy(s => s.Name).Select(s => s.Name).ToListAsync();

    public async Task<Store?> FindStoreAsync(string? name)
    {
        var n = Clean(name)?.ToLower();
        return n is null ? null : await Stores.FirstOrDefaultAsync(s => s.Name.ToLower() == n);
    }

    public async Task<int?> FindItemIdAsync(int? storeId, string? code, string name)
    {
        if (storeId is int sid && Clean(code) is { } c)
            return await Codes.Where(x => x.StoreId == sid && x.Code == c).Select(x => (int?)x.ItemId).FirstOrDefaultAsync();
        // No item number to go on: the exact same name is the same item.
        var n = name.Trim().ToLower();
        return await Items.Where(i => i.Name.ToLower() == n).Select(i => (int?)i.Id).FirstOrDefaultAsync();
    }

    /// <summary>Known items for the AI to match against, most recently bought first.</summary>
    public async Task<List<(int Id, string Name, List<string> Stores)>> GetKnownItemsAsync(int max)
    {
        var names = await Items.ToDictionaryAsync(i => i.Id, i => i.Name);
        return (await PurchasesAsync()).GroupBy(r => r.ItemId)
            .Select(g => (Id: g.Key, Last: g.Max(r => r.Date), Stores: g.Select(r => r.Store).OfType<string>().Distinct().ToList()))
            .OrderByDescending(x => x.Last).Take(max)
            .Where(x => names.ContainsKey(x.Id))
            .Select(x => (x.Id, names[x.Id], x.Stores)).ToList();
    }

    public async Task<Dictionary<int, ItemRefDto>> GetItemRefsAsync(IEnumerable<int> ids)
    {
        var set = ids.Distinct().ToList();
        if (set.Count == 0) return [];
        var items = await Items.Where(i => set.Contains(i.Id)).ToListAsync();
        var rows = await PurchasesAsync(set);
        return items.ToDictionary(i => i.Id, i =>
        {
            var last = rows.Where(r => r.ItemId == i.Id).MaxBy(r => (r.Date, r.ReceiptId));
            return new ItemRefDto(i.Id, i.Name, last?.Store, last?.EffectiveUnitPrice, i.SkipInventory, i.PackQuantity, i.PackUnit);
        });
    }

    public async Task<int?> FindDuplicateAsync(int? storeId, DateOnly? date, decimal? total) =>
        date is null || total is null ? null
            : (await Receipts.Where(r => r.StoreId == storeId && r.PurchasedAt == date).Select(r => new { r.Id, r.Total }).ToListAsync())
                .FirstOrDefault(r => r.Total == total)?.Id;

    // ---------- Saving ----------

    public async Task<ReceiptDto> SaveReceiptAsync(SaveReceiptRequest req)
    {
        var anySplit = req.Split is { Count: > 0 } || req.Lines.Any(l => l.Split is { Count: > 0 });
        var payer = await PayerAsync(req.PaidByPersonId, anySplit, req.Split, req.Lines.SelectMany(l => l.Split ?? []));
        await using var tx = await db.Database.BeginTransactionAsync();
        var store = await GetOrCreateStoreAsync(req.Store);
        var receipt = new Receipt
        {
            HouseholdId = Hid, StoreId = store?.Id, PurchasedAt = req.PurchasedAt, Subtotal = req.Subtotal, Tax = req.Tax,
            Total = req.Total, Note = Clean(req.Note), Source = ReceiptSource.Scan, UserId = me.UserId,
            PaidByPersonId = payer, Split = SplitMath.Parts(req.Split),
        };
        var itemsForInventory = new List<(TrackedItem Item, AddItemRequest Request)>();
        foreach (var (line, sort) in req.Lines.Select((l, i) => (l, i)))
        {
            var item = await ResolveItemAsync(line, store);
            receipt.Lines.Add(new ReceiptLine
            {
                Sort = sort, ItemId = item.Id, Code = Clean(line.Code), Name = line.Name.Trim(), Raw = Clean(line.Raw),
                Category = line.Category, Quantity = line.Quantity, Unit = Clean(line.Unit), UnitPrice = line.UnitPrice,
                Amount = line.Amount, Discount = line.Discount is > 0 ? line.Discount : null,
                Split = SplitMath.Parts(line.Split),
            });
            if (line.Inventory is { } add)
            {
                RememberPack(item, line, add);
                itemsForInventory.Add((item, add with
                {
                    PurchasedAt = add.PurchasedAt ?? req.PurchasedAt,
                    Price = add.Price ?? line.Amount,
                    Notes = add.Notes ?? (store is null ? null : $"From {store.Name}"),
                }));
            }
        }
        var owed = SplitMath.ApplyShares(receipt);
        db.Add(receipt);
        await db.SaveChangesAsync();

        // Same transaction: the receipt and its groceries are saved together or not at all.
        var added = itemsForInventory.Count == 0 ? [] : await inventory.AddItemsAsync(itemsForInventory.Select(x => x.Request).ToList());
        foreach (var ((item, _), dto) in itemsForInventory.Zip(added))
            item.ProductId ??= dto.Product.Id;
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        await notifier.OwedAsync([(receipt, owed)]);

        return (await GetReceiptAsync(receipt.Id)) with { AddedToInventory = added.Count };
    }

    /// <summary>
    /// Who paid, after checking everyone named belongs to the household. With a split but no payer named, it's you.
    /// Without a split there's nothing owed, so no payer is recorded.
    /// </summary>
    internal async Task<int?> PayerAsync(int? paidBy, bool anySplit, params IEnumerable<SplitPartDto>?[] splits)
    {
        await people.CheckAsync(splits.SelectMany(s => s ?? []).Select(p => (int?)p.PersonId).Append(paidBy));
        if (!anySplit) return paidBy;
        return paidBy ?? await people.MyPersonIdAsync() ?? throw new BadRequestException("Say who paid.");
    }

    /// <summary>Changes who paid and how a saved expense is split; shares already marked received stay received.</summary>
    public async Task<ReceiptDto> SetSplitAsync(int id, SetSplitRequest req)
    {
        var r = await Receipts.Include(x => x.Lines).ThenInclude(l => l.Split).Include(x => x.Split).Include(x => x.Shares)
            .SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Receipt not found");
        foreach (var l in req.Lines ?? [])
        {
            var line = r.Lines.SingleOrDefault(x => x.Id == l.LineId) ?? throw new BadRequestException("Unknown line");
            line.Split = SplitMath.Parts(l.Split);
        }
        r.Split = SplitMath.Parts(req.Split);
        var anySplit = r.Split.Count > 0 || r.Lines.Any(l => l.Split.Count > 0);
        r.PaidByPersonId = await PayerAsync(req.PaidByPersonId, anySplit, req.Split, (req.Lines ?? []).SelectMany(l => l.Split ?? []));
        var owed = SplitMath.ApplyShares(r);
        await db.SaveChangesAsync();
        await notifier.OwedAsync([(r, owed)]);
        return await GetReceiptAsync(id);
    }

    /// <summary>
    /// Remembers how much one of these is from what was put away: 1 × PACE SALSA put away as 2 L means one is 2 L.
    /// Put away exactly as the receipt says (2.1 kg of chicken) means there's nothing to remember.
    /// </summary>
    internal static void RememberPack(TrackedItem item, SaveLineRequest line, AddItemRequest add)
    {
        var unit = Clean(add.Unit);
        if (string.Equals(unit, Clean(line.Unit), StringComparison.OrdinalIgnoreCase) && Math.Abs(add.Quantity - line.Quantity) < 0.0005)
        {
            item.PackQuantity = null;
            item.PackUnit = null;
        }
        else
        {
            item.PackQuantity = Math.Round(add.Quantity / line.Quantity, 3);
            item.PackUnit = unit;
        }
    }

    /// <summary>The tracked item a line belongs to: the one chosen, else the one with this store's item number, else a new one.</summary>
    private async Task<TrackedItem> ResolveItemAsync(SaveLineRequest line, Store? store)
    {
        var code = Clean(line.Code);
        TrackedItem? item = null;
        if (line.ItemId is int chosen)
            item = await Items.SingleOrDefaultAsync(i => i.Id == chosen) ?? throw new BadRequestException("Unknown item");
        else if (await FindItemIdAsync(store?.Id, code, line.Name) is int found)
            item = await Items.SingleAsync(i => i.Id == found);

        if (item is null)
        {
            item = new TrackedItem { HouseholdId = Hid, Name = line.Name.Trim(), Category = line.Category };
            db.Add(item);
        }
        // Remember "Put away" for next time: unticked means this is food they don't keep in inventory.
        if (line.TrackInventory is bool track) item.SkipInventory = !track;
        await db.SaveChangesAsync();

        // Remember the store's number for it, so next time it links by itself.
        if (store is not null && code is not null)
        {
            var existing = await Codes.SingleOrDefaultAsync(c => c.StoreId == store.Id && c.Code == code);
            if (existing is null) db.Add(new ItemCode { HouseholdId = Hid, StoreId = store.Id, Code = code, ItemId = item.Id });
            else existing.ItemId = item.Id; // the user linked it to a different item
            await db.SaveChangesAsync();
        }
        return item;
    }

    public async Task<ReceiptDto> AddManualAsync(ManualExpenseRequest req)
    {
        var payer = await PayerAsync(req.PaidByPersonId, req.Split is { Count: > 0 }, req.Split);
        var store = await GetOrCreateStoreAsync(req.Store);
        var receipt = new Receipt
        {
            HouseholdId = Hid, StoreId = store?.Id, PurchasedAt = req.PurchasedAt, Total = req.Amount, Note = Clean(req.Note),
            Source = ReceiptSource.Manual, UserId = me.UserId, PaidByPersonId = payer, Split = SplitMath.Parts(req.Split),
            // Not a tracked item: one-off amounts would clutter the item list.
            Lines = [new ReceiptLine { Name = req.Description.Trim(), Category = req.Category, Amount = req.Amount }],
        };
        var owed = SplitMath.ApplyShares(receipt);
        db.Add(receipt);
        await db.SaveChangesAsync();
        await notifier.OwedAsync([(receipt, owed)]);
        return await GetReceiptAsync(receipt.Id);
    }

    public async Task<ReceiptDto> UpdateReceiptAsync(int id, UpdateReceiptRequest req)
    {
        var r = await Receipts.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Receipt not found");
        r.StoreId = (await GetOrCreateStoreAsync(req.Store))?.Id;
        r.PurchasedAt = req.PurchasedAt;
        r.Note = Clean(req.Note);
        await db.SaveChangesAsync();
        return await GetReceiptAsync(id);
    }

    /// <summary>Attaches a bill's original file: a PDF is kept as-is, a photo is stored like receipt photos.</summary>
    public async Task<ReceiptDto> AttachDocumentAsync(int id, byte[] data, string fileName, CancellationToken ct)
    {
        var r = await Receipts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Receipt not found");
        if (!Pdf.Looks(data)) return await AddPhotosAsync(id, [data], ct);
        var old = r.DocumentId;
        r.DocumentId = (await files.SaveAsync(Hid, data, Pdf.ContentType, fileName, ct)).Id;
        await db.SaveChangesAsync(ct);
        await files.DeleteAsync(old, ct);
        return await GetReceiptAsync(id);
    }

    /// <summary>Marks a bill paid to the company on a date, or not paid (null).</summary>
    public async Task<ReceiptDto> SetBillPaidAsync(int id, DateOnly? paidOn)
    {
        var r = await Receipts.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Receipt not found");
        r.BillPaidOn = paidOn;
        await db.SaveChangesAsync();
        return await GetReceiptAsync(id);
    }

    public async Task<ReceiptDto> AddPhotosAsync(int id, IReadOnlyList<byte[]> photos, CancellationToken ct)
    {
        var r = await Receipts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Receipt not found");
        if (photos.Count == 0) throw new BadRequestException("Choose a photo to upload.");
        if (r.ImageIds.Count + photos.Count > MaxPhotos) throw new BadRequestException($"A receipt can have up to {MaxPhotos} photos.");
        var ids = new List<Guid>(r.ImageIds);
        foreach (var p in photos) ids.Add((await images.SaveAsync(Hid, p, PhotoEdge, ct: ct)).Id);
        r.ImageIds = ids;
        await db.SaveChangesAsync(ct);
        return await GetReceiptAsync(id);
    }

    /// <summary>Deletes the receipt and its photos. Items left with no purchases go too.</summary>
    public async Task DeleteReceiptAsync(int id)
    {
        var r = await Receipts.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Receipt not found");
        var itemIds = r.Lines.Select(l => l.ItemId).OfType<int>().Distinct().ToList();
        var photos = r.ImageIds.ToList();
        var document = r.DocumentId;
        db.Remove(r);
        await db.SaveChangesAsync();
        foreach (var p in photos) await images.DeleteAsync(p);
        await files.DeleteAsync(document);

        var stillUsed = await db.Set<ReceiptLine>().Where(l => l.ItemId != null && itemIds.Contains(l.ItemId.Value))
            .Select(l => l.ItemId!.Value).Distinct().ToListAsync();
        var orphans = itemIds.Except(stillUsed).ToList();
        if (orphans.Count > 0)
        {
            await Codes.Where(c => orphans.Contains(c.ItemId)).ExecuteDeleteAsync();
            await Items.Where(i => orphans.Contains(i.Id)).ExecuteDeleteAsync();
        }
    }

    internal async Task<Store?> GetOrCreateStoreAsync(string? name)
    {
        if (Clean(name) is not { } n) return null;
        if (await FindStoreAsync(n) is { } s) return s;
        s = new Store { HouseholdId = Hid, Name = n.Length <= 80 ? n : n[..80] };
        db.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    // ---------- Receipts ----------

    public async Task<List<ReceiptSummaryDto>> ListReceiptsAsync(DateOnly? from, DateOnly? to, int? storeId)
    {
        var q = Receipts.Include(r => r.Store).AsQueryable();
        if (from is { } f) q = q.Where(r => r.PurchasedAt >= f);
        if (to is { } t) q = q.Where(r => r.PurchasedAt <= t);
        if (storeId is { } s) q = q.Where(r => r.StoreId == s);
        var list = await q.OrderByDescending(r => r.PurchasedAt).ThenByDescending(r => r.Id).Take(500)
            .Select(r => new
            {
                r.Id, Store = r.Store!.Name, r.PurchasedAt, r.Total, Lines = r.Lines.Count, First = r.Lines.Select(l => l.Name).FirstOrDefault(),
                r.Source, r.ImageIds, r.DocumentId, r.DueDate, r.BillPaidOn,
                Outstanding = r.Shares.Where(s => s.ReceivedAt == null).Select(s => s.Amount).ToList(),
            }).ToListAsync();
        // A manual expense or rent with no store shows its description instead.
        return list.Select(r => new ReceiptSummaryDto(r.Id, r.Store ?? (r.Lines == 1 ? r.First : null), r.PurchasedAt, r.Total, r.Lines, r.Source,
            r.ImageIds.Count > 0 || r.DocumentId != null, r.DueDate, r.BillPaidOn, r.Outstanding.Sum())).ToList();
    }

    public async Task<ReceiptDto> GetReceiptAsync(int id)
    {
        var r = await Receipts.Include(x => x.Store).Include(x => x.Lines).ThenInclude(l => l.Split).Include(x => x.Split).Include(x => x.Shares)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Receipt not found");
        var names = r.PaidByPersonId is null ? [] : await people.NamesAsync();
        var breakdown = SplitMath.Breakdown(r);
        var members = r.Shares.Count == 0 ? [] : await db.Set<Person>().Where(p => p.HouseholdId == Hid && p.UserId != null).Select(p => p.Id).ToListAsync();
        return new ReceiptDto(r.Id, r.Store?.Name, r.PurchasedAt, r.Subtotal, r.Tax, r.Total, r.Note, r.Source,
            r.ImageIds.Select(i => StoredImage.Src(i, null)!).ToList(),
            r.Lines.OrderBy(l => l.Sort).Select(l => new ReceiptLineDto(l.Id, l.ItemId, l.Code, l.Name, l.Raw, l.Category, l.Quantity,
                l.Unit, l.UnitPrice, l.Amount, l.Discount, SplitMath.Dtos(l.Split))).ToList(),
            r.CreatedAt,
            r.PaidByPersonId, r.PaidByPersonId is int p ? names.GetValueOrDefault(p) : null, SplitMath.Dtos(r.Split),
            r.Shares.OrderBy(s => names.GetValueOrDefault(s.PersonId)).Select(s => new ShareDto(s.Id, s.PersonId, names.GetValueOrDefault(s.PersonId, "?"), s.Amount, s.ReceivedAt,
                breakdown.GetValueOrDefault(s.PersonId) ?? [], s.RemindedAt, members.Contains(s.PersonId))).ToList(),
            r.DueDate, r.BillPaidOn, r.RecurringId, r.DocumentId is Guid doc ? StoredFile.Src(doc) : null);
    }

    // ---------- Tracked items ----------

    private record Purchase(int ItemId, int ReceiptId, DateOnly Date, string? Store, double Quantity, string? Unit,
        decimal? UnitPrice, decimal Amount, decimal? Discount)
    {
        /// <summary>What one unit really cost, after discounts.</summary>
        public decimal? EffectiveUnitPrice => Quantity > 0 ? Math.Round(Amount / (decimal)Quantity, 2) : UnitPrice;
    }

    /// <summary>Every purchase of a tracked item (optionally only these items). Filter here, not after: the projection can't be queried.</summary>
    private Task<List<Purchase>> PurchasesAsync(IReadOnlyCollection<int>? itemIds = null)
    {
        var lines = from r in Receipts
                    from l in r.Lines
                    where l.ItemId != null && (itemIds == null || itemIds.Contains(l.ItemId.Value))
                    select new { l.ItemId, ReceiptId = r.Id, r.PurchasedAt, Store = r.Store!.Name, l.Quantity, l.Unit, l.UnitPrice, l.Amount, l.Discount };
        return lines.Select(x => new Purchase(x.ItemId!.Value, x.ReceiptId, x.PurchasedAt, x.Store, x.Quantity, x.Unit, x.UnitPrice, x.Amount, x.Discount))
            .ToListAsync();
    }

    /// <summary>
    /// How often something's bought, from the distinct days it was bought. "Due" needs at least three purchases
    /// (two gaps) and is dropped once it's three times overdue (probably stopped buying it).
    /// </summary>
    internal static RhythmDto Rhythm(IEnumerable<DateOnly> dates, DateOnly today)
    {
        var days = dates.Distinct().Order().ToList();
        var last = days[^1];
        var since = today.DayNumber - last.DayNumber;
        var gaps = days.Zip(days.Skip(1), (a, b) => b.DayNumber - a.DayNumber).Order().ToList();
        int? every = gaps.Count == 0 ? null : (int)Math.Round(gaps.Count % 2 == 1 ? gaps[gaps.Count / 2] : (gaps[gaps.Count / 2 - 1] + gaps[gaps.Count / 2]) / 2.0);
        var due = gaps.Count >= 2 && every is int e && e > 0 && since >= Math.Round(e * 0.85) && since <= e * 3;
        return new RhythmDto(days.Count, last, since, every, due);
    }

    public async Task<List<TrackedItemDto>> ListItemsAsync(string? q)
    {
        var items = await Items.ToListAsync();
        if (Clean(q) is { } term) items = items.Where(i => i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        var ids = items.Select(i => i.Id).ToList();
        var purchases = (await PurchasesAsync(ids)).ToLookup(p => p.ItemId);
        var codes = (await Codes.Where(c => ids.Contains(c.ItemId)).Select(c => new { c.ItemId, c.Code }).ToListAsync()).ToLookup(c => c.ItemId, c => c.Code);
        var today = time.Today();
        return items.Select(i => ToDto(i, purchases[i.Id].ToList(), codes[i.Id].ToList(), today))
            .OrderByDescending(d => d.Rhythm?.LastBought).ThenBy(d => d.Name).ToList();
    }

    private static TrackedItemDto ToDto(TrackedItem i, List<Purchase> purchases, List<string> codes, DateOnly today)
    {
        var last = purchases.MaxBy(p => (p.Date, p.ReceiptId));
        return new TrackedItemDto(i.Id, i.Name, i.Category, i.ProductId,
            purchases.Count == 0 ? null : Rhythm(purchases.Select(p => p.Date), today), last?.EffectiveUnitPrice, last?.Store, codes,
            i.SkipInventory, i.PackQuantity, i.PackUnit);
    }

    public async Task<TrackedItemDetailDto> GetItemAsync(int id)
    {
        var item = await Items.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Item not found");
        var purchases = await PurchasesAsync([id]);
        var codes = await Codes.Where(c => c.ItemId == id).Select(c => c.Code).ToListAsync();
        var stores = purchases.GroupBy(p => p.Store ?? "No store").Select(g =>
        {
            var last = g.MaxBy(p => (p.Date, p.ReceiptId))!;
            return new StorePriceDto(g.Key, last.EffectiveUnitPrice, g.Min(p => p.EffectiveUnitPrice), last.Date, g.Select(p => p.ReceiptId).Distinct().Count());
        }).OrderBy(s => s.LastUnitPrice ?? decimal.MaxValue).ToList();
        return new TrackedItemDetailDto(ToDto(item, purchases, codes, time.Today()), stores,
            purchases.OrderByDescending(p => p.Date).ThenByDescending(p => p.ReceiptId)
                .Select(p => new PurchaseDto(p.ReceiptId, p.Date, p.Store, p.Quantity, p.Unit, p.UnitPrice, p.Amount, p.Discount)).ToList(),
            purchases.Sum(p => p.Amount));
    }

    public async Task<TrackedItemDetailDto> UpdateItemAsync(int id, UpdateItemRequest req)
    {
        var item = await Items.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Item not found");
        item.Name = req.Name.Trim();
        item.Category = req.Category;
        if (req.SkipInventory is bool skip) item.SkipInventory = skip;
        if (req.UpdatePack)
        {
            item.PackQuantity = req.PackQuantity;
            item.PackUnit = req.PackQuantity is null ? null : Clean(req.PackUnit);
        }
        await db.SaveChangesAsync();
        return await GetItemAsync(id);
    }

    /// <summary>Folds one item into another: its purchases and store numbers move across, then it's deleted.</summary>
    public async Task<TrackedItemDetailDto> MergeItemsAsync(int id, MergeItemsRequest req)
    {
        if (id == req.IntoItemId) throw new BadRequestException("Pick a different item.");
        var source = await Items.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Item not found");
        var into = await Items.SingleOrDefaultAsync(i => i.Id == req.IntoItemId) ?? throw new NotFoundException("Item not found");
        await using var tx = await db.Database.BeginTransactionAsync();

        var sourceId = source.Id;
        var lines = await (from r in Receipts from l in r.Lines where l.ItemId == sourceId select l).ToListAsync();
        foreach (var l in lines) l.ItemId = into.Id;
        var intoCodes = await Codes.Where(c => c.ItemId == into.Id).Select(c => new { c.StoreId, c.Code }).ToListAsync();
        foreach (var c in await Codes.Where(c => c.ItemId == sourceId).ToListAsync())
        {
            if (intoCodes.Any(x => x.StoreId == c.StoreId && x.Code == c.Code)) db.Remove(c);
            else c.ItemId = into.Id;
        }
        into.ProductId ??= source.ProductId;
        into.SkipInventory = into.SkipInventory || source.SkipInventory;
        if (into.PackQuantity is null) (into.PackQuantity, into.PackUnit) = (source.PackQuantity, source.PackUnit);
        db.Remove(source);
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return await GetItemAsync(into.Id);
    }

    /// <summary>Things bought regularly that are about due again, most overdue first.</summary>
    public async Task<List<TrackedItemDto>> GetDueAsync() =>
        (await ListItemsAsync(null)).Where(i => i.Rhythm is { Due: true })
            .OrderByDescending(i => (double)i.Rhythm!.DaysSinceLast / i.Rhythm.EveryDays!.Value).ToList();

    // ---------- Spending summary ----------

    /// <summary>Spending for one month ("YYYY-MM", default this month) plus monthly totals for the last <paramref name="months"/>.</summary>
    public async Task<SummaryDto> GetSummaryAsync(string? month, int months)
    {
        var today = time.Today();
        var start = DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var m)
            ? m : new DateOnly(today.Year, today.Month, 1);
        months = Math.Clamp(months, 1, 24);
        var rangeStart = start.AddMonths(-(months - 1));
        var end = start.AddMonths(1);

        var receipts = await Receipts.Include(r => r.Store).Include(r => r.Lines)
            .Where(r => r.PurchasedAt >= rangeStart.AddMonths(-1) && r.PurchasedAt < end).ToListAsync();
        static string Key(DateOnly d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        var monthTotals = Enumerable.Range(0, months).Select(i => rangeStart.AddMonths(i)).Select(d =>
        {
            var inMonth = receipts.Where(r => Key(r.PurchasedAt) == Key(d)).ToList();
            return new MonthTotalDto(Key(d), inMonth.Sum(r => r.Total), inMonth.Count);
        }).ToList();

        var current = receipts.Where(r => Key(r.PurchasedAt) == Key(start)).ToList();
        var previous = receipts.Where(r => Key(r.PurchasedAt) == Key(start.AddMonths(-1))).ToList();

        var labels = ExpenseCategories.All.ToDictionary(c => c.Id, c => c.Label);
        var byCategory = new Dictionary<string, decimal>();
        void Add(string key, decimal amount) => byCategory[key] = byCategory.GetValueOrDefault(key) + amount;
        foreach (var r in current)
        {
            foreach (var l in r.Lines) Add(l.Category, l.Amount);
            if (r.Tax is > 0) Add("tax", r.Tax.Value);
            // Lines the AI couldn't read still count toward the total.
            var rest = r.Total - r.Lines.Sum(l => l.Amount) - (r.Tax ?? 0);
            if (Math.Abs(rest) >= 0.05m) Add("unitemised", rest);
        }

        return new SummaryDto(Key(start), current.Sum(r => r.Total), previous.Count == 0 ? null : previous.Sum(r => r.Total),
            byCategory.Where(kv => kv.Value != 0).OrderByDescending(kv => kv.Value)
                .Select(kv => new BreakdownDto(kv.Key, kv.Key switch
                {
                    "tax" => "Tax",
                    "unitemised" => "Not itemised",
                    _ => labels.GetValueOrDefault(kv.Key, kv.Key),
                }, kv.Value)).ToList(),
            current.GroupBy(r => r.Store?.Name ?? "No store").Select(g => new BreakdownDto(g.Key, g.Key, g.Sum(r => r.Total)))
                .OrderByDescending(b => b.Total).ToList(),
            monthTotals);
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
