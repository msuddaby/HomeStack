using HomeStack.Core;
using HomeStack.Core.Images;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Inventory;

/// <summary>Products with a web picture get a small local copy (see <see cref="ImageCache"/>).</summary>
public class ProductImages(HomeStackDbContext db) : IRemoteImageSource
{
    public int MaxEdge => InventoryService.ImageEdge;

    // Runs for every household from the background job, so there's no household filter here.
    public Task<List<RemoteImage>> PendingAsync(int max, CancellationToken ct) =>
        db.Set<Product>().Where(p => p.ImageUrl != null && p.ImageId == null).OrderBy(p => p.Id).Take(max)
            .Select(p => new RemoteImage(p.Id, p.HouseholdId, p.ImageUrl!)).ToListAsync(ct);

    public async Task<bool> AttachAsync(RemoteImage image, Guid imageId, CancellationToken ct) =>
        await db.Set<Product>()
            .Where(p => p.Id == image.OwnerId && p.HouseholdId == image.HouseholdId && p.ImageUrl == image.Url && p.ImageId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ImageId, imageId), ct) == 1;
}
