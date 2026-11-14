using HomeStack.Core;
using HomeStack.Modules.Inventory;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Shopping;

public class ShoppingService(HomeStackDbContext db, ICurrentUser me, InventoryService inventory)
{
    private int Hid => me.HouseholdId;
    private IQueryable<ShoppingItem> Items => db.Set<ShoppingItem>().Where(i => i.HouseholdId == Hid);

    /// <summary>The visible list. Brings low-stock auto items up to date first.</summary>
    public async Task<List<ShoppingItemDto>> GetListAsync()
    {
        await SyncLowStockAsync();
        var items = await Items.Where(i => i.DismissedAt == null).OrderBy(i => i.CreatedAt).ToListAsync();
        return await ToDtos(items);
    }

    /// <summary>
    /// Adds an auto item for each low-stock product that isn't already on the list, and removes
    /// unchecked/dismissed auto items whose product has been restocked.
    /// </summary>
    public async Task SyncLowStockAsync()
    {
        var low = await inventory.GetLowStockAsync();
        var lowIds = low.Select(l => l.Product.Id).ToHashSet();
        var linked = await Items.Where(i => i.ProductId != null).ToListAsync();

        var restocked = linked.Where(i => i.Source == ShoppingSource.LowStock && i.CheckedAt == null && !lowIds.Contains(i.ProductId!.Value));
        db.RemoveRange(restocked);

        var present = linked.Select(i => i.ProductId!.Value).ToHashSet();
        foreach (var l in low.Where(l => !present.Contains(l.Product.Id)))
        {
            var p = l.Product;
            var need = p.MinQuantity!.Value - l.Quantity;
            db.Add(new ShoppingItem
            {
                HouseholdId = Hid, Name = p.Name, ProductId = p.Id, Category = p.Category,
                Quantity = need > 0 ? Math.Round(need, 3) : null, Unit = p.DefaultUnit,
                Source = ShoppingSource.LowStock, AddedByUserId = me.UserId,
            });
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Adds an item, merging with an unchecked entry for the same product/name.</summary>
    public async Task<ShoppingItemDto> AddAsync(AddShoppingItemRequest req)
    {
        var product = req.ProductId is int pid ? await inventory.GetProductAsync(pid)
            : await inventory.FindByNameAsync(req.Name!);
        var name = product?.Name ?? req.Name!.Trim();

        var existing = await Items.Where(i => i.CheckedAt == null && (product != null ? i.ProductId == product.Id : i.Name.ToLower() == name.ToLower()))
            .FirstOrDefaultAsync();
        if (existing is not null)
        {
            existing.DismissedAt = null;
            existing.Source = ShoppingSource.Manual;
            if (req.Quantity is double q) existing.Quantity = (existing.Quantity ?? 0) + q;
            if (!string.IsNullOrWhiteSpace(req.Note)) existing.Note = req.Note.Trim();
            await db.SaveChangesAsync();
            return (await ToDtos([existing]))[0];
        }

        var item = new ShoppingItem
        {
            HouseholdId = Hid, Name = name, ProductId = product?.Id,
            Category = product?.Category ?? req.Category ?? Categories.Guess(name),
            Quantity = req.Quantity, Unit = Blank(req.Unit) ?? product?.DefaultUnit, Note = Blank(req.Note),
            Source = ShoppingSource.Manual, AddedByUserId = me.UserId,
        };
        db.Add(item);
        await db.SaveChangesAsync();
        return (await ToDtos([item]))[0];
    }

    public async Task<ShoppingItemDto> UpdateAsync(int id, UpdateShoppingItemRequest req)
    {
        var item = await Load(id);
        item.Name = req.Name.Trim();
        item.Category = req.Category;
        item.Quantity = req.Quantity;
        item.Unit = Blank(req.Unit);
        item.Note = Blank(req.Note);
        await db.SaveChangesAsync();
        return (await ToDtos([item]))[0];
    }

    public async Task<ShoppingItemDto> CheckAsync(int id, bool isChecked)
    {
        var item = await Load(id);
        item.CheckedAt = isChecked ? DateTime.UtcNow : null;
        item.CheckedByUserId = isChecked ? me.UserId : null;
        await db.SaveChangesAsync();
        return (await ToDtos([item]))[0];
    }

    /// <summary>Removes an item. Auto (low-stock) items are hidden instead, so they don't come straight back.</summary>
    public async Task RemoveAsync(int id)
    {
        var item = await Load(id);
        if (item.Source == ShoppingSource.LowStock && item.CheckedAt == null) item.DismissedAt = DateTime.UtcNow;
        else db.Remove(item);
        await db.SaveChangesAsync();
    }

    /// <summary>Clears all checked items without adding them to inventory.</summary>
    public async Task<int> ClearCheckedAsync()
    {
        var done = await Items.Where(i => i.CheckedAt != null).ToListAsync();
        db.RemoveRange(done);
        await db.SaveChangesAsync();
        return done.Count;
    }

    /// <summary>Moves bought items into inventory (creating products for free-text items) and clears them from the list.</summary>
    public async Task<PutAwayResult> PutAwayAsync(PutAwayRequest req)
    {
        var ids = req.Lines.Select(l => l.ShoppingItemId).ToList();
        var rows = await Items.Where(i => ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id);
        var missing = ids.Where(id => !rows.ContainsKey(id)).ToList();
        if (missing.Count > 0) throw new NotFoundException($"Shopping item {missing[0]} not found");

        await using var tx = await db.Database.BeginTransactionAsync();
        var added = new List<ItemDto>();
        foreach (var line in req.Lines)
        {
            var row = rows[line.ShoppingItemId];
            if (line.AddToInventory)
            {
                // Reuse a product with the same name if one appeared since the item was listed.
                var productId = row.ProductId ?? (await inventory.FindByNameAsync(row.Name))?.Id;
                var item = await inventory.AddItemAsync(new AddItemRequest(
                    ProductId: productId,
                    Product: productId is null ? new ProductRequest(row.Name, null, row.Category, null, row.Unit, null, null, null, null, null) : null,
                    LocationId: line.LocationId!.Value,
                    Quantity: line.Quantity ?? row.Quantity ?? 1,
                    Unit: line.Unit ?? row.Unit,
                    ExpiresAt: line.ExpiresAt,
                    NoExpiry: line.NoExpiry,
                    FrozenAt: line.FrozenAt));
                added.Add(item);
            }
            db.Remove(row);
        }
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new PutAwayResult(added.Count, req.Lines.Count - added.Count, added);
    }

    // ---------- helpers ----------

    private async Task<ShoppingItem> Load(int id) =>
        await Items.SingleOrDefaultAsync(i => i.Id == id) ?? throw new NotFoundException("Shopping item not found");

    private async Task<List<ShoppingItemDto>> ToDtos(List<ShoppingItem> items)
    {
        var productIds = items.Where(i => i.ProductId != null).Select(i => i.ProductId!.Value).Distinct().ToList();
        var products = productIds.Count == 0 ? new Dictionary<int, ProductDto>()
            : (await db.Set<Product>().Where(p => p.HouseholdId == Hid && productIds.Contains(p.Id)).ToListAsync())
                .ToDictionary(p => p.Id, p => p.ToDto());
        var userIds = items.Where(i => i.CheckedByUserId != null).Select(i => i.CheckedByUserId!.Value).Distinct().ToList();
        var users = userIds.Count == 0 ? new Dictionary<int, string>()
            : await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);

        return items.Select(i => new ShoppingItemDto(i.Id, i.Name, i.ProductId, i.Category, i.Quantity, i.Unit, i.Note, i.Source,
            i.CreatedAt, i.CheckedAt, i.CheckedByUserId is int u ? users.GetValueOrDefault(u) : null,
            i.ProductId is int p ? products.GetValueOrDefault(p) : null)).ToList();
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
