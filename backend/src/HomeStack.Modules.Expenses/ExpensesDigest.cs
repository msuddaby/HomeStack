using HomeStack.Core.Notifications;

namespace HomeStack.Modules.Expenses;

/// <summary>Daily digest: things you buy regularly that are about due again.</summary>
public class ExpensesDigest(ExpensesService expenses) : IDigestSource
{
    public int Order => 40;

    public async Task<IReadOnlyList<DigestSection>> GetSectionsAsync(CancellationToken ct)
    {
        var due = await expenses.GetDueAsync();
        var lines = due.Take(8).Select(i => $"{i.Name}: usually every {i.Rhythm!.EveryDays} days, last bought {i.Rhythm.DaysSinceLast} days ago").ToList();
        return [new DigestSection("Usually buy about now", lines)];
    }
}
