using HomeStack.Core;
using HomeStack.Core.Images;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Meals;

/// <summary>Recipes with a web picture get a local copy (see <see cref="ImageCache"/>).</summary>
public class RecipeImages(HomeStackDbContext db) : IRemoteImageSource
{
    /// <summary>Big enough for the full-width photo on a phone's recipe page.</summary>
    public const int Edge = 1000;
    public int MaxEdge => Edge;

    // Runs for every household from the background job, so there's no household filter here.
    public Task<List<RemoteImage>> PendingAsync(int max, CancellationToken ct) =>
        db.Set<Recipe>().Where(r => r.ImageUrl != null && r.ImageId == null).OrderBy(r => r.Id).Take(max)
            .Select(r => new RemoteImage(r.Id, r.HouseholdId, r.ImageUrl!)).ToListAsync(ct);

    public async Task<bool> AttachAsync(RemoteImage image, Guid imageId, CancellationToken ct) =>
        await db.Set<Recipe>()
            .Where(r => r.Id == image.OwnerId && r.HouseholdId == image.HouseholdId && r.ImageUrl == image.Url && r.ImageId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ImageId, imageId), ct) == 1;
}
