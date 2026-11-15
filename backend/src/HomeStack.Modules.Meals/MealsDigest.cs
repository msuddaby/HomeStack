using HomeStack.Core;
using HomeStack.Core.Notifications;
using Microsoft.EntityFrameworkCore;

namespace HomeStack.Modules.Meals;

/// <summary>Daily digest: today's planned (not yet cooked) meals.</summary>
public class MealsDigest(HomeStackDbContext db, ICurrentUser me, TimeProvider time) : IDigestSource
{
    public int Order => 20;

    public async Task<IReadOnlyList<DigestSection>> GetSectionsAsync(CancellationToken ct)
    {
        var today = time.Today();
        var entries = await db.Set<MealPlanEntry>().Include(e => e.Recipe)
            .Where(e => e.HouseholdId == me.HouseholdId && e.Date == today && e.CookedAt == null)
            .ToListAsync(ct);
        var lines = entries.OrderBy(e => e.Slot)
            .Select(e => $"{e.Slot}: {e.Recipe?.Title ?? e.Title ?? "Something"}").ToList();
        return [new DigestSection("On the menu today", lines)];
    }
}
