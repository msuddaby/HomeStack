using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeStack.Core.Notifications;

public class DigestService(HomeStackDbContext db, ICurrentUser me, IEnumerable<IDigestSource> sources, NtfyClient ntfy, TimeProvider time)
{
    private const int MaxLinesPerSection = 8;

    public async Task<NotificationSettings?> GetSettingsAsync(CancellationToken ct = default) =>
        await db.Set<NotificationSettings>().SingleOrDefaultAsync(s => s.HouseholdId == me.HouseholdId, ct);

    public async Task<DigestMessage> BuildAsync(CancellationToken ct = default)
    {
        var sections = new List<DigestSection>();
        foreach (var source in sources.OrderBy(s => s.Order))
            sections.AddRange((await source.GetSectionsAsync(ct)).Where(s => s.Lines.Count > 0));

        if (sections.Count == 0)
            return new DigestMessage("HomeStack", "Nothing needs attention today.", false, true);

        var summaries = sections.Select(s => s.Summary).OfType<string>().ToList();
        var title = summaries.Count > 0 ? string.Join(" · ", summaries) : "HomeStack today";
        var body = new StringBuilder();
        foreach (var s in sections)
        {
            if (body.Length > 0) body.Append("\n\n");
            body.Append(s.Heading);
            foreach (var line in s.Lines.Take(MaxLinesPerSection)) body.Append("\n• ").Append(line);
            if (s.Lines.Count > MaxLinesPerSection) body.Append($"\n• …and {s.Lines.Count - MaxLinesPerSection} more");
        }
        return new DigestMessage(title, body.ToString(), sections.Any(s => s.Urgent), false);
    }

    /// <summary>Sends the digest now, even if empty. Throws <see cref="BadRequestException"/> if it can't be delivered.</summary>
    public async Task<DigestMessage> SendTestAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null || string.IsNullOrWhiteSpace(settings.Topic)) throw new BadRequestException("Save a topic first.");
        var message = await BuildAsync(ct);
        await DeliverAsync(settings, message, ct);
        await db.SaveChangesAsync(ct);
        if (settings.LastError is { } error) throw new BadRequestException(error);
        return message;
    }

    /// <summary>The scheduled daily send. Skips empty digests; records success or the error.</summary>
    public async Task SendDailyAsync(CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);
        if (settings is null || !settings.Enabled || string.IsNullOrWhiteSpace(settings.Topic)) return;
        var message = await BuildAsync(ct);
        if (!message.IsEmpty) await DeliverAsync(settings, message, ct);
        else settings.LastError = null;
        // Only count the day as done once it got through (or there was nothing to say); failures retry on the next tick.
        if (settings.LastError is null) settings.LastSentOn = time.Today();
        await db.SaveChangesAsync(ct);
    }

    private async Task DeliverAsync(NotificationSettings settings, DigestMessage message, CancellationToken ct)
    {
        try
        {
            await ntfy.PublishAsync(settings, message, ct);
            settings.LastSentAt = time.GetUtcNow().UtcDateTime;
            settings.LastError = null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            settings.LastError = ex is TaskCanceledException ? "Timed out reaching the ntfy server." : ex.Message;
        }
    }
}

/// <summary>Publishes to an ntfy server (https://ntfy.sh or self-hosted) using its JSON API.</summary>
public class NtfyClient(HttpClient http)
{
    public async Task PublishAsync(NotificationSettings s, DigestMessage m, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, s.ServerUrl.TrimEnd('/') + "/")
        {
            Content = JsonContent.Create(new
            {
                topic = s.Topic,
                title = m.Title,
                message = m.Body,
                priority = m.Urgent ? 4 : 3,
                tags = new[] { m.Urgent ? "warning" : "house" },
                click = s.AppUrl,
            }),
        };
        if (!string.IsNullOrEmpty(s.AccessToken)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.AccessToken);
        using var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            var detail = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"ntfy answered {(int)res.StatusCode}: {Truncate(detail, 200)}");
        }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s.Trim() : s[..n].Trim() + "…";
}

/// <summary>Every few minutes, sends each household's digest once its hour has come (within a 4-hour window).</summary>
internal sealed class DigestWorker(IServiceScopeFactory scopes, IConfiguration config, TimeProvider time, ILogger<DigestWorker> logger)
    : PeriodicWorker(scopes, config, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    protected override async Task TickAsync(IServiceProvider services, CancellationToken ct)
    {
        var db = services.GetRequiredService<HomeStackDbContext>();
        var now = time.GetLocalNow();
        var today = DateOnly.FromDateTime(now.DateTime);
        var due = await db.Set<NotificationSettings>().AsNoTracking()
            .Where(s => s.Enabled && (s.LastSentOn == null || s.LastSentOn < today))
            .Select(s => new { s.HouseholdId, s.DigestHour }).ToListAsync(ct);

        foreach (var s in due.Where(s => now.Hour >= s.DigestHour && now.Hour < s.DigestHour + 4))
        {
            await using var scope = Scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<HouseholdScope>().Enter(s.HouseholdId);
            await scope.ServiceProvider.GetRequiredService<DigestService>().SendDailyAsync(ct);
        }
    }
}
