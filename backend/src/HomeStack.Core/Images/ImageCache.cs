using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HomeStack.Core.Images;

/// <summary>Something that should get a local copy of a picture on the web.</summary>
public record RemoteImage(int OwnerId, int HouseholdId, string Url);

/// <summary>
/// A module's records with web images (products, recipes). <see cref="ImageCache"/> asks each source
/// what still needs a local copy and hands back the stored image's id.
/// </summary>
public interface IRemoteImageSource
{
    /// <summary>Longest edge, in pixels, of the local copy.</summary>
    int MaxEdge { get; }
    /// <summary>Records that have an image link but no local copy yet.</summary>
    Task<List<RemoteImage>> PendingAsync(int max, CancellationToken ct);
    /// <summary>
    /// Points the record at its local copy. Returns false if the record changed in the meantime
    /// (different link, or a photo was uploaded), so the copy isn't wanted.
    /// </summary>
    Task<bool> AttachAsync(RemoteImage image, Guid imageId, CancellationToken ct);
}

/// <summary>Links that failed to download, so they aren't retried every minute.</summary>
public sealed class ImageFetchFailures
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromHours(6);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _until = new();

    public bool ShouldSkip(string url, DateTimeOffset now) => _until.TryGetValue(url, out var t) && t > now;
    public void Failed(string url, DateTimeOffset now) => _until[url] = now + RetryAfter;
}

/// <summary>Downloads pictures, refusing anything that isn't http(s) or is too big.</summary>
public class ImageFetcher(HttpClient http)
{
    public async Task<byte[]?> GetAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return null;
        using var res = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!res.IsSuccessStatusCode || res.Content.Headers.ContentLength > Thumbnails.MaxBytes) return null;

        await using var stream = await res.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + read > Thumbnails.MaxBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }
}

/// <summary>Makes local copies of web images that don't have one yet.</summary>
public class ImageCache(
    IEnumerable<IRemoteImageSource> sources, ImageService images, ImageFetcher fetcher, ImageFetchFailures failures,
    TimeProvider time, ILogger<ImageCache> logger)
{
    private const int BatchSize = 20;

    /// <summary>Returns how many images were copied.</summary>
    public async Task<int> CacheMissingAsync(CancellationToken ct)
    {
        var copied = 0;
        foreach (var source in sources)
        {
            var now = time.GetUtcNow();
            var pending = (await source.PendingAsync(BatchSize * 4, ct)).Where(p => !failures.ShouldSkip(p.Url, now)).Take(BatchSize);
            foreach (var p in pending)
            {
                byte[]? data;
                try { data = await fetcher.GetAsync(p.Url, ct); }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
                {
                    logger.LogInformation("Couldn't download image {Url}: {Error}", p.Url, ex.Message);
                    data = null;
                }

                StoredImage? stored = null;
                if (data is not null)
                {
                    try { stored = await images.SaveAsync(p.HouseholdId, data, source.MaxEdge, p.Url, ct); }
                    catch (BadRequestException) { logger.LogInformation("Image {Url} isn't a picture we can read", p.Url); }
                }
                if (stored is null)
                {
                    failures.Failed(p.Url, time.GetUtcNow());
                    continue;
                }

                if (await source.AttachAsync(p, stored.Id, ct)) copied++;
                else await images.DeleteAsync(stored.Id, ct);
            }
        }
        return copied;
    }
}

internal sealed class ImageCacheWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<ImageCacheWorker> logger)
    : PeriodicWorker(scopes, config, logger)
{
    protected override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override Task TickAsync(IServiceProvider services, CancellationToken ct) =>
        services.GetRequiredService<ImageCache>().CacheMissingAsync(ct);
}
