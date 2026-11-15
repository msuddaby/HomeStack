using HomeStack.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeStack.Modules.Inventory;

public class InventoryService(HomeStackDbContext db, ICurrentUser me, TimeProvider time, IOptions<InventoryOptions> options)
{
    private readonly InventoryOptions _opt = options.Value;
    private int Hid => me.HouseholdId;

    private IQueryable<Location> Locations => db.Set<Location>().Where(l => l.HouseholdId == Hid);
    private IQueryable<Product> Products => db.Set<Product>().Where(p => p.HouseholdId == Hid);
    private IQueryable<InventoryItem> Items => db.Set<InventoryItem>().Where(i => i.HouseholdId == Hid)
        .Include(i => i.Product).Include(i => i.Location);

    // ---------- Locations ----------

    public async Task<List<LocationDto>> GetLocationsAsync() =>
        (await Locations.OrderBy(l => l.Sort).ThenBy(l => l.Id).ToListAsync()).Select(l => l.ToDto()).ToList();

    public async Task<LocationDto> CreateLocationAsync(LocationRequest req)
    {
        var sort = (await Locations.MaxAsync(l => (int?)l.Sort) ?? -1) + 1;
        var loc = new Location { HouseholdId = Hid, Name = req.Name.Trim(), Kind = req.Kind, Sort = sort };
        db.Add(loc);
        await db.SaveChangesAsync();
        return loc.ToDto();
    }

    public async Task<LocationDto> UpdateLocationAsync(int id, LocationRequest req)
    {
        var loc = await Locations.SingleOrDefaultAsync(l => l.Id == id) ?? throw new NotFoundException("Location not found");
        loc.Name = req.Name.Trim();
        loc.Kind = req.Kind;
        await db.SaveChangesAsync();
        return loc.ToDto();
    }

    public async Task DeleteLocationAsync(int id)
    {
        var loc = await Locations.SingleOrDefaultAsync(l => l.Id == id) ?? throw new NotFoundException("Location not found");
        if (await db.Set<InventoryItem>().AnyAsync(i => i.LocationId == id))
            throw new ConflictException("Move or remove the items in this location first.");
        db.Remove(loc);
        await db.SaveChangesAsync();
    }

    // ---------- Products ----------

    public async Task<List<ProductDto>> SearchProductsAsync(string? q, int limit = 20)
    {
        var query = Products;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.Name, pattern) || (p.Brand != null && EF.Functions.Like(p.Brand, pattern)));
        }
        var list = await query.OrderBy(p => p.Name).Take(Math.Clamp(limit, 1, 100)).ToListAsync();
        return list.Select(p => p.ToDto()).ToList();
    }

    public async Task<ProductDto> CreateProductAsync(ProductRequest req) => (await CreateProductEntityAsync(req)).ToDto();

    public async Task<ProductDto> UpdateProductAsync(int id, ProductRequest req)
    {
        var p = await Products.SingleOrDefaultAsync(x => x.Id == id) ?? throw new NotFoundException("Product not found");
        await EnsureBarcodeFree(req.Barcode, id);
        p.Apply(req);
        await db.SaveChangesAsync();
        return p.ToDto();
    }

    public async Task<ProductDto?> FindByBarcodeAsync(string barcode)
    {
        var p = await Products.FirstOrDefaultAsync(x => x.Barcode == barcode.Trim());
        return p?.ToDto();
    }

    private async Task<Product> CreateProductEntityAsync(ProductRequest req)
    {
        await EnsureBarcodeFree(req.Barcode, null);
        var p = new Product { HouseholdId = Hid, Name = req.Name };
        p.Apply(req);
        db.Add(p);
        await db.SaveChangesAsync();
        return p;
    }

    private async Task EnsureBarcodeFree(string? barcode, int? exceptId)
    {
        var code = Mapping.Blank(barcode);
        if (code is not null && await Products.AnyAsync(p => p.Barcode == code && p.Id != exceptId))
            throw new ConflictException($"Another product already has barcode {code}.");
    }

    // ---------- Items ----------

    public async Task<List<ItemDto>> ListItemsAsync(int? locationId, LocationKind? kind, string? q)
    {
        var query = Items;
        if (locationId is not null) query = query.Where(i => i.LocationId == locationId);
        if (kind is not null) query = query.Where(i => i.Location!.Kind == kind);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(i => EF.Functions.Like(i.Product!.Name, $"%{q.Trim()}%"));

        var items = await query.ToListAsync();
        // Soonest expiry first; undated items last.
        return items.OrderBy(i => i.ExpiresAt ?? DateOnly.MaxValue).ThenBy(i => i.Product!.Name)
            .Select(i => i.ToDto()).ToList();
    }

    public async Task<ItemDto> GetItemAsync(int id) => (await LoadItem(id)).ToDto();

    /// <summary>Adds several items in one transaction. New products with a name we already know reuse that product.</summary>
    public async Task<List<ItemDto>> AddItemsAsync(IReadOnlyList<AddItemRequest> requests)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        var added = new List<ItemDto>();
        foreach (var r in requests)
        {
            var req = r.ProductId is null && r.Product is { } p && await FindByNameAsync(p.Name) is { } known
                ? r with { ProductId = known.Id, Product = null } : r;
            added.Add(await AddItemAsync(req));
        }
        await tx.CommitAsync();
        return added;
    }

    public async Task<ItemDto> AddItemAsync(AddItemRequest req)
    {
        var location = await Locations.SingleOrDefaultAsync(l => l.Id == req.LocationId) ?? throw new BadRequestException("Unknown location");
        var product = req.ProductId is int pid
            ? await Products.SingleOrDefaultAsync(p => p.Id == pid) ?? throw new BadRequestException("Unknown product")
            : await CreateProductEntityAsync(req.Product!);

        var today = time.Today();
        var item = new InventoryItem
        {
            HouseholdId = Hid,
            ProductId = product.Id,
            LocationId = location.Id,
            Quantity = req.Quantity,
            Unit = Mapping.Blank(req.Unit) ?? product.DefaultUnit,
            PurchasedAt = req.PurchasedAt ?? today,
            OpenedAt = req.OpenedAt,
            Notes = Mapping.Blank(req.Notes),
        };
        // Things put straight in the freezer count as frozen from purchase.
        item.FrozenAt = req.FrozenAt ?? (location.Kind == LocationKind.Freezer ? item.PurchasedAt : null);
        item.ExpiresAt = req.NoExpiry ? null : req.ExpiresAt ?? SuggestExpiry(product, location.Kind, item);

        db.Add(item);
        await db.SaveChangesAsync();
        await LogAsync(item, InventoryEventType.Added, item.Quantity);
        item.Product = product;
        item.Location = location;
        return item.ToDto();
    }

    public async Task<ItemDto> UpdateItemAsync(int id, UpdateItemRequest req)
    {
        var item = await LoadItem(id);
        if (req.LocationId != item.LocationId)
            item.Location = await Locations.SingleOrDefaultAsync(l => l.Id == req.LocationId) ?? throw new BadRequestException("Unknown location");
        item.LocationId = req.LocationId;
        item.Quantity = req.Quantity;
        item.Unit = Mapping.Blank(req.Unit);
        item.PurchasedAt = req.PurchasedAt;
        item.ExpiresAt = req.ExpiresAt;
        item.OpenedAt = req.OpenedAt;
        item.FrozenAt = req.FrozenAt;
        item.Notes = Mapping.Blank(req.Notes);
        await db.SaveChangesAsync();
        return item.ToDto();
    }

    /// <summary>Use up or throw out some/all of an item. Returns the remaining item, or null if it's gone.</summary>
    public async Task<ItemDto?> ConsumeAsync(int id, ConsumeRequest req)
    {
        var item = await LoadItem(id);
        var amount = Math.Min(req.Quantity ?? item.Quantity, item.Quantity);
        await LogAsync(item, req.Wasted ? InventoryEventType.Wasted : InventoryEventType.Used, amount, save: false);

        if (amount >= item.Quantity - 1e-9)
        {
            db.Remove(item);
            await db.SaveChangesAsync();
            return null;
        }
        item.Quantity = Math.Round(item.Quantity - amount, 3);
        await db.SaveChangesAsync();
        return item.ToDto();
    }

    /// <summary>
    /// Move an item. Into the freezer: records the freeze date and resets expiry to freezer life.
    /// Out of the freezer: treated as thawed (short use-by). Between other places: expiry can only get sooner
    /// (e.g. an opened jar moved to the fridge).
    /// </summary>
    public async Task<ItemDto> MoveAsync(int id, MoveRequest req)
    {
        var item = await LoadItem(id);
        var target = await Locations.SingleOrDefaultAsync(l => l.Id == req.LocationId) ?? throw new BadRequestException("Unknown location");
        var from = item.Location!.Kind;
        var to = target.Kind;
        var today = time.Today();
        var product = item.Product!;

        var type = InventoryEventType.Moved;
        if (from != LocationKind.Freezer && to == LocationKind.Freezer)
        {
            type = InventoryEventType.Frozen;
            item.FrozenAt = today;
            if (req.AdjustExpiry && Categories.ShelfLifeDays(product, LocationKind.Freezer) is int fd)
                item.ExpiresAt = today.AddDays(fd);
        }
        else if (from == LocationKind.Freezer && to != LocationKind.Freezer)
        {
            type = InventoryEventType.Thawed;
            item.FrozenAt = null;
            if (req.AdjustExpiry) item.ExpiresAt = today.AddDays(Categories.Get(product.Category).ThawedDays);
        }
        else if (req.AdjustExpiry && from != to && Categories.ShelfLifeDays(product, to) is int days)
        {
            var candidate = today.AddDays(days);
            if (item.ExpiresAt is null || candidate < item.ExpiresAt) item.ExpiresAt = candidate;
        }

        item.LocationId = target.Id;
        item.Location = target;
        await LogAsync(item, type, item.Quantity, save: false);
        await db.SaveChangesAsync();
        return item.ToDto();
    }

    /// <summary>Delete a mistaken entry (no usage/waste recorded).</summary>
    public async Task DeleteItemAsync(int id)
    {
        var item = await LoadItem(id);
        db.Remove(item);
        await db.SaveChangesAsync();
    }

    // ---------- Dashboard ----------

    public async Task<InventoryDashboardDto> GetDashboardAsync()
    {
        var today = time.Today();
        var items = await Items.ToListAsync();
        var locations = await Locations.OrderBy(l => l.Sort).ToListAsync();

        bool InFreezer(InventoryItem i) => i.Location!.Kind == LocationKind.Freezer;
        static IReadOnlyList<ItemDto> BySoonest(IEnumerable<InventoryItem> xs) =>
            xs.OrderBy(i => i.ExpiresAt).ThenBy(i => i.Product!.Name).Select(i => i.ToDto()).ToList();

        var notFrozen = items.Where(i => !InFreezer(i) && i.ExpiresAt is not null).ToList();
        var expired = notFrozen.Where(i => i.ExpiresAt < today);
        var freezeSoon = notFrozen.Where(i =>
            i.Location!.Kind == LocationKind.Fridge && Categories.IsFreezable(i.Product!) &&
            i.ExpiresAt >= today && i.ExpiresAt <= today.AddDays(_opt.FreezeWindowDays)).ToList();
        var freezeIds = freezeSoon.Select(i => i.Id).ToHashSet();
        var expiringSoon = notFrozen.Where(i =>
            !freezeIds.Contains(i.Id) && i.ExpiresAt >= today && i.ExpiresAt <= today.AddDays(_opt.ExpiringSoonDays));

        var longInFreezer = items.Where(InFreezer)
            .Select(i => (Item: i, FreezerDays: Categories.ShelfLifeDays(i.Product!, LocationKind.Freezer),
                DaysFrozen: today.DayNumber - (i.FrozenAt ?? i.PurchasedAt ?? DateOnly.FromDateTime(i.CreatedAt)).DayNumber))
            .Where(x => x.FreezerDays is > 0 && x.DaysFrozen >= x.FreezerDays * _opt.FreezerWarnRatio)
            .OrderByDescending(x => (double)x.DaysFrozen / x.FreezerDays!.Value)
            .Select(x => new LongFrozenDto(x.Item.ToDto(), x.DaysFrozen, x.FreezerDays!.Value))
            .ToList();

        var lowStock = await GetLowStockAsync();

        var counts = locations.Select(l => new LocationCountDto(l.Id, l.Name, l.Kind, items.Count(i => i.LocationId == l.Id))).ToList();

        return new InventoryDashboardDto(BySoonest(expired), BySoonest(expiringSoon), BySoonest(freezeSoon), longInFreezer, lowStock, counts);
    }

    /// <summary>Products with a minimum quantity whose total on hand is below it.</summary>
    public async Task<List<LowStockDto>> GetLowStockAsync()
    {
        var tracked = await Products.Where(p => p.MinQuantity != null).ToListAsync();
        if (tracked.Count == 0) return [];
        var ids = tracked.Select(p => p.Id).ToList();
        var onHand = (await db.Set<InventoryItem>().Where(i => i.HouseholdId == Hid && ids.Contains(i.ProductId))
                .Select(i => new { i.ProductId, i.Quantity }).ToListAsync())
            .GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
        return tracked
            .Select(p => new LowStockDto(p.ToDto(), onHand.GetValueOrDefault(p.Id)))
            .Where(x => x.Quantity < x.Product.MinQuantity)
            .OrderBy(x => x.Product.Name).ToList();
    }

    /// <summary>On-hand totals per product, with the soonest non-freezer expiry. Used by recipe availability.</summary>
    public async Task<List<StockDto>> GetStockAsync()
    {
        var rows = await db.Set<InventoryItem>().Where(i => i.HouseholdId == Hid)
            .Select(i => new { i.ProductId, i.Quantity, i.ExpiresAt, Kind = i.Location!.Kind }).ToListAsync();
        return rows.GroupBy(r => r.ProductId).Select(g => new StockDto(g.Key, g.Sum(r => r.Quantity),
            g.Where(r => r.Kind != LocationKind.Freezer && r.ExpiresAt != null).Select(r => r.ExpiresAt).Min())).ToList();
    }

    public async Task<List<ProductDto>> GetAllProductsAsync() =>
        (await Products.OrderBy(p => p.Name).ToListAsync()).Select(p => p.ToDto()).ToList();

    /// <summary>Items of the given products, soonest-expiring first (freezer last).</summary>
    public async Task<List<ItemDto>> GetItemsForProductsAsync(IReadOnlyCollection<int> productIds)
    {
        if (productIds.Count == 0) return [];
        var items = await Items.Where(i => productIds.Contains(i.ProductId)).ToListAsync();
        return items.OrderBy(i => i.Location!.Kind == LocationKind.Freezer).ThenBy(i => i.ExpiresAt ?? DateOnly.MaxValue)
            .Select(i => i.ToDto()).ToList();
    }

    public async Task<ProductDto> GetProductAsync(int id) =>
        (await Products.SingleOrDefaultAsync(p => p.Id == id) ?? throw new NotFoundException("Product not found")).ToDto();

    /// <summary>Find a product by exact name (case-insensitive), for linking free-text entries.</summary>
    public async Task<ProductDto?> FindByNameAsync(string name)
    {
        var n = name.Trim().ToLower();
        return (await Products.FirstOrDefaultAsync(p => p.Name.ToLower() == n))?.ToDto();
    }

    // ---------- helpers ----------

    public static DateOnly? SuggestExpiry(Product product, LocationKind kind, InventoryItem item)
    {
        var days = Categories.ShelfLifeDays(product, kind);
        if (days is null) return null;
        var start = kind == LocationKind.Freezer ? item.FrozenAt ?? item.PurchasedAt : item.PurchasedAt;
        return start?.AddDays(days.Value);
    }

    private async Task<InventoryItem> LoadItem(int id) =>
        await Items.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Item not found");

    private async Task LogAsync(InventoryItem item, InventoryEventType type, double qty, bool save = true)
    {
        db.Add(new InventoryEvent
        {
            HouseholdId = Hid, ProductId = item.ProductId, ItemId = item.Id, UserId = me.UserId,
            Type = type, Quantity = qty, Unit = item.Unit,
        });
        if (save) await db.SaveChangesAsync();
    }
}
