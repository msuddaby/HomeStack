using Microsoft.EntityFrameworkCore;

namespace HomeStack.Core.Notifications;

/// <summary>
/// A module's contribution to the daily digest. Register implementations as scoped
/// <c>IDigestSource</c> in <see cref="IModule.AddServices"/>; they run with the household's
/// <see cref="ICurrentUser"/>, so normal household-scoped services work.
/// </summary>
public interface IDigestSource
{
    /// <summary>Lower comes first in the message.</summary>
    int Order => 100;
    Task<IReadOnlyList<DigestSection>> GetSectionsAsync(CancellationToken ct);
}

/// <param name="Heading">e.g. "Freeze today".</param>
/// <param name="Lines">One line per thing, e.g. "Chicken thighs (expires tomorrow)".</param>
/// <param name="Summary">Short count for the notification title, e.g. "2 to freeze".</param>
/// <param name="Urgent">Raises the notification priority.</param>
public record DigestSection(string Heading, IReadOnlyList<string> Lines, string? Summary = null, bool Urgent = false);

public record DigestMessage(string Title, string Body, bool Urgent, bool IsEmpty);

/// <summary>Per-household notification settings (one row per household).</summary>
public class NotificationSettings
{
    public int HouseholdId { get; set; }
    public bool Enabled { get; set; }
    public string ServerUrl { get; set; } = "https://ntfy.sh";
    public string Topic { get; set; } = "";
    public string? AccessToken { get; set; }
    /// <summary>Local hour (0–23) the daily digest goes out.</summary>
    public int DigestHour { get; set; } = 8;
    /// <summary>Where the app is reached from (captured when settings are saved); tapping the notification opens it.</summary>
    public string? AppUrl { get; set; }
    public DateOnly? LastSentOn { get; set; }
    public DateTime? LastSentAt { get; set; }
    public string? LastError { get; set; }

    internal static void Configure(ModelBuilder mb) => mb.Entity<NotificationSettings>(e =>
    {
        e.ToTable("notification_settings");
        e.HasKey(x => x.HouseholdId);
        e.Property(x => x.HouseholdId).ValueGeneratedNever();
        e.Property(x => x.ServerUrl).HasMaxLength(200);
        e.Property(x => x.Topic).HasMaxLength(64);
        e.Property(x => x.AccessToken).HasMaxLength(200);
        e.Property(x => x.AppUrl).HasMaxLength(200);
        e.Property(x => x.LastError).HasMaxLength(500);
        e.HasOne<Data.Household>().WithOne().HasForeignKey<NotificationSettings>(x => x.HouseholdId).OnDelete(DeleteBehavior.Cascade);
    });
}

public static class DigestText
{
    /// <summary>"today", "tomorrow", "Mon", or "Nov 21".</summary>
    public static string When(DateOnly date, DateOnly today) => (date.DayNumber - today.DayNumber) switch
    {
        0 => "today",
        1 => "tomorrow",
        -1 => "yesterday",
        > 1 and < 7 => date.ToString("ddd", System.Globalization.CultureInfo.InvariantCulture),
        _ => date.ToString("MMM d", System.Globalization.CultureInfo.InvariantCulture),
    };

    public static string Plural(int n, string one, string many) => $"{n} {(n == 1 ? one : many)}";
}
