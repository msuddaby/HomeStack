using HomeStack.Core;
using HomeStack.Core.Notifications;
using static HomeStack.Core.Notifications.DigestText;

namespace HomeStack.Modules.Inventory;

/// <summary>Daily digest: what to freeze, what's expiring or expired, and what's been frozen too long.</summary>
public class InventoryDigest(InventoryService inventory, TimeProvider time) : IDigestSource
{
    public int Order => 10;

    public async Task<IReadOnlyList<DigestSection>> GetSectionsAsync(CancellationToken ct)
    {
        var today = time.Today();
        var d = await inventory.GetDashboardAsync();
        static string Name(ItemDto i) => i.Product.Name;

        return
        [
            new("Freeze it or use it",
                d.FreezeSoon.Select(i => $"{Name(i)} (expires {When(i.ExpiresAt!.Value, today)})").ToList(),
                d.FreezeSoon.Count > 0 ? $"{d.FreezeSoon.Count} to freeze" : null, Urgent: d.FreezeSoon.Count > 0),
            new("Use soon",
                d.ExpiringSoon.Select(i => $"{Name(i)} (expires {When(i.ExpiresAt!.Value, today)})").ToList(),
                d.ExpiringSoon.Count > 0 ? $"{d.ExpiringSoon.Count} expiring" : null),
            new("Past its date",
                d.Expired.Select(i => $"{Name(i)} ({When(i.ExpiresAt!.Value, today)})").ToList(),
                d.Expired.Count > 0 ? $"{d.Expired.Count} expired" : null),
            new("Long in the freezer",
                d.LongInFreezer.Select(x => $"{Name(x.Item)} ({Age(x.DaysFrozen)})").ToList()),
        ];
    }

    private static string Age(int days) => days switch
    {
        < 14 => Plural(days, "day", "days"),
        < 60 => Plural(days / 7, "week", "weeks"),
        _ => Plural(days / 30, "month", "months"),
    };
}
